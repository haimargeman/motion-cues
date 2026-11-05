using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteadyCuesV2
{
    // Entire dashboard uses managed layouts and autosized wrapped labels.
    // No fixed coordinates: RTL, high DPI, and long error messages remain readable.
    internal sealed class Dashboard : Form
    {
        readonly SensorService sensors;
        readonly AppConfig config;
        readonly OverlayManager overlays;
        readonly Timer refresh = new Timer { Interval = 150 };
        readonly FlowLayoutPanel body;
        readonly Label state, status, vector, raw, technical, explanation, displayInfo;
        readonly Preview preview;
        readonly ComboBox mode, direction;
        readonly TrackBar gain, dots, count, opacity;
        readonly Label gainLabel, dotsLabel, countLabel, opacityLabel;
        readonly Button visualTest;
        readonly Panel statusCard, previewCard, settingsCard, hardwareCard, advancedCard;
        const int Spacing = 14;
        static readonly Color Background = Color.FromArgb(18, 23, 30);
        static readonly Color Surface = Color.FromArgb(31, 39, 49);
        static readonly Color TextColor = Color.FromArgb(240, 244, 246);
        static readonly Color Secondary = Color.FromArgb(184, 200, 211);
        static readonly Color Accent = Color.FromArgb(123, 217, 197);
        static readonly Color Warning = Color.FromArgb(251, 188, 124);

        public Dashboard(SensorService s, AppConfig c, OverlayManager o)
        {
            sensors=s; config=c; overlays=o;
            Text="SteadyCues 2 • תנועת נסיעה";
            StartPosition=FormStartPosition.CenterScreen;
            MinimumSize=new Size(620, 560);
            Size=new Size(900, 780);
            BackColor=Background;
            ForeColor=TextColor;
            Font=new Font("Segoe UI", 10f);
            RightToLeft=RightToLeft.Yes;
            RightToLeftLayout=true;
            AutoScaleMode=AutoScaleMode.Dpi;

            var heading = new TableLayoutPanel {
                Dock=DockStyle.Top, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=1, RowCount=2,
                BackColor=Background, Padding=new Padding(24,17,24,13)
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.Controls.Add(T("SteadyCues 2", 21, true, TextColor),0,0);
            heading.Controls.Add(T("מהדורה מקומית • חיישני המחשב בלבד • ללא חיבור לטלפון",10,false,Secondary),0,1);

            body=new FlowLayoutPanel {
                Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown,
                WrapContents=false, AutoScroll=true, BackColor=Background,
                Padding=new Padding(18,8,18,26)
            };
            Controls.Add(body);
            Controls.Add(heading);
            heading.BringToFront();

            var sc=Section("מצב התנועה");
            statusCard=sc.Item1;
            var statusGrid=sc.Item2;
            state=T("מאתר חיישנים...",16,true,TextColor);
            status=T("",10,false,Secondary);
            vector=T("",10,true,Accent);
            raw=T("",9,false,Secondary);
            AddLine(statusGrid,state);
            AddLine(statusGrid,status);
            AddLine(statusGrid,vector);
            AddLine(statusGrid,raw);
            body.Controls.Add(statusCard);

            var pc=Section("תצוגה מקדימה");
            previewCard=pc.Item1;
            var previewGrid=pc.Item2;
            preview=new Preview(sensors,config) { Dock=DockStyle.Top,Height=132,
                BackColor=Color.FromArgb(230,236,240),Margin=new Padding(0,5,0,3) };
            AddLine(previewGrid,preview);
            displayInfo=T("הדמיה מקדימה של נתוני החיישן. אם אין נתונים, הנקודות לא ינועו.",9,false,Secondary);
            AddLine(previewGrid,displayInfo);
            visualTest=ActionButton("בדיקת ציור – הזזת נקודות ל־4 שניות");
            visualTest.Click+=(sender,args)=>{
                overlays.BeginVisualTest();
                displayInfo.Text="בדיקת הציור פועלת למשך ארבע שניות. זו בדיקה חזותית בלבד, לא נתוני החיישן.";
            };
            AddLine(previewGrid,visualTest);
            body.Controls.Add(previewCard);

            var oc=Section("הגדרות תנועה");
            settingsCard=oc.Item1;
            var opt=oc.Item2;
            mode=Choice(new[]{"מוצג אוטומטית בעת תנועה","מוצג תמיד","כבוי"},config.Mode);
            direction=Choice(new[]{"כיוון רגיל","90° ימינה","180° הפוך","90° שמאלה"},config.Direction);
            AddLine(opt,T("אופן הצגת הנקודות",10,true,TextColor));
            AddLine(opt,mode);
            AddLine(opt,T("כיוון הצירים ביחס לרכב",10,true,TextColor));
            AddLine(opt,direction);
            gain=Track(40,500,(int)Math.Round(config.Gain*100));
            gainLabel=T("",10,true,Accent);
            AddLine(opt,T("רגישות לתאוצה – הגדלה גורמת לתגובה חזקה יותר",10,true,TextColor));
            AddLine(opt,gain);
            AddLine(opt,gainLabel);
            gainLabel.Text="רגישות נוכחית: "+config.Gain.ToString("0.00")+"×";
            var calibrate=ActionButton("איפוס אומדן הכבידה");
            calibrate.Click+=(sender,args)=>{
                sensors.Calibrate();
                MessageBox.Show(this,"האומדן אופס. השאירו את המחשב יציב למשך שנייה.","כיול");
            };
            AddLine(opt,calibrate);
            body.Controls.Add(settingsCard);
            mode.SelectedIndexChanged+=(sender,args)=>{config.Mode=mode.SelectedIndex;config.Save();};
            direction.SelectedIndexChanged+=(sender,args)=>{
                config.Direction=direction.SelectedIndex;sensors.SetDirection(config.Direction);config.Save();
            };
            gain.Scroll+=(sender,args)=>{
                config.Gain=gain.Value/100.0;
                gainLabel.Text="רגישות נוכחית: "+config.Gain.ToString("0.00")+"×";
                config.Save();
            };

            var hc=Section("אבחון חיישני Windows");
            hardwareCard=hc.Item1;
            var hg=hc.Item2;
            technical=T("",10,true,TextColor);
            explanation=T("יש להזיז את המחשב בעדינות ולבחון אם ערכי המדידה הגולמיים משתנים. שינוי במדידה הגולמית ללא שינוי בערכי X ו־Y מצביע על בעיית חישוב או כיוון.",9,false,Secondary);
            AddLine(hg,technical);
            AddLine(hg,explanation);
            body.Controls.Add(hardwareCard);

            var ac=Section("עיצוב הנקודות");
            advancedCard=ac.Item1;
            var ag=ac.Item2;
            dots=Track(3,16,config.DotSize);
            count=Track(7,22,config.DotCount);
            opacity=Track(20,100,(int)(config.Opacity*100));
            dotsLabel=T("",10,false,Secondary);countLabel=T("",10,false,Secondary);opacityLabel=T("",10,false,Secondary);
            AddLine(ag,T("גודל העיגולים השחורים",10,true,TextColor));AddLine(ag,dots);AddLine(ag,dotsLabel);
            AddLine(ag,T("מספר הנקודות בכל צד",10,true,TextColor));AddLine(ag,count);AddLine(ag,countLabel);
            AddLine(ag,T("אטימות העיגולים",10,true,TextColor));AddLine(ag,opacity);AddLine(ag,opacityLabel);
            dotsLabel.Text="גודל: "+config.DotSize;
            countLabel.Text="נקודות: "+config.DotCount;
            opacityLabel.Text="אטימות: "+(int)(config.Opacity*100)+"%";
            dots.Scroll+=(sender,args)=>{config.DotSize=dots.Value;dotsLabel.Text="גודל: "+dots.Value;config.Save();};
            count.Scroll+=(sender,args)=>{config.DotCount=count.Value;countLabel.Text="נקודות: "+count.Value;config.Save();};
            opacity.Scroll+=(sender,args)=>{config.Opacity=opacity.Value/100.0;opacityLabel.Text="אטימות: "+opacity.Value+"%";config.Save();};
            body.Controls.Add(advancedCard);

            body.Resize+=(sender,args)=>ResizeCards();
            Shown+=(sender,args)=>ResizeCards();
            refresh.Tick+=(sender,args)=>UpdateSensorStatus();
            refresh.Start();
            UpdateSensorStatus();
            FormClosed+=(sender,args)=>{refresh.Stop();refresh.Dispose();};
        }

        static Label T(string value,float size,bool heavy,Color color)
        {
            return new Label {
                Text=value,Font=new Font("Segoe UI",size,heavy?FontStyle.Bold:FontStyle.Regular),
                ForeColor=color,RightToLeft=RightToLeft.Yes,
                TextAlign=ContentAlignment.MiddleRight,
                AutoSize=true,MinimumSize=new Size(0,22),Margin=new Padding(0,3,0,6),
                UseMnemonic=false
            };
        }
        static Tuple<Panel,TableLayoutPanel> Section(string title)
        {
            var panel=new Panel {
                Width=810,Height=140,BackColor=Surface,
                Margin=new Padding(0,0,0,Spacing),Padding=new Padding(16,14,16,15)
            };
            var grid=new TableLayoutPanel {
                Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=1,RowCount=0,
                RightToLeft=RightToLeft.Yes,
                BackColor=Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            AddLine(grid,T(title,13,true,TextColor));
            panel.Controls.Add(grid);
            grid.SizeChanged+=(sender,args)=>{
                var h=grid.PreferredSize.Height+panel.Padding.Top+panel.Padding.Bottom+2;
                if(panel.Height!=h)panel.Height=h;
            };
            return Tuple.Create(panel,grid);
        }
        static void AddLine(TableLayoutPanel grid,Control item)
        {
            item.Dock=DockStyle.Top;
            if(item is TrackBar)item.Margin=new Padding(1,2,1,1);
            else if(item is ComboBox)item.Margin=new Padding(0,0,0,12);
            else if(item is Button)item.Margin=new Padding(0,8,0,8);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(item,0,grid.RowCount);
            grid.RowCount++;
        }
        static ComboBox Choice(string[] values,int selected)
        {
            var cb=new ComboBox {
                Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,
                FlatStyle=FlatStyle.Flat,RightToLeft=RightToLeft.Yes,
                BackColor=Color.FromArgb(46,57,69),ForeColor=TextColor,
                Font=new Font("Segoe UI",10f),Height=35,IntegralHeight=false
            };
            cb.Items.AddRange(values);
            cb.SelectedIndex=Math.Max(0,Math.Min(selected,values.Length-1));
            return cb;
        }
        static TrackBar Track(int min,int max,int value)
        {
            return new TrackBar {
                Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),
                TickStyle=TickStyle.None,Height=40,BackColor=Surface
            };
        }
        static Button ActionButton(string value)
        {
            var btn=new Button {
                Text=value,Height=43,AutoSize=false,Dock=DockStyle.Top,
                BackColor=Accent,ForeColor=Color.FromArgb(20,34,41),
                Font=new Font("Segoe UI",10,FontStyle.Bold),
                FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,
                RightToLeft=RightToLeft.Yes
            };
            btn.FlatAppearance.BorderSize=0;
            return btn;
        }
        void ResizeCards()
        {
            if(body==null || body.IsDisposed)return;
            int width=Math.Max(360,body.ClientSize.Width-body.Padding.Horizontal-24);
            body.SuspendLayout();
            foreach(Control item in body.Controls)
            {
                item.Width=width;
                foreach(Control c in item.Controls)
                {
                    if(c is TableLayoutPanel)
                    {
                        c.Width=width-item.Padding.Horizontal;
                        foreach(Control sub in c.Controls)
                        {
                            var label=sub as Label;
                            if(label!=null)
                                label.MaximumSize=new Size(Math.Max(240,c.Width-8),0);
                        }
                    }
                }
            }
            body.ResumeLayout(true);
        }
        void UpdateSensorStatus()
        {
            if(IsDisposed)return;
            var d=sensors.Snapshot();
            if(!d.Found) {
                state.Text="Windows אינו מזהה מד תאוצה";
                state.ForeColor=Warning;
                status.Text="אין מקור תנועה מקומי זמין. בדקו את מנהל ההתקנים ואת מנהלי החיישנים של המחשב.";
            }
            else if(!d.Fresh) {
                state.Text="מד התאוצה זוהה, אך אין נתונים חדשים";
                state.ForeColor=Warning;
                status.Text=d.Samples==0?"התקן החיישן מחובר אך טרם התקבלו מדידות.":
                    "קריאה אחרונה לפני "+d.Age.ToString("0.0")+" שניות.";
            }
            else {
                state.Text="חיישני התנועה פועלים";
                state.ForeColor=Accent;
                status.Text="קצב: "+d.Rate.ToString("0")+" קריאות בשנייה • מספר מדידות: "+d.Samples+
                    " • "+(d.Moving?"תנועה מזוהה":"תנועה חלשה או יציב");
            }
            vector.Text="תנועה מחושבת: X "+d.X.ToString("+0.00;-0.00;0.00")+
                " | Y "+d.Y.ToString("+0.00;-0.00;0.00")+
                " | עוצמה "+d.Strength.ToString("0.00")+" m/s²";
            raw.Text="חיישן גולמי: X "+d.RawX.ToString("0.00")+
                " | Y "+d.RawY.ToString("0.00")+" | Z "+d.RawZ.ToString("0.00")+" m/s²";
            technical.Text="מד תאוצה: "+(d.Found?"מחובר":"לא זמין")+
                " • גירוסקופ: "+(d.Gyro?"מחובר":"לא זמין")+
                " • עדכון: "+(d.Fresh?"תקין":"לא עדכני");
            if(!String.IsNullOrWhiteSpace(d.Error))
                explanation.Text="שגיאת חיישן: "+d.Error;
            preview.Invalidate();
        }
    }

    internal sealed class Preview : Control
    {
        readonly SensorService sensors;
        readonly AppConfig config;
        public Preview(SensorService s,AppConfig c)
        {
            sensors=s;config=c;
            BackColor=Color.FromArgb(230,236,240);
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|
                ControlStyles.AllPaintingInWmPaint,true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var d=sensors.Snapshot();
            float midX=Width/2f,midY=Height/2f;
            float dx=(float)(Math.Tanh(d.X*config.Gain/4)*35);
            float dy=(float)(Math.Tanh(d.Y*config.Gain/4)*35);
            using(var line=new Pen(Color.FromArgb(90,173,192,201)))
            using(var dot=new SolidBrush(Color.FromArgb(8,8,10)))
            {
                g.DrawLine(line,midX,12,midX,Height-12);
                g.DrawLine(line,15,midY,Width-15,midY);
                for(int i=0;i<8;i++) {
                    float x=28+i*Math.Max(1,Width-56)/7f;
                    g.FillEllipse(dot,x+dx-5,midY+dy-5,10,10);
                }
            }
        }
    }
}
