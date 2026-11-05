using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SteadyCuesV2
{
    // Separate compositing layer: never blocks mouse input or steals keyboard focus.
    internal sealed class CueOverlay : Form
    {
        const int WS_EX_TRANSPARENT=0x20,WS_EX_NOACTIVATE=0x08000000,WS_EX_TOOLWINDOW=0x80;
        static readonly Color KeyColor=Color.FromArgb(255,0,255);
        readonly SensorService sensors;
        readonly AppConfig config;
        readonly bool leftEdge;
        readonly Timer timer=new Timer();
        double ox,oy,vx,vy,visibility,holdUntil;
        readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
        double lastTime;
        bool configured;
        double visualTestUntil;
        public void BeginVisualTest() { visualTestUntil=clock.Elapsed.TotalSeconds+4.0; }

        public CueOverlay(Screen screen, SensorService s, AppConfig c, bool left)
        {
            sensors=s;config=c;leftEdge=left;
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.Manual;
            // Two narrow edge strips instead of a full-screen transparent window.
            // Painting cost is proportional to strip area, not total display area.
            int strip=Math.Max(190,Math.Min(330,screen.Bounds.Width/4));
            Bounds=new Rectangle(left?screen.Bounds.Left:screen.Bounds.Right-strip,screen.Bounds.Top,strip,screen.Bounds.Height);
            ShowInTaskbar=false;TopMost=true;BackColor=KeyColor;TransparencyKey=KeyColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
            timer.Interval=16;
            timer.Tick+=Tick;
            timer.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p=base.CreateParams; p.ExStyle|=WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW;return p; }
        }
        void Tick(object sender,EventArgs e)
        {
            if(IsDisposed)return;
            double now=clock.Elapsed.TotalSeconds;
            double dt=lastTime>0?Math.Max(0.005,Math.Min(.05,now-lastTime)):.016;
            lastTime=now;
            var data=sensors.Snapshot();
            if(data.Moving)holdUntil=now+3.0;
            bool live=data.Fresh && data.Samples>0;
            bool testing=now<visualTestUntil;
            bool visible=testing || config.Mode==1 || (config.Mode==0 && live && now<holdUntil);
            if(config.Mode==2 && !testing) visible=false;
            double target=visible?1:0;
            double fadeSpeed=target>visibility?dt/.22:dt/.50;
            visibility+=Math.Max(-fadeSpeed,Math.Min(fadeSpeed,target-visibility));
            // Accelerations are m/s2; 2 m/s2 = about 90 px by default.
            // Sensitivity is explicit, not coupled to automatic visibility.
            // Controlled visual-only test is never fed to the sensor engine.
            double tx=testing?90*Math.Sin(now*3.2):(live?Math.Tanh(data.X*config.Gain/4.0)*135:0);
            double ty=testing?65*Math.Cos(now*2.5):(live?Math.Tanh(data.Y*config.Gain/4.0)*135:0);
            double w=2*Math.PI*2.7,damping=.89;
            int steps=4;double h=dt/steps;
            for(int i=0;i<steps;i++){
                vx+=(w*w*(tx-ox)-2*damping*w*vx)*h;
                vy+=(w*w*(ty-oy)-2*damping*w*vy)*h;
                ox+=vx*h;oy+=vy*h;
            }
            if(!configured) { configured=true; try { Win32.SetWindowDisplayAffinity(Handle, config.CaptureSafe?0x11u:0u); }catch{} }
            if(visibility>.002)Invalidate();
            else Invalidate();
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(KeyColor); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(KeyColor);
            if(visibility<.003)return;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            int rows=config.DotCount;
            double step=(double)Height/(rows+1);
            double radius=config.DotSize*Math.Max(1,DeviceDpi/96.0);
            int columns=2; double edge=26*Math.Max(1,DeviceDpi/96.0);
            int alpha=(int)(Math.Max(0,Math.Min(1,visibility*config.Opacity))*220);
            using(var brush=new SolidBrush(Color.FromArgb(alpha,4,4,6)))
            using(var outline=new Pen(Color.FromArgb(alpha,4,4,6),1.2f))
            {
                for(int i=1;i<=rows;i++) {
                    double y=i*step+oy;
                    for(int col=0;col<columns;col++){
                        double x=leftEdge?edge+col*step*.57+ox:Width-edge-col*step*.57+ox;
                        PaintDot(g,brush,outline,x,y,radius);
                    }
                }
            }
        }
        static void PaintDot(Graphics g,Brush brush,Pen edge,double x,double y,double r)
        {
            if(y<0 || double.IsNaN(x) || double.IsNaN(y))return;
            float dx=(float)(x-r),dy=(float)(y-r),diameter=(float)(2*r);
            g.FillEllipse(brush,dx,dy,diameter,diameter);
            g.DrawEllipse(edge,dx,dy,diameter,diameter);
        }
        public void UpdateCapturePolicy()
        {
            try{Win32.SetWindowDisplayAffinity(Handle,config.CaptureSafe?0x11u:0u);}catch{}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){timer.Stop();timer.Dispose();}
            base.Dispose(disposing);
        }
        static class Win32
        {
            [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd,uint affinity);
        }
    }
    internal sealed class OverlayManager : IDisposable
    {
        readonly SensorService sensors;
        readonly AppConfig settings;
        readonly List<CueOverlay> windows=new List<CueOverlay>();
        public OverlayManager(SensorService s,AppConfig c){sensors=s;settings=c;}
        public void Start()
        {
            foreach(var screen in Screen.AllScreens) {
                if(!screen.Primary)continue;
                for(int edge=0;edge<2;edge++) {
                    var overlay=new CueOverlay(screen,sensors,settings,edge==0);
                    windows.Add(overlay);
                    overlay.Show();
                }
            }
        }
        public void CapturePolicy(){foreach(var w in windows)w.UpdateCapturePolicy();}
        public void BeginVisualTest(){foreach(var w in windows)w.BeginVisualTest();}
        public void Dispose(){foreach(var w in windows)w.Close();windows.Clear();}
    }
}
