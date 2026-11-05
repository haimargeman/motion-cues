using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SteadyCuesV2
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using(var mutex=new Mutex(true,@"Local\SteadyCuesV2.Singleton",out created))
            {
                if(!created){MessageBox.Show("SteadyCues החדש כבר פועל.","SteadyCues 2");return;}
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException+=(s,e)=>WriteError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException+=(s,e)=>WriteError(e.ExceptionObject as Exception);
                using(var app=new AppHost())Application.Run(app);
            }
        }
        static void WriteError(Exception ex)
        {
            try {
                Directory.CreateDirectory(AppConfig.Folder);
                File.AppendAllText(Path.Combine(AppConfig.Folder,"errors.log"),DateTime.Now.ToString("u")+" "+ex+Environment.NewLine);
            }catch{}
            MessageBox.Show("אירעה תקלה. מידע נוסף נשמר בקובץ errors.log בהגדרות התוכנה.","SteadyCues 2");
        }
    }
    internal sealed class AppHost : ApplicationContext
    {
        internal readonly SensorService Sensors=new SensorService();
        internal readonly AppConfig Config=AppConfig.Load();
        readonly OverlayManager overlays;
        readonly NotifyIcon tray;
        Dashboard window;
        public AppHost()
        {
            Sensors.SetDirection(Config.Direction);
            Sensors.Start();
            overlays=new OverlayManager(Sensors,Config);
            overlays.Start();
            tray=new NotifyIcon { Icon=SystemIcons.Information,Text="SteadyCues 2",Visible=true };
            var menu=new ContextMenuStrip { RightToLeft=RightToLeft.Yes };
            menu.Items.Add("פתיחת לוח הבקרה",null,(s,e)=>ShowDashboard());
            menu.Items.Add("הצגה תמיד",null,(s,e)=>{Config.Mode=1;Config.Save();});
            menu.Items.Add("כיבוי הנקודות",null,(s,e)=>{Config.Mode=2;Config.Save();});
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("יציאה",null,(s,e)=>ExitThread());
            tray.ContextMenuStrip=menu;
            tray.MouseDoubleClick+=(s,e)=>ShowDashboard();
            ShowDashboard();
        }
        void ShowDashboard()
        {
            if(window==null||window.IsDisposed) {
                window=new Dashboard(Sensors,Config,overlays);
                window.FormClosing+=(s,e)=>{
                    if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;window.Hide();}
                };
            }
            window.Show();
            window.Activate();
        }
        protected override void ExitThreadCore()
        {
            Config.Save();
            tray.Visible=false;tray.Dispose();
            if(window!=null){window.Hide();window.Dispose();}
            overlays.Dispose();Sensors.Dispose();
            base.ExitThreadCore();
        }
    }
}
