using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteadyCuesV2
{
    // New dashboard: native Hebrew / RTL, responsive containers and live diagnostics.
    internal sealed class Dashboard : Form
    {
        readonly SensorService sensors;
        readonly AppConfig config;
        readonly OverlayManager overlays;
        readonly Timer refresh=new Timer { Interval=120 };
        readonly FlowLayoutPanel body;
        readonly Label headline,statTitle,statDetail,vector,hardware,helper,level;
        readonly Preview preview;
        readonly ComboBox mode,direction;
        readonly TrackBar sensitivity,size,number,opacity;
        readonly Label gainValue,sizeValue,numberValue,opacityValue;
        readonly Panel cardStatus,cardPreview,cardOptions,cardTech;
        static readonly Color Bg=Color.FromArgb(18,23,30);
        static readonly Color PanelColor=Color.FromArgb(29,37,48);
        static readonly Color Fore=Color.FromArgb(237,243,246);
        static readonly Color Muted=Color.FromArgb(167,184,195);
        static readonly Color Accent=Color.FromArgb(117,207,190);

        public Dashboard(SensorService sensor,AppConfig settings,OverlayManager ov)
        {
            sensors=sensor;config=settings;overlays=ov;
            Text="SteadyCues 2 — חיישנים מובנים";
            MinimumSize=new Size(790,640);
            Size=new Size(970,755);
            StartPosition=FormStartPosition.CenterScreen;
            BackColor=Bg;ForeColor=Fore;
            Font=new Font("Segoe UI",10);
            RightToLeft=RightToLeft.Yes;
            RightToLeftLayout=true;
            AutoScaleMode=AutoScaleMode.Dpi;
            var top=new Panel{Height=98,Dock=DockStyle.Top,BackColor=Bg,Padding=new Padding(24,14,24,8)};
            headline=Label("SteadyCues 2",22,FontStyle.Bold,Fore,42);
            headline.Dock=DockStyle.Top;
            var sub=Label("ממשק תנועה מקומי • ללא טלפון • ללא רשת • עברית מלאה",10,FontStyle.Regular,Muted,24);
            sub.Dock=DockStyle.Top;
            top.Controls.Add(sub);top.Controls.Add(headline);
            Controls.Add(top);

            body=new FlowLayoutPanel {
                Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
                WrapContents=false,AutoScroll=true,Padding=new Padding(22,8,22,22),
                BackColor=Bg
            };
            Controls.Add(body);
            body.BringToFront();
            body.SizeChanged+=(s,e)=>ResizeCards();

            cardStatus=Card(148);
            statTitle=Label("בודק חיישנים...",16,FontStyle.Bold,Fore,32);
            statDetail=Label("",10,FontStyle.Regular,Muted,42);
            vector=Label("",10,FontStyle.Regular,Accent,32);
            statTitle.Location=new Point(18,14);statTitle.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            statDetail.Location=new Point(18,53);statDetail.Anchor=statTitle.Anchor;
            vector.Location=new Point(18,105);vector.Anchor=statTitle.Anchor;
            cardStatus.Controls.Add(statTitle);cardStatus.Controls.Add(statDetail);cardStatus.Controls.Add(vector);
            body.Controls.Add(cardStatus);

            cardPreview=Card(185);
            var previewTitle=Label("תצוגה חיה של תנועת הנקודות",12,FontStyle.Bold,Fore,28);
            previewTitle.SetBounds(18,10,600,28);
            preview=new Preview(sensors,config) { Left=18,Top=42,Height=126,Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right };
            cardPreview.Controls.Add(previewTitle);cardPreview.Controls.Add(preview);
            body.Controls.Add(cardPreview);

            cardOptions=Card(250);
            var optTitle=Label("התאמה אישית",12,FontStyle.Bold,Fore,29);
            optTitle.SetBounds(18,10,600,29);cardOptions.Controls.Add(optTitle);
            mode=MakeCombo(new[]{"אוטומטי — מוצג בעת תנועה","מוצג תמיד — מומלץ לבדיקה","כבוי"},config.Mode);
            direction=MakeCombo(new[]{"כיוון רגיל","סיבוב 90°","כיוון הפוך (180°)","סיבוב 270°"},config.Direction);
            var modeLabel=Label("מצב הצגה",10,FontStyle.Regular,Muted,25);
            var directionLabel=Label("כיוון המחשב ביחס לרכב",10,FontStyle.Regular,Muted,25);
            cardOptions.Controls.Add(modeLabel);cardOptions.Controls.Add(mode);
            cardOptions.Controls.Add(directionLabel);cardOptions.Controls.Add(direction);
            sensitivity=MakeSlider(40,500,(int)Math.Round(config.Gain*100));
            gainValue=Label("",10,FontStyle.Bold,Accent,24);
            cardOptions.Controls.Add(sensitivity);cardOptions.Controls.Add(gainValue);
            var sensitivityLabel=Label("עוצמת תגובת הנקודות לתנועה",10,FontStyle.Regular,Muted,25);
            cardOptions.Controls.Add(sensitivityLabel);
            mode.SelectedIndexChanged+=(s,e)=>{config.Mode=mode.SelectedIndex;config.Save();};
            direction.SelectedIndexChanged+=(s,e)=>{config.Direction=direction.SelectedIndex;sensors.SetDirection(config.Direction);config.Save();};
            sensitivity.Scroll+=(s,e)=>{config.Gain=sensitivity.Value/100.0;gainValue.Text=config.Gain.ToString("0.00")+"×";config.Save();};
            gainValue.Text=config.Gain.ToString("0.00")+"×";
            // Explicit positions avoid nested auto-layout changes and unwanted text overlap.
            modeLabel.SetBounds(18,49,190,25);mode.SetBounds(18,78,272,33);
            directionLabel.SetBounds(325,49,240,25);direction.SetBounds(325,78,300,33);
            sensitivityLabel.SetBounds(18,130,400,27);
            sensitivity.SetBounds(18,157,440,42);gainValue.SetBounds(480,163,120,30);
            var calibrate=MakeButton("איפוס וכיול מחדש");
            calibrate.SetBounds(18,207,200,35);cardOptions.Controls.Add(calibrate);
            calibrate.Click+=(s,e)=>{sensors.Calibrate();MessageBox.Show(this,"הכיול אופס. השאירו את המחשב יציב לרגע.","כיול");};
            body.Controls.Add(cardOptions);

            cardTech=Card(166);
            var techTitle=Label("בדיקות חיישנים ומערכת",12,FontStyle.Bold,Fore,28);
            techTitle.SetBounds(18,12,600,28);
            hardware=Label("",10,FontStyle.Regular,Fore,32);hardware.SetBounds(18,47,620,33);
            helper=Label("לבדיקה: הניעו את המחשב בעדינות ובדקו שהערכים X ו־Y משתנים. כשהמחשב מקובע ברכב, הערכים אמורים להגיב להאצה, בלימה ופניות.",9,FontStyle.Regular,Muted,65);
            helper.SetBounds(18,85,630,65);
            cardTech.Controls.Add(techTitle);cardTech.Controls.Add(hardware);cardTech.Controls.Add(helper);
            body.Controls.Add(cardTech);

            // Optional fine controls in a dedicated compact section.
            var advanced=Card(170);
            var aTitle=Label("אפשרויות תצוגה מתקדמות",12,FontStyle.Bold,Fore,28);
            aTitle.SetBounds(18,10,600,28);advanced.Controls.Add(aTitle);
            size=MakeSlider(3,16,config.DotSize);
            number=MakeSlider(7,22,config.DotCount);
            opacity=MakeSlider(20,100,(int)(config.Opacity*100));
            sizeValue=Label("",9,FontStyle.Bold,Accent,24);
            numberValue=Label("",9,FontStyle.Bold,Accent,24);
            opacityValue=Label("",9,FontStyle.Bold,Accent,24);
            SetupAdvanced(advanced,"גודל נקודות",size,sizeValue,18,48);
            SetupAdvanced(advanced,"כמות נקודות",number,numberValue,237,48);
            SetupAdvanced(advanced,"אטימות",opacity,opacityValue,456,48);
            size.Scroll+=(s,e)=>{config.DotSize=size.Value;sizeValue.Text=size.Value.ToString();config.Save();};
            number.Scroll+=(s,e)=>{config.DotCount=number.Value;numberValue.Text=number.Value.ToString();config.Save();};
            opacity.Scroll+=(s,e)=>{config.Opacity=opacity.Value/100.0;opacityValue.Text=opacity.Value+"%";config.Save();};
            sizeValue.Text=size.Value.ToString();numberValue.Text=number.Value.ToString();opacityValue.Text=opacity.Value+"%";
            var small=Label("מצב ״מוצג תמיד״ מסייע לבדיקה גם כשאין תנועה. לעבודה אוטומטית בחרו במצב ״אוטומטי״.",9,FontStyle.Regular,Muted,31);
            small.SetBounds(18,128,650,30);advanced.Controls.Add(small);
            body.Controls.Add(advanced);

            refresh.Tick+=(s,e)=>RefreshLive();
            refresh.Start();
            ResizeCards();RefreshLive();
            FormClosed+=(s,e)=>{refresh.Stop();refresh.Dispose();};
        }
        static Label Label(string text,float size,FontStyle style,Color color,int height)
        {
            return new Label {
                Text=text,Font=new Font("Segoe UI",size,style),ForeColor=color,Height=height,
                AutoEllipsis=false,TextAlign=ContentAlignment.MiddleRight,
                RightToLeft=RightToLeft.Yes,AutoSize=false,BackColor=Color.Transparent
            };
        }
        static Panel Card(int height)
        {
            return new Panel {Height=height,Width=850,BackColor=PanelColor,Margin=new Padding(0,0,0,14)};
        }
        static ComboBox MakeCombo(string[] options,int selected)
        {
            var c=new ComboBox {
                DropDownStyle=ComboBoxStyle.DropDownList,
                Font=new Font("Segoe UI",10),
                BackColor=Color.FromArgb(42,52,65),ForeColor=Color.White,
                FlatStyle=FlatStyle.Flat,RightToLeft=RightToLeft.Yes
            };
            c.Items.AddRange(options);
            c.SelectedIndex=Math.Max(0,Math.Min(options.Length-1,selected));
            return c;
        }
        static TrackBar MakeSlider(int min,int max,int val)
        {
            return new TrackBar {Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,val)),TickStyle=TickStyle.None,BackColor=PanelColor};
        }
        static Button MakeButton(string caption)
        {
            var b=new Button {
                Text=caption,FlatStyle=FlatStyle.Flat,BackColor=Accent,
                ForeColor=Color.FromArgb(13,37,42),Font=new Font("Segoe UI",10,FontStyle.Bold),
                Cursor=Cursors.Hand
            }; b.FlatAppearance.BorderSize=0;return b;
        }
        static void SetupAdvanced(Panel p,string title,TrackBar slider,Label value,int x,int y)
        {
            var label=Label(title,10,FontStyle.Regular,Muted,26);label.SetBounds(x,y,190,25);
            slider.SetBounds(x,y+29,155,38);
            value.SetBounds(x+155,y+34,45,25);
            p.Controls.Add(label);p.Controls.Add(slider);p.Controls.Add(value);
        }
        void ResizeCards()
        {
            if(body==null)return;
            int w=Math.Max(660,body.ClientSize.Width-body.Padding.Left-body.Padding.Right-24);
            foreach(Control c in body.Controls) {
                c.Width=w;
                if(c==cardStatus) {
                    foreach(Control item in cardStatus.Controls)item.Width=w-36;
                } else if(c==cardPreview) {
                    foreach(Control item in cardPreview.Controls)item.Width=w-36;
                } else if(c==cardTech) {
                    foreach(Control item in cardTech.Controls)item.Width=w-36;
                }
            }
        }
        void RefreshLive()
        {
            if(IsDisposed)return;
            var d=sensors.Snapshot();
            if(!d.Found) {
                statTitle.Text="לא נמצא מד תאוצה זמין";
                statTitle.ForeColor=Color.FromArgb(247,178,127);
                statDetail.Text="התוכנה אינה מקבלת כרגע מידע מחיישן התנועה של Windows.";
            } else if(!d.Fresh) {
                statTitle.Text="זוהה חיישן — ממתין לנתונים חיים";
                statTitle.ForeColor=Color.FromArgb(247,178,127);
                statDetail.Text=d.Samples==0?"זוהה התקן, אך עדיין לא התקבלה אפילו קריאה אחת.":"הקריאות הפסיקו להתעדכן. קריאה אחרונה לפני "+d.Age.ToString("0.0")+" שניות.";
            } else {
                statTitle.Text="החיישן פועל ומספק נתונים";
                statTitle.ForeColor=Accent;
                statDetail.Text="קצב: "+d.Rate.ToString("0")+" קריאות לשנייה • מספר קריאות: "+d.Samples+" • מצב: "+(d.Moving?"תנועה מזוהה":"תנועה חלשה / יציב");
            }
            vector.Text="X: "+d.X.ToString("+0.00;-0.00;0.00")+"  |  Y: "+d.Y.ToString("+0.00;-0.00;0.00")+"  |  עוצמה: "+d.Strength.ToString("0.00")+" m/s²";
            hardware.Text="מד תאוצה: "+(d.Found?"זוהה":"לא זמין")+"    •    גירוסקופ: "+(d.Gyro?"זוהה":"לא זמין")+"    •    תקינות: "+(d.Fresh?"נתונים חיים":"ללא נתונים עדכניים");
            if(!string.IsNullOrEmpty(d.Error))helper.Text="מידע ממערכת החיישנים: "+d.Error;
            preview.Invalidate();
        }
    }
    internal sealed class Preview : Control
    {
        readonly SensorService sensors;readonly AppConfig config;
        public Preview(SensorService s,AppConfig c)
        {
            sensors=s;config=c;BackColor=Color.FromArgb(22,29,38);
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var data=sensors.Snapshot();
            float cx=Width/2f,cy=Height/2f;
            float dx=(float)(Math.Tanh(data.X*config.Gain/4)*30);
            float dy=(float)(Math.Tanh(data.Y*config.Gain/4)*30);
            using(var center=new Pen(Color.FromArgb(75,165,181,192),1))
            using(var dot=new SolidBrush(Color.FromArgb(117,207,190)))
            {
                g.DrawLine(center,cx,15,cx,Height-15);
                g.DrawLine(center,16,cy,Width-16,cy);
                for(int n=0;n<8;n++) {
                    float x=28+n*(Width-56)/7f;
                    g.FillEllipse(dot,x+dx-3,cy+dy-3,6,6);
                }
            }
        }
    }
}
