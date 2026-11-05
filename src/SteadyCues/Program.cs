using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SteadyCues
{
    internal static class Program
    {
        public const string Website = "https://steadycues.vercel.app/";
        public const string Repository = "https://github.com/Aweswomedude1234/motion-cues";
        private const string MutexName = @"Local\SteadyCues.SingleInstance";
        public const string ShowEventName = @"Local\SteadyCues.Show";
        public const string QuitEventName = @"Local\SteadyCues.Quit";

        public static string Version
        {
            get
            {
                var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                return a != null ? a.InformationalVersion : "dev";
            }
        }

        /// <summary>--loopback: phone link only listens on 127.0.0.1 (for development and tests).</summary>
        public static bool LoopbackOnly;

        [STAThread]
        private static int Main(string[] args)
        {
            Func<string, bool> has = f => args.Any(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase));
            LoopbackOnly = has("--loopback");

            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);

            bool quiet = has("--quiet");
            if (has("--uninstall"))
            {
                if (!quiet)
                {
                    var answer = MessageBox.Show("להסיר את SteadyCues ואת ההגדרות מהמחשב?", "Uninstall SteadyCues",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (answer != DialogResult.OK) return 1;
                }
                StopRunningInstance();
                Installer.Uninstall();
                if (!quiet) MessageBox.Show("SteadyCues הוסרה מהמחשב.", "SteadyCues", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            if (has("--install"))
            {
                // Silent install for package managers (winget): copy into place, register, exit.
                StopRunningInstance();
                if (!Installer.Install()) return 1;
                if (!Installer.AutoStartEnabled) Installer.SetAutoStart(true);
                return 0;
            }

            // Package managers that keep their own copy (Scoop, winget portable) manage updates themselves.
            bool managed = Installer.CurrentExe.IndexOf(@"\scoop\apps\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           Installer.CurrentExe.IndexOf(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase) >= 0;
            bool portable = managed || has("--portable") || File.Exists(Path.Combine(Path.GetDirectoryName(Installer.CurrentExe), "portable.txt"));
            if (!portable && !Installer.IsInstalledCopy)
            {
                // Downloaded copy: replace any running/older install, then run from the install folder.
                StopRunningInstance();
                if (Installer.Install())
                {
                    Installer.Relaunch("--show");
                    return 0;
                }
                // Couldn't install (locked-down PC): carry on as a portable app.
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    if (!has("--background")) Signal(ShowEventName);
                    return 0;
                }
                bool showWindow = !has("--background");
                using (var app = new TrayApp(showWindow, has("--demo")))
                    Application.Run(app);
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static void Signal(string name)
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(name)) ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Asks a running SteadyCues to exit and waits until it has.</summary>
        private static void StopRunningInstance()
        {
            Signal(QuitEventName);
            try
            {
                using (var m = new Mutex(false, MutexName))
                {
                    bool got;
                    try { got = m.WaitOne(6000); }
                    catch (AbandonedMutexException) { got = true; }
                    if (got) m.ReleaseMutex();
                }
            }
            catch { }
        }

        private static int _crashing;
        private static void Crash(Exception ex)
        {
            if (Interlocked.Exchange(ref _crashing, 1) == 1) return;
            string log = Path.Combine(Settings.Folder, "error.log");
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(log, DateTime.Now.ToString("u") + "  v" + Version + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            MessageBox.Show("אירעה תקלה בתוכנה ויש לסגור אותה.\n\n" + (ex != null ? ex.Message : "שגיאה לא ידועה") +
                            "\n\nפרטי השגיאה נשמרו ב־" + log + ". צרפו את הפרטים לדיווח על התקלה.",
                "SteadyCues", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
