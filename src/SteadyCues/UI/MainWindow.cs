using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SteadyCues.UI
{
    /// <summary>The one window of the app: status and pairing, appearance, and settings.</summary>
    internal sealed class MainWindow : Form
    {
        private enum SensorView { None, BuiltIn, BuiltInMissing, PhonePair, PhoneConnected, Location }

        private readonly TrayApp _app;
        private readonly Settings _s;
        private readonly MotionHub _hub;
        private readonly PhoneBridge _bridge;
        private readonly Timer _refresh = new Timer { Interval = 400 };
        private float _dpi = 1;

        private Label _title, _subtitle;
        private PictureBox _logo;
        private Segmented _tabs;
        private Panel[] _pages, _content;

        // Home
        private Card _statusCard, _sensorCard;
        private CuePreview _homePreview;
        private Label _statusTitle, _statusDetail, _modeLabel, _modeHelp, _firstRunNote;
        private Segmented _mode;
        private FlatButton _demoBtn, _doneBtn;
        private Label _sensorTitle, _sensorText, _step1, _step2, _step3, _certNote, _bridgeError, _helpText;
        private QrView _qr;
        private TextBox _link;
        private ComboBox _network;
        private LinkLabel _sensorLink, _helpLink;
        private FlatButton _firewallBtn;
        private bool _showPairing, _showHelp, _toldAboutTray;
        private SensorView _view = SensorView.None;
        private List<PhoneBridge.LocalAddress> _addresses = new List<PhoneBridge.LocalAddress>();

        // Appearance
        private CuePreview _lookPreview;
        private FlatButton _lookDemoBtn;
        private Slider _intensity, _size, _rows, _opacity;
        private Label _intensityVal, _sizeVal, _rowsVal, _opacityVal;
        private Segmented _style, _edges, _displays;
        private Label _styleHelp;
        private LinkLabel _reset;

        // Settings
        private Toggle _autostart, _hideCapture;
        private Segmented _source, _forward;
        private Label _sourceHelp, _forwardHelp, _shortcut, _about;
        private FlatButton _firewallBtn2, _newCodeBtn, _uninstallBtn;
        private LinkLabel _links;

        public MainWindow(TrayApp app)
        {
            _app = app;
            _s = app.Settings;
            _hub = app.Hub;
            _bridge = app.Bridge;

            Text = "SteadyCues — חיישני מחשב בלבד";
            RightToLeft = RightToLeft.Yes;
            Icon = TrayApp.AppIcon(32);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(10f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            Build();
            _refresh.Tick += delegate { RefreshState(); };
            _s.Changed += OnSettingsChanged;
        }

        private int D(float v) { return (int)Math.Round(v * _dpi); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle, Theme.Dark);
            Native.UseWin11Chrome(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _refresh.Enabled = Visible;
            if (Visible) RefreshState();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _refresh.Dispose(); _s.Changed -= OnSettingsChanged; }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { HideToTray(); return true; }
            if (keyData == (Keys.Control | Keys.Tab)) { _tabs.SelectedIndex = (_tabs.SelectedIndex + 1) % 3; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ShowPage(int index) { _tabs.SelectedIndex = index; }

        private void HideToTray()
        {
            Hide();
            if (_app.WelcomeSession && !_toldAboutTray)
            {
                _toldAboutTray = true;
                _app.Balloon("SteadyCues פועלת", "התוכנה פועלת באזור ההתראות ליד השעון. לחצו על הסמל לפתיחתה.");
            }
        }

        // ================================================================== construction

        private void Build()
        {
            SuspendLayout();
            _dpi = DeviceDpi / 96f;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(D(820), work.Width - D(36)), Math.Min(D(850), work.Height - D(40)));
            MinimumSize = new Size(D(630), D(500));

            _logo = new PictureBox { Image = TrayApp.AppIcon(64).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Bg };
            _title = MakeLabel("SteadyCues", 15f, FontStyle.Bold, Theme.Text);
            _subtitle = MakeLabel("", 9f, FontStyle.Regular, Theme.Muted);
            _tabs = new Segmented("ראשי", "תצוגה", "הגדרות");
            _tabs.SelectedIndexChanged += delegate { SwitchPage(); };
            Controls.AddRange(new Control[] { _logo, _title, _subtitle, _tabs });

            _pages = new Panel[3];
            _content = new Panel[3];
            for (int i = 0; i < 3; i++)
            {
                _pages[i] = new Panel { AutoScroll = true, BackColor = Theme.Bg, Visible = i == 0 };
                _content[i] = new Panel { BackColor = Theme.Bg };
                _pages[i].Controls.Add(_content[i]);
                Controls.Add(_pages[i]);
            }
            BuildHome(_content[0]);
            BuildAppearance(_content[1]);
            BuildSettings(_content[2]);
            LoadValues();
            ResumeLayout();
            LayoutAll();
        }

        private Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label { Text = text, Font = Theme.Font(size, style), ForeColor = color, BackColor = Color.Transparent, AutoSize = false, UseMnemonic = false, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight };
        }

        private LinkLabel MakeLink(string text)
        {
            var l = new LinkLabel
            {
                Text = text, Font = Theme.Font(9.5f), AutoSize = false, BackColor = Color.Transparent,
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.Focus, VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, UseMnemonic = false, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight,
            };
            return l;
        }

        private void BuildHome(Panel p)
        {
            _statusCard = new Card();
            _homePreview = new CuePreview(_s, _hub);
            _statusTitle = MakeLabel("", 11f, FontStyle.Bold, Theme.Text);
            _statusDetail = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            _statusCard.Controls.AddRange(new Control[] { _homePreview, _statusTitle, _statusDetail });

            _modeLabel = MakeLabel("הצגת נקודות", 10.5f, FontStyle.Bold, Theme.Text);
            _mode = new Segmented("אוטומטי", "מוצג תמיד", "כבוי");
            _mode.SelectedIndexChanged += delegate { _s.Mode = (CueMode)_mode.SelectedIndex; _s.NotifyChanged(); UpdateModeHelp(); LayoutHome(); };
            _modeHelp = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);

            _sensorCard = new Card();
            _sensorTitle = MakeLabel("", 11f, FontStyle.Bold, Theme.Text);
            _sensorText = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            _qr = new QrView();
            _step1 = MakeLabel("1  Put this PC and your phone on the same Wi-Fi — or connect this PC to your phone's hotspot.", 9.5f, FontStyle.Regular, Theme.Text);
            _step2 = MakeLabel("2  Scan the code with your phone's camera.", 9.5f, FontStyle.Regular, Theme.Text);
            _step3 = MakeLabel("3  If a warning appears, tap Show details › visit this website (iPhone) or Advanced › Proceed (Android). Then tap Start.", 9.5f, FontStyle.Regular, Theme.Text);
            _certNote = MakeLabel("The warning shows once because your PC made its own security certificate. Motion data stays on your network.", 8.5f, FontStyle.Regular, Theme.Muted);
            _link = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Muted, Font = Theme.Font(9f), TabStop = true };
            _network = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Font(9f), BackColor = Theme.Surface, ForeColor = Theme.Text };
            _network.SelectedIndexChanged += delegate { UpdateLink(); };
            _bridgeError = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Error);
            _sensorLink = MakeLink("");
            _sensorLink.LinkClicked += delegate { OnSensorLink(); };
            _helpLink = MakeLink("Phone can't connect?");
            _helpLink.LinkClicked += delegate { _showHelp = !_showHelp; LayoutHome(); };
            _helpText = MakeLabel("Check that both devices are on the same network and SteadyCues is running. Windows Firewall may be blocking the phone, especially on hotspots, which Windows treats as public networks. Allow it once:", 9f, FontStyle.Regular, Theme.Muted);
            _firewallBtn = new FlatButton { Text = "Allow through firewall" };
            _firewallBtn.Click += delegate { AllowFirewall(); };
            _sensorCard.Controls.AddRange(new Control[] { _sensorTitle, _sensorText, _qr, _step1, _step2, _step3, _certNote, _link, _network, _bridgeError, _sensorLink, _helpLink, _helpText, _firewallBtn });

            _firstRunNote = MakeLabel("התוכנה תופעל עם Windows. ניתן לשנות זאת בהגדרות.", 9f, FontStyle.Regular, Theme.Muted);
            _demoBtn = new FlatButton { Text = "בדיקת תצוגה" };
            _demoBtn.Click += delegate { _app.ToggleDemo(); RefreshState(); };
            _doneBtn = new FlatButton { Text = "סיום", Primary = true };
            _doneBtn.Click += delegate { HideToTray(); };

            p.Controls.AddRange(new Control[] { _statusCard, _modeLabel, _mode, _modeHelp, _sensorCard, _firstRunNote, _demoBtn, _doneBtn });
        }

        private void BuildAppearance(Panel p)
        {
            _lookPreview = new CuePreview(_s, _hub);
            _lookDemoBtn = new FlatButton { Text = "תצוגה מקדימה" };
            _lookDemoBtn.Click += delegate { _app.ToggleDemo(); RefreshState(); };

            _intensity = MakeSlider(0.25, 2.0, 0.05, v => { _s.Intensity = v; });
            _size = MakeSlider(0.5, 2.0, 0.05, v => { _s.DotSize = v; });
            _rows = MakeSlider(6, 18, 1, v => { _s.Rows = (int)Math.Round(v); });
            _opacity = MakeSlider(0.2, 1.0, 0.05, v => { _s.Opacity = v; });
            _intensityVal = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            _sizeVal = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            _rowsVal = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            _opacityVal = MakeLabel("", 9.5f, FontStyle.Regular, Theme.Muted);
            foreach (var l in new[] { _intensityVal, _sizeVal, _rowsVal, _opacityVal }) l.TextAlign = ContentAlignment.MiddleRight;

            _style = new Segmented("מותאם", "כהה", "בהיר", "צבע הדגשה");
            _style.SelectedIndexChanged += delegate { _s.Style = (DotStyle)_style.SelectedIndex; _s.NotifyChanged(); };
            _styleHelp = MakeLabel("נקודות מותאמות כוללות קו מתאר בהיר כדי להיראות גם על רקע כהה וגם על רקע בהיר.", 9f, FontStyle.Regular, Theme.Muted);
            _edges = new Segmented("צד ימין ושמאל", "כל ארבעת הצדדים");
            _edges.SelectedIndexChanged += delegate { _s.Edges = (EdgeLayout)_edges.SelectedIndex; _s.NotifyChanged(); };
            _displays = new Segmented("מסך ראשי", "כל המסכים");
            _displays.SelectedIndexChanged += delegate { _s.Displays = (DisplayTarget)_displays.SelectedIndex; _s.NotifyChanged(); };
            _reset = MakeLink("איפוס לברירת המחדל");
            _reset.LinkClicked += delegate
            {
                var d = new Settings();
                _s.Intensity = d.Intensity; _s.DotSize = d.DotSize; _s.Rows = d.Rows; _s.Opacity = d.Opacity;
                _s.Style = d.Style; _s.Edges = d.Edges; _s.Displays = d.Displays;
                _s.NotifyChanged();
                LoadValues();
            };

            p.Controls.AddRange(new Control[] { _lookPreview, _lookDemoBtn, _intensity, _size, _rows, _opacity, _intensityVal, _sizeVal, _rowsVal, _opacityVal,
                _style, _styleHelp, _edges, _displays, _reset });
            foreach (var t in new[] { "Movement strength", "Dot size", "Dots per column", "Opacity", "Dot color", "Where dots appear", "Displays" })
                p.Controls.Add(new Label { Name = "h:" + t, Text = HebrewHeading(t), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight, Font = Theme.Font(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
        }

        private static string HebrewHeading(string text)
        {
            switch (text)
            {
                case "Movement strength": return "עוצמת התנועה";
                case "Dot size": return "גודל הנקודות";
                case "Dots per column": return "נקודות בטור";
                case "Opacity": return "אטימות";
                case "Dot color": return "צבע הנקודות";
                case "Where dots appear": return "מיקום הנקודות";
                case "Displays": return "מסכים";
                case "Motion source": return "מקור התנועה";
                case "If the dots move the wrong way": return "תיקון כיוון התנועה";
                case "Keyboard shortcut": return "קיצור מקשים";
                case "Phone link": return "חיבור לטלפון";
                case "About": return "אודות";
                default: return text;
            }
        }

        private static void AddLink(LinkLabel l, string part, string url)
        {
            l.Links.Add(l.Text.IndexOf(part, StringComparison.Ordinal), part.Length, url);
        }

        private Slider MakeSlider(double min, double max, double step, Action<double> apply)
        {
            var sl = new Slider { Minimum = min, Maximum = max, Step = step };
            sl.ValueChanged += delegate { apply(sl.Value); _s.NotifyChanged(); UpdateValueLabels(); };
            return sl;
        }

        private void BuildSettings(Panel p)
        {
            _autostart = new Toggle { Text = "הפעלה עם Windows", Description = "התוכנה תופעל אוטומטית באזור ההתראות בעת הכניסה למחשב." };
            _autostart.CheckedChanged += delegate { Installer.SetAutoStart(_autostart.Checked); };
            _hideCapture = new Toggle { Text = "הסתרת נקודות מצילומי מסך", Description = "הנקודות לא יופיעו בצילומי מסך, בהקלטות או בשיתוף מסך." };
            _hideCapture.CheckedChanged += delegate { _s.HideFromCapture = _hideCapture.Checked; _s.NotifyChanged(); };

            _source = new Segmented("חיישנים מקומיים בלבד");
            _source.Enabled = false; // Source selection is locked to local sensors.
            _sourceHelp = MakeLabel("", 9f, FontStyle.Regular, Theme.Muted);
            _forward = new Segmented("רגיל", "סיבוב 90°", "הפוך", "סיבוב 270°");
            _forward.SelectedIndexChanged += delegate { _s.Forward = (ForwardAdjust)_forward.SelectedIndex; _s.NotifyChanged(); };
            _forwardHelp = MakeLabel("אם הנקודות נעות לצדדים בעת בלימה, נסו סיבוב של 90°. אם הן נעות בכיוון ההפוך, בחרו הפוך.", 9f, FontStyle.Regular, Theme.Muted);
            _shortcut = MakeLabel("לחצו Ctrl + Alt + M מכל חלון כדי להציג או להסתיר את הנקודות.", 9.5f, FontStyle.Regular, Theme.Text);

            _firewallBtn2 = new FlatButton { Text = "Allow in firewall" };
            _firewallBtn2.Click += delegate { AllowFirewall(); };
            _newCodeBtn = new FlatButton { Text = "New pairing code" };
            _newCodeBtn.Click += delegate
            {
                if (MessageBox.Show(this, "Create a new phone link? Phones using the old link will have to scan the new code.", "SteadyCues",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                _s.PhoneToken = Settings.NewToken();
                _s.NotifyChanged();
                UpdateLink();
            };
            _uninstallBtn = new FlatButton { Text = "הסרה…" };
            _uninstallBtn.Click += delegate { _app.Uninstall(this); };

            _about = MakeLabel("SteadyCues " + Program.Version + " · free and open source (MIT license)", 9f, FontStyle.Regular, Theme.Muted);
            _links = MakeLink("אתר התוכנה   ·   GitHub   ·   דיווח על תקלה");
            _links.Links.Clear();
            AddLink(_links, "אתר התוכנה", Program.Website);
            AddLink(_links, "GitHub", Program.Repository);
            AddLink(_links, "דיווח על תקלה", Program.Repository + "/issues/new/choose");
            _links.LinkClicked += (s, e) => Open((string)e.Link.LinkData);

            p.Controls.AddRange(new Control[] { _autostart, _hideCapture, _source, _sourceHelp, _forward, _forwardHelp, _shortcut,
                _firewallBtn2, _newCodeBtn, _uninstallBtn, _about, _links });
            foreach (var t in new[] { "Motion source", "If the dots move the wrong way", "Keyboard shortcut", "Phone link", "About" })
                p.Controls.Add(new Label { Name = "h:" + t, Text = t, Font = Theme.Font(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
        }

        private void LoadValues()
        {
            _mode.SetSilently((int)_s.Mode);
            _intensity.SetSilently(_s.Intensity);
            _size.SetSilently(_s.DotSize);
            _rows.SetSilently(_s.Rows);
            _opacity.SetSilently(_s.Opacity);
            _style.SetSilently((int)_s.Style);
            _edges.SetSilently((int)_s.Edges);
            _displays.SetSilently((int)_s.Displays);
            _autostart.SetSilently(Installer.AutoStartEnabled);
            _hideCapture.SetSilently(_s.HideFromCapture);
            _source.SetSilently(0);
            _forward.SetSilently((int)_s.Forward);
            UpdateValueLabels();
            UpdateModeHelp();
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated) return;
            if ((int)_s.Mode != _mode.SelectedIndex) BeginInvoke((Action)(() => { _mode.SetSilently((int)_s.Mode); UpdateModeHelp(); LayoutHome(); }));
        }

        private void UpdateValueLabels()
        {
            _intensityVal.Text = (_s.Intensity * 100).ToString("0") + "%";
            _sizeVal.Text = (_s.DotSize * 100).ToString("0") + "%";
            _rowsVal.Text = _s.Rows.ToString();
            _opacityVal.Text = (_s.Opacity * 100).ToString("0") + "%";
        }

        private void UpdateModeHelp()
        {
            switch (_s.Mode)
            {
                case CueMode.Automatic: _modeHelp.Text = "הנקודות מופיעות בהדרגה בזמן תנועה ונעלמות לאחר העצירה."; break;
                case CueMode.AlwaysOn: _modeHelp.Text = "הנקודות מוצגות תמיד ומגיבות להאצה, בלימה ופניות."; break;
                default: _modeHelp.Text = "הנקודות מוסתרות. להצגתן לחצו Ctrl + Alt + M."; break;
            }
        }

        // ================================================================== layout

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_pages != null) LayoutAll();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _dpi = e.DeviceDpiNew / 96f;
            LayoutAll();
        }

        private void LayoutAll()
        {
            int pad = D(20);
            _logo.SetBounds(pad, D(18), D(36), D(36));
            _title.SetBounds(pad + D(46), D(12), ClientSize.Width - pad * 2 - D(46), D(30));
            _subtitle.SetBounds(pad + D(47), D(40), ClientSize.Width - pad * 2 - D(46), D(20));
            _tabs.SetBounds(pad, D(70), ClientSize.Width - pad * 2, D(38));
            int top = D(118);
            foreach (var pg in _pages) pg.SetBounds(0, top, ClientSize.Width, ClientSize.Height - top);
            LayoutHome();
            LayoutAppearance();
            LayoutSettings();
        }

        private int ContentWidth(Panel p)
        {
            return p.ClientSize.Width - D(40) - (p.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
        }

        private int TextHeight(Label l, int width)
        {
            if (string.IsNullOrEmpty(l.Text)) return 0;
            return TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(D(80), width - D(8)), 0), TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft).Height + D(10);
        }

        private int PutText(Label l, int x, int y, int w)
        {
            int h = TextHeight(l, w);
            l.SetBounds(x, y, w, h);
            l.Visible = h > 0;
            return y + h;
        }

        private void LayoutHome()
        {
            var host = _pages[0];
            var p = _content[0];
            p.SuspendLayout();
            int x = D(20), w = ContentWidth(host), y = D(4), ip = D(16), iw = w - 2 * ip;

            // Status card
            int cy = ip;
            _homePreview.SetBounds(ip, cy, iw, D(128)); cy += D(128) + D(12);
            cy = PutText(_statusTitle, ip, cy, iw);
            cy = PutText(_statusDetail, ip, cy + D(2), iw) + ip;
            _statusCard.SetBounds(x, y, w, cy);
            y += cy + D(20);

            y = PutText(_modeLabel, x, y, w) + D(6);
            _mode.SetBounds(x, y, w, D(46)); y += D(46) + D(8);
            y = PutText(_modeHelp, x, y, w) + D(20);

            // Sensor card
            cy = LayoutSensorCard(ip, iw);
            _sensorCard.SetBounds(x, y, w, cy);
            y += cy + D(16);

            _firstRunNote.Visible = _app.WelcomeSession;
            if (_app.WelcomeSession) y = PutText(_firstRunNote, x, y, w) + D(12);

            int bw = (w - D(10)) / 2;
            _demoBtn.Visible = false;
            _doneBtn.SetBounds(x, y, w, D(42));
            y += D(38) + D(20);
            FinishScroll(host, p, y);
        }

        private int LayoutSensorCard(int ip, int iw)
        {
            int y = ip;
            foreach (Control c in _sensorCard.Controls) c.Visible = false;
            y = PutText(_sensorTitle, ip, y, iw) + D(4);
            y = PutText(_sensorText, ip, y, iw);

            if (false) // Phone pairing is intentionally unavailable in this local-only build.
            {
                y += D(12);
                int q = Math.Min(D(150), iw / 2 - D(8));
                _qr.SetBounds(ip, y, q, q);
                _qr.Visible = true;
                int sx = ip + q + D(16), sw = iw - q - D(16);
                int sy = PutText(_step1, sx, y, sw) + D(6);
                sy = PutText(_step2, sx, sy, sw) + D(6);
                sy = PutText(_step3, sx, sy, sw);
                y = Math.Max(y + q, sy) + D(10);
                if (_addresses.Count > 1)
                {
                    _network.SetBounds(ip, y, iw, D(26));
                    _network.Visible = true;
                    y += D(30);
                }
                _link.SetBounds(ip, y, iw, D(18));
                _link.Visible = true;
                y += D(22);
                y = PutText(_certNote, ip, y, iw) + D(8);
                if (!string.IsNullOrEmpty(_bridgeError.Text)) y = PutText(_bridgeError, ip, y, iw) + D(8);
                _helpLink.SetBounds(ip, y, iw, D(20));
                _helpLink.Visible = true;
                y += D(22);
                if (_showHelp)
                {
                    y = PutText(_helpText, ip, y + D(2), iw) + D(8);
                    _firewallBtn.SetBounds(ip, y, Math.Min(iw, D(220)), D(34));
                    _firewallBtn.Visible = true;
                    y += D(34) + D(4);
                }
            }
            if (false) // No external sensor links.
            {
                y += D(8);
                _sensorLink.SetBounds(ip, y, iw, D(20));
                _sensorLink.Visible = true;
                y += D(20);
            }
            return y + ip;
        }

        private void LayoutAppearance()
        {
            var host = _pages[1];
            var p = _content[1];
            p.SuspendLayout();
            int x = D(20), w = ContentWidth(host), y = D(4);
            _lookPreview.SetBounds(x, y, w, D(170)); y += D(170) + D(10);
            _lookDemoBtn.Visible = false; y += D(10);

            y = SliderRow(p, "Movement strength", _intensity, _intensityVal, x, y, w);
            y = SliderRow(p, "Dot size", _size, _sizeVal, x, y, w);
            y = SliderRow(p, "Dots per column", _rows, _rowsVal, x, y, w);
            y = SliderRow(p, "Opacity", _opacity, _opacityVal, x, y, w);
            y = Header(p, "Dot color", x, y, w);
            _style.SetBounds(x, y, w, D(38)); y += D(38) + D(6);
            y = PutText(_styleHelp, x, y, w) + D(18);
            y = Header(p, "Where dots appear", x, y, w);
            _edges.SetBounds(x, y, w, D(38)); y += D(38) + D(18);
            y = Header(p, "Displays", x, y, w);
            _displays.SetBounds(x, y, w, D(38)); y += D(38) + D(14);
            _reset.SetBounds(x, y, w, D(22)); y += D(22) + D(20);
            FinishScroll(host, p, y);
        }

        private int SliderRow(Panel p, string title, Slider s, Label val, int x, int y, int w)
        {
            var h = p.Controls["h:" + title];
            h.SetBounds(x, y, w - D(76), D(30));
            val.SetBounds(x + w - D(70), y, D(70), D(30));
            s.SetBounds(x - D(8), y + D(32), w + D(16), D(30));
            return y + D(32) + D(30) + D(18);
        }

        private int Header(Panel p, string title, int x, int y, int w)
        {
            p.Controls["h:" + title].SetBounds(x, y, w, D(30));
            return y + D(34);
        }

        private void LayoutSettings()
        {
            var host = _pages[2];
            var p = _content[2];
            p.SuspendLayout();
            int x = D(20), w = ContentWidth(host), y = D(8);
            int th = _autostart.PreferredHeight(w);
            _autostart.SetBounds(x, y, w, th); y += th + D(18);
            th = _hideCapture.PreferredHeight(w);
            _hideCapture.SetBounds(x, y, w, th); y += th + D(24);

            y = Header(p, "Motion source", x, y, w);
            _source.SetBounds(x, y, w, D(46)); y += D(46) + D(8);
            y = PutText(_sourceHelp, x, y, w) + D(22);

            y = Header(p, "If the dots move the wrong way", x, y, w);
            _forward.SetBounds(x, y, w, D(46)); y += D(46) + D(8);
            y = PutText(_forwardHelp, x, y, w) + D(22);

            y = Header(p, "Keyboard shortcut", x, y, w);
            y = PutText(_shortcut, x, y, w) + D(22);

            _firewallBtn2.Visible = false;
            _newCodeBtn.Visible = false;
            y += D(8);

            y = Header(p, "About", x, y, w);
            y = PutText(_about, x, y, w) + D(4);
            _links.SetBounds(x, y, w, D(22)); y += D(22) + D(14);
            _uninstallBtn.SetBounds(x, y, D(140), D(36)); y += D(36) + D(20);
            FinishScroll(host, p, y);
        }

        private void FinishScroll(Panel host, Panel content, int contentBottom)
        {
            content.ResumeLayout();
            int w = host.ClientSize.Width;
            if (content.Width != w || content.Height != contentBottom)
                content.SetBounds(0, host.AutoScrollPosition.Y, w, contentBottom);
        }

        private void SwitchPage()
        {
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _tabs.SelectedIndex;
            LayoutAll();
        }

        // ================================================================== live state

        private void RefreshState()
        {
            if (!Visible) return;
            var snap = _hub.Snapshot();
            bool builtIn = _hub.IsPresent(ActiveSource.BuiltIn);
            bool phone = _bridge.PhoneConnected;
            bool demo = _hub.DemoRunning;
            double sensorAge = _hub.GetAge(ActiveSource.BuiltIn);
            bool receiving = builtIn && snap.Fresh && _hub.GetSampleCount(ActiveSource.BuiltIn) > 0;
            double sampleRate = _hub.GetRate(ActiveSource.BuiltIn);

            // Header + status card
            string state;
            if (_s.Mode == CueMode.Off) state = "הנקודות כבויות";
            else if (demo) state = "הדמיית נסיעה פועלת";
            else if (!builtIn) state = "לא זוהה מד תאוצה מובנה";
            else if (!receiving) state = "החיישן זוהה, אך אינו שולח נתונים";
            else if (_s.Mode == CueMode.AlwaysOn || snap.Moving) state = snap.Moving ? "בתנועה · הנקודות מוצגות" : "הנקודות מוצגות";
            else state = "מוכן · הנקודות יוצגו בזמן תנועה";
            _statusTitle.Text = state;
            _subtitle.Text = _s.Mode == CueMode.Off ? "מושהה" : "פועל באזור ההתראות";

            string source = MotionHub.Describe(snap.Source);
            if (snap.Source == ActiveSource.BuiltIn) source = "מד תאוצה וגירוסקופ מובנים";
            _statusDetail.Text = !builtIn
                ? "Windows לא זיהה מד תאוצה מובנה. בדקו את מנהל ההתקנים."
                : !receiving
                    ? (sensorAge < 0 ? "החיישן זוהה, אך עדיין לא התקבלו קריאות תנועה." : "אין קריאות עדכניות מהחיישן (לפני " + sensorAge.ToString("0.0") + " שניות).")
                    : "קריאות בזמן אמת: " + sampleRate.ToString("0") + " לשנייה | X: " + snap.Fx.ToString("+0.00;-0.00;0.00") + " | Y: " + snap.Fy.ToString("+0.00;-0.00;0.00") + " m/s²";

            // Which sensor card to show
            SensorView view;
            view = builtIn ? SensorView.BuiltIn : SensorView.BuiltInMissing;

            switch (view)
            {
                case SensorView.BuiltIn:
                    _sensorTitle.Text = receiving ? "✓  החיישן פעיל ושולח נתונים" : "⚠  החיישן זוהה אך אין נתונים חיים";
                    _sensorTitle.ForeColor = receiving ? Theme.Ok : Theme.Warn;
                    _sensorText.Text = receiving
                        ? _hub.GetStatus(ActiveSource.BuiltIn) + ". הניעו את המחשב בעדינות ובדקו שהערכים X ו־Y משתנים למעלה."
                        : "לא התקבלו נתונים עדכניים מהחיישן. בדקו מנהלי התקנים, מצב טאבלט וחסימת גישה לחיישנים ב־Windows.";
                    _sensorLink.Text = "";
                    break;
                case SensorView.BuiltInMissing:
                    _sensorTitle.Text = "לא זוהה מד תאוצה במחשב";
                    _sensorTitle.ForeColor = Theme.Warn;
                    _sensorText.Text = "Windows אינו חושף כרגע מד תאוצה זמין. יש לבדוק את מנהלי ההתקנים ואת תמיכת החומרה.";
                    _sensorLink.Text = "";
                    break;
                case SensorView.Location:
                    _sensorTitle.Text = "Using location (GPS)";
                    _sensorTitle.ForeColor = Theme.Text;
                    _sensorText.Text = _hub.GetStatus(ActiveSource.Location) + ". GPS updates about once per second, so cues are gentler than with a motion sensor.";
                    _sensorLink.Text = "";
                    break;
                case SensorView.PhoneConnected:
                    _sensorTitle.Text = "✓  Phone connected";
                    _sensorTitle.ForeColor = Theme.Ok;
                    _sensorText.Text = string.Format("Receiving {0:0} readings per second. Keep the SteadyCues page open on your phone; you can dim its screen.",
                        _hub.GetRate(ActiveSource.Phone));
                    _sensorLink.Text = "Show the pairing code";
                    break;
                default:
                    _sensorTitle.Text = "Connect your phone";
                    _sensorTitle.ForeColor = Theme.Text;
                    _sensorText.Text = builtIn
                        ? "Your phone will sense the car's movement and send it to this PC."
                        : "This PC doesn't have a motion sensor, so your phone will feel the car's movement for it. It takes about 20 seconds.";
                    if (_bridge.PageEverLoaded && !phone) _sensorText.Text = "Your phone opened the page. Tap Start on the phone to begin.";
                    _sensorLink.Text = phone ? "Hide the pairing code" : (builtIn ? "Use this PC's sensor instead" : "");
                    break;
            }
            _bridgeError.Text = _bridge.Error != null ? "Phone link couldn't start: " + _bridge.Error : "";
            if (view == SensorView.PhonePair) UpdateLink();

            _demoBtn.Text = demo ? "עצירת הדמיה" : "בדיקת תצוגה";
            _lookDemoBtn.Text = demo ? "עצירת הדמיה" : "תצוגה מקדימה";

            var parts = new List<string>();
            parts.Add( "המחשב: " + (builtIn ? "מד התאוצה זמין" : "לא נמצא מד תאוצה"));
            
            
            _sourceHelp.Text = "מקור תנועה: חיישנים מקומיים בלבד. " + (receiving ? "קריאות נוכחיות: " + sampleRate.ToString("0") + "/שנייה. " + _hub.GetStatus(ActiveSource.BuiltIn) : builtIn ? "החיישן זוהה אך לא התקבל מידע בזמן אמת." : "לא זוהה מד תאוצה נגיש ב־Windows.");

            if (view != _view) { _view = view; LayoutHome(); LayoutSettings(); }
            else
            {
                // Texts may have changed length; relayout cheaply only the affected page.
                if (_tabs.SelectedIndex == 0) LayoutHome();
            }
        }

        private void OnSensorLink()
        {
            switch (_view)
            {
                case SensorView.BuiltIn:
                case SensorView.BuiltInMissing:
                case SensorView.Location:
                    _s.PhoneBridge = true;
                    _s.Source = SourceKind.Automatic;
                    _showPairing = true;
                    _s.NotifyChanged();
                    _source.SetSilently(0);
                    break;
                case SensorView.PhoneConnected:
                    _showPairing = true;
                    break;
                default:
                    if (_bridge.PhoneConnected) _showPairing = false;
                    else { _s.PhoneBridge = false; _s.NotifyChanged(); }
                    break;
            }
            RefreshState();
        }

        private void FillNetworks()
        {
            string current = _network.SelectedItem != null ? ((PhoneBridge.LocalAddress)_network.SelectedItem).Ip : null;
            _network.Items.Clear();
            foreach (var a in _addresses) _network.Items.Add(a);
            int idx = _addresses.FindIndex(a => a.Ip == current);
            if (_network.Items.Count > 0) _network.SelectedIndex = Math.Max(0, idx);
            UpdateLink();
        }

        private void UpdateLink()
        {
            string ip = _network.SelectedItem != null ? ((PhoneBridge.LocalAddress)_network.SelectedItem).Ip : null;
            if (ip == null)
            {
                _qr.Value = null;
                _link.Text = "Not connected to a network. Join Wi-Fi or your phone's hotspot.";
                return;
            }
            string url = _bridge.LinkFor(ip);
            _qr.Value = url;
            if (_link.Text != url) _link.Text = url;
        }

        private void AllowFirewall()
        {
            bool ok = PhoneBridge.AllowThroughFirewall(_bridge.Port);
            MessageBox.Show(this, ok ? "Done. Your phone can now reach SteadyCues on any network. Try scanning the code again."
                                     : "The firewall rule wasn't added. Administrator permission is needed.",
                "SteadyCues", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
