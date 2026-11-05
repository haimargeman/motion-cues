using System;
using System.IO;
using System.Globalization;

namespace SteadyCuesV2
{
    internal struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static V3 operator *(V3 a, double n) { return new V3(a.X*n,a.Y*n,a.Z*n); }
        public static double Dot(V3 a, V3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static V3 Cross(V3 a, V3 b) { return new V3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public double Length { get { return Math.Sqrt(Dot(this,this)); } }
        public V3 Unit { get { var n=Length; return n>1e-9?this*(1/n):new V3(); } }
    }

    // The processing engine is deterministic and independent of Windows and the user interface.
    // Input: acceleration WITH gravity in m/s2, gyro angular speed in rad/s.
    internal sealed class MotionEngine
    {
        private V3 gravity, filtered;
        private bool initialized;
        private double lastTilt;
        private double clock;
        public int Direction; // 0, 1, 2, 3 quarter turns
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Strength { get; private set; }
        public bool Moving { get; private set; }

        public void Reset() { initialized=false; clock=0; lastTilt=-20; gravity=new V3(); filtered=new V3(); X=Y=Strength=0; Moving=false; }

        public void Process(V3 a, V3? gyro, double dt)
        {
            if (double.IsNaN(a.X)||double.IsNaN(a.Y)||double.IsNaN(a.Z)||
                double.IsInfinity(a.X)||double.IsInfinity(a.Y)||double.IsInfinity(a.Z)) return;
            if (dt<=0 || dt>0.3) dt=0.016;
            clock+=dt;
            if (!initialized) { gravity=a; initialized=true; }
            var up=gravity.Unit;
            bool tilting=false;
            if (gyro.HasValue)
            {
                var w=gyro.Value;
                double tilt=(w-up*V3.Dot(w,up)).Length;
                tilting=tilt>0.55;
            }
            if (tilting) lastTilt=clock;
            bool handling=clock-lastTilt<0.5;
            double tau=handling?0.28:gyro.HasValue?8.0:5.0;
            gravity=gravity+(a-gravity)*(1-Math.Exp(-dt/tau));
            if (gravity.Length<3) { X=Y=Strength=0; Moving=false; return; }
            var linear=a-gravity;
            filtered=filtered+(linear-filtered)*(1-Math.Exp(-dt/0.11));
            if (handling) { filtered=new V3(); X=Y=Strength=0; Moving=false; return; }
            up=gravity.Unit;
            // Screen upright: the back faces the vehicle's forward direction.
            // Screen horizontal: top edge faces forward. User can correct orientation.
            var y=new V3(0,1,0);
            var back=new V3(0,0,-1);
            double upright=Math.Abs(up.Z);
            double t=Math.Max(0,Math.Min(1,(upright-0.55)/0.35));
            t=t*t*(3-2*t);
            var f=(back-up*V3.Dot(back,up))*(1-t)+(y-up*V3.Dot(y,up))*t;
            if (f.Length<0.001) f=y-up*V3.Dot(y,up);
            if (f.Length<0.001) f=new V3(1,0,0);
            f=f.Unit;
            var right=V3.Cross(f,up).Unit;
            for (int i=0;i<(Direction%4+4)%4;i++) { var old=f; f=right; right=old*(-1); }
            // X right and Y passenger-back. A rearward felt force moves dots down.
            X=-V3.Dot(filtered,right);
            Y=V3.Dot(filtered,f);
            Strength=Math.Sqrt(X*X+Y*Y);
            Moving=Strength>0.12;
        }
    }

    internal sealed class AppConfig
    {
        public int Mode=1; // 0 automatic, 1 always-on, 2 hidden
        public int Direction;
        public double Gain=1.8;
        public double Opacity=0.86;
        public int DotSize=7;
        public int DotCount=13;
        public bool CaptureSafe=true;
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SteadyCuesV2"); } }
        public static AppConfig Load()
        {
            var c=new AppConfig();
            try {
                var path=Path.Combine(Folder,"settings.txt");
                if (!File.Exists(path)) return c;
                foreach(var l in File.ReadAllLines(path))
                {
                    var kv=l.Split(new[]{'='},2); if(kv.Length!=2)continue;
                    double n; if(!double.TryParse(kv[1],NumberStyles.Float,CultureInfo.InvariantCulture,out n))continue;
                    switch(kv[0]) {
                        case "mode":c.Mode=(int)Math.Max(0,Math.Min(2,n));break;
                        case "direction":c.Direction=(int)Math.Max(0,Math.Min(3,n));break;
                        case "gain":c.Gain=Math.Max(0.4,Math.Min(5,n));break;
                        case "opacity":c.Opacity=Math.Max(.2,Math.Min(1,n));break;
                        case "dotSize":c.DotSize=(int)Math.Max(3,Math.Min(16,n));break;
                        case "dotCount":c.DotCount=(int)Math.Max(7,Math.Min(22,n));break;
                    }
                }
            }catch{}
            return c;
        }
        public void Save()
        {
            try {
                Directory.CreateDirectory(Folder);
                var c=CultureInfo.InvariantCulture;
                File.WriteAllLines(Path.Combine(Folder,"settings.txt"),new[]{
                    "mode="+Mode,"direction="+Direction,"gain="+Gain.ToString(c),
                    "opacity="+Opacity.ToString(c),"dotSize="+DotSize,"dotCount="+DotCount
                });
            }catch{}
        }
    }

    internal sealed class SensorSnapshot
    {
        public bool Found, Gyro;
        public bool Fresh, Moving;
        public double Age=-1, Rate, X, Y, Strength;
        public long Samples;
        public string Error="";
    }
}
