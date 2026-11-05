using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SteadyCues.UI;

namespace SteadyCues
{
    /// <summary>Owns the app's lifetime: motion sources, the overlay, the tray icon and the window.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        public readonly Settings Settings;
        public readonly MotionHub Hub;
        public readonly PhoneBridge Bridge;
        /// <summary>True during the very first run, to show welcome hints.</summary>
        public bool WelcomeSession { get; private set; }
        private readonly BuiltInSensorSource _builtIn;
        private readonly LocationSource _location;
        private readonly DemoSource _demo;
        private readonly Overlay _overlay;
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private readonly Control _ui;
        private readonly EventWaitHandle _showEvent, _quitEvent;
        private readonly RegisteredWaitHandle _showWait, _quitWait;
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private ToolStripMenuItem _statusItem, _auto, _always, _off, _demoItem;
        private MainWindow _window;
        private CueMode _lastOnMode = CueMode.Automatic;
        private bool _exiting;

        public TrayApp(bool showWindow, bool startDemo)
        {
            Settings = Settings.Load();
            Hub = new MotionHub(Settings);
            _ui = new Control();
            _ui.CreateControl();

            _builtIn = new BuiltInSensorSource(Hub);
            _builtIn.Start();
            _location = new LocationSource(Hub);
            _demo = new DemoSource(Hub);
            Bridge = new PhoneBridge(Settings, Hub);

            // First launch: start with Windows by default (visible and reversible in Settings), and
            // remember right away that setup happened so a later launch never redoes it.
            bool firstRun = Settings.FirstRun;
            WelcomeSession = firstRun;
            if (firstRun)
            {
                if (Installer.IsInstalledCopy) Installer.SetAutoStart(true);
                Settings.FirstRun = false;
                Settings.Save();
            }

            ApplySources();
            Settings.Changed += delegate { ApplySources(); UpdateTray(); };

            _overlay = new Overlay(Settings, Hub);
            _overlay.Start();

            _tray = new NotifyIcon { Icon = AppIcon(SystemInformation.SmallIconSize.Width), Text = "SteadyCues", Visible = true };
            _tray.ContextMenuStrip = BuildMenu();
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

            _hotkey = new HotkeyWindow(ToggleOnOff);

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, delegate { _ui.BeginInvoke((Action)ShowWindow); }, null, -1, false);
            _quitWait = ThreadPool.RegisterWaitForSingleObject(_quitEvent, delegate { _ui.BeginInvoke((Action)Quit); }, null, -1, false);

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _statusTimer.Tick += delegate { UpdateTray(); };
            _statusTimer.Start();
            UpdateTray();

            if (showWindow || firstRun) ShowWindow();
            // Simulation is intentionally unavailable in this local-sensors-only edition.
        }

        // ------------------------------------------------------------------ sources

        private void ApplySources()
        {
            // Never listen on a network port or activate external motion sources.
            if (Bridge.Running) Bridge.Stop();
            _location.Stop();
            Settings.Source = SourceKind.BuiltInSensor;
            Settings.PhoneBridge = false;
            _builtIn.Adjust = Settings.Forward;
        }

        public void ToggleDemo()
        {
            return; // Use built-in physical sensors exclusively.
            /* if (_demo.Running) _demo.Stop();
            else
            {
                if (Settings.Mode == CueMode.Off) { Settings.Mode = _lastOnMode; Settings.NotifyChanged(); }
                _demo.Start();
            }
            UpdateTray(); */
        }

        private void ToggleOnOff()
        {
            if (Settings.Mode != CueMode.Off)
            {
                _lastOnMode = Settings.Mode;
                Settings.Mode = CueMode.Off;
            }
            else Settings.Mode = _lastOnMode;
            Settings.NotifyChanged();
            Balloon(Settings.Mode == CueMode.Off ? "הנקודות כובו" : "הנקודות הופעלו",
                Settings.Mode == CueMode.Off ? "להפעלה מחדש לחצו Ctrl + Alt + M." : "לכיבוי לחצו Ctrl + Alt + M.");
        }

        private void SetMode(CueMode m)
        {
            if (m != CueMode.Off) _lastOnMode = m;
            Settings.Mode = m;
            Settings.NotifyChanged();
        }

        // ------------------------------------------------------------------ tray

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            _statusItem = new ToolStripMenuItem("SteadyCues") { Enabled = false };
            _auto = new ToolStripMenuItem("אוטומטי", null, delegate { SetMode(CueMode.Automatic); });
            _always = new ToolStripMenuItem("מוצג תמיד", null, delegate { SetMode(CueMode.AlwaysOn); });
            _off = new ToolStripMenuItem("כבוי", null, delegate { SetMode(CueMode.Off); }) { ShortcutKeyDisplayString = "Ctrl+Alt+M" };
            _demoItem = new ToolStripMenuItem("הדמיית נסיעה") { Enabled = false };
            menu.Items.AddRange(new ToolStripItem[]
            {
                _statusItem, new ToolStripSeparator(), _auto, _always, _off, new ToolStripSeparator(),
                
                new ToolStripMenuItem("פתח את SteadyCues", null, delegate { ShowWindow(); }) { Font = new Font(menu.Font, FontStyle.Bold) },
                new ToolStripSeparator(),
                new ToolStripMenuItem("יציאה", null, delegate { Quit(); }),
            });
            return menu;
        }

        private void UpdateTray()
        {
            if (_tray == null) return;
            var snap = Hub.Snapshot();
            string status;
            if (Settings.Mode == CueMode.Off) status = "כבוי";
            else if (snap.Source == ActiveSource.Demo) status = "הדמיית נסיעה";
            else if (snap.Source == ActiveSource.None) status = "לא זוהה מד תאוצה במחשב";
            else if (snap.Source == ActiveSource.Phone && !Bridge.PhoneConnected) status = "ממתין לחיישן";
            else status = snap.Moving ? "בתנועה · הנקודות מוצגות" : "מוכן";
            _statusItem.Text = "SteadyCues — " + status;
            string tip = "SteadyCues: " + status;
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            _auto.Checked = Settings.Mode == CueMode.Automatic;
            _always.Checked = Settings.Mode == CueMode.AlwaysOn;
            _off.Checked = Settings.Mode == CueMode.Off;
            _demoItem.Checked = _demo.Running;
        }

        public void Balloon(string title, string text)
        {
            try { _tray.ShowBalloonTip(3000, title, text, ToolTipIcon.None); } catch { }
        }

        public void ShowWindow()
        {
            if (_exiting) return;
            if (_window == null || _window.IsDisposed) _window = new MainWindow(this);
            if (!_window.Visible) _window.Show();
            if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
            _window.Activate();
            _window.BringToFront();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Rebuild the window when the user flips Windows between light and dark.
            bool wasDark = Theme.Dark;
            Theme.Refresh();
            if (wasDark == Theme.Dark || _window == null) return;
            bool visible = _window.Visible;
            _window.Dispose();
            _window = null;
            if (visible) ShowWindow();
        }

        public void Uninstall(IWin32Window owner)
        {
            if (MessageBox.Show(owner, "להסיר את SteadyCues ואת כל הגדרותיה מהמחשב?", "Uninstall SteadyCues",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Installer.Uninstall();
            Quit();
        }

        public void Quit()
        {
            if (_exiting) return;
            _exiting = true;
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _exiting = true;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _statusTimer.Stop();
            if (_window != null) _window.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _hotkey.Dispose();
            _overlay.Dispose();
            _demo.Dispose();
            _location.Dispose();
            _builtIn.Dispose();
            Bridge.Dispose();
            _showWait.Unregister(null);
            _quitWait.Unregister(null);
            _showEvent.Dispose();
            _quitEvent.Dispose();
            base.ExitThreadCore();
        }

        public static Icon AppIcon(int size)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SteadyCues.icon.ico"))
                return s != null ? new Icon(s, size, size) : SystemIcons.Application;
        }

        /// <summary>Receives the global Ctrl+Alt+M hotkey.</summary>
        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            private readonly Action _onHotkey;
            private readonly bool _registered;

            public HotkeyWindow(Action onHotkey)
            {
                _onHotkey = onHotkey;
                CreateHandle(new CreateParams { Caption = "SteadyCues hotkey" });
                _registered = Native.RegisterHotKey(Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.M);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_HOTKEY) _onHotkey();
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (_registered) Native.UnregisterHotKey(Handle, 1);
                DestroyHandle();
            }
        }
    }
}
