using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SteadyCues.UI
{
    /// <summary>Base for the owner-drawn controls: double buffered, themed, DPI-aware helpers.</summary>
    internal abstract class DrawnControl : Control
    {
        protected bool Hover, Pressed;

        protected DrawnControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Font(10f);
        }

        protected float Dpi { get { return DeviceDpi / 96f; } }
        protected float D(float v) { return v * Dpi; }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected Graphics Prepare(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            return g;
        }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            using (var p = new Pen(Theme.Focus, D(2)))
            using (var path = Theme.Round(RectangleF.Inflate(r, D(2), D(2)), radius + D(2)))
                g.DrawPath(p, path);
        }

        protected static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Center)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.DirectionRightToLeft })
                g.DrawString(s, f, b, r, sf);
        }
    }

    /// <summary>Rounded surface that groups related controls.</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = DeviceDpi / 96f;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 10 * dpi))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, Math.Max(1f, dpi))) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>A row of mutually exclusive options (Windows 11 / Astryx-style segmented control).</summary>
    internal sealed class Segmented : DrawnControl
    {
        private string[] _items = new string[0];
        private int _selected;
        private int _hoverIndex = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented(params string[] items)
        {
            _items = items;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
            Height = 36;
        }

        public string[] Items { get { return _items; } set { _items = value; Invalidate(); } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Length || value == _selected) return;
                _selected = value;
                AccessibleName = _items[value];
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetSilently(int index) { if (index >= 0 && index < _items.Length) { _selected = index; Invalidate(); } }

        private RectangleF Segment(int i)
        {
            float pad = D(3), w = (Width - 2 * pad) / Math.Max(1, _items.Length);
            return new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = -1;
            for (int i = 0; i < _items.Length; i++) if (Segment(i).Contains(e.Location)) idx = i;
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
            Cursor = idx >= 0 && idx != _selected ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            for (int i = 0; i < _items.Length; i++) if (Segment(i).Contains(e.Location)) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && _selected > 0) SelectedIndex = _selected - 1;
            if (e.KeyCode == Keys.Right && _selected < _items.Length - 1) SelectedIndex = _selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = D(8);
            using (var path = Theme.Round(outer, radius))
            using (var b = new SolidBrush(Theme.Subtle))
                g.FillPath(b, path);
            for (int i = 0; i < _items.Length; i++)
            {
                var r = Segment(i);
                bool sel = i == _selected;
                if (sel || i == _hoverIndex)
                {
                    using (var path = Theme.Round(r, radius - D(2)))
                    {
                        using (var b = new SolidBrush(sel ? Theme.Surface : Theme.Mix(Theme.Subtle, Theme.Surface, 0.5)))
                            g.FillPath(b, path);
                        if (sel)
                            using (var p = new Pen(Theme.Border, Math.Max(1f, Dpi)))
                                g.DrawPath(p, path);
                    }
                }
                var font = sel ? Theme.Font(Font.Size, FontStyle.Bold) : Font;
                // Shrink long Hebrew labels rather than allowing them to collide.
                var usable = Math.Max(12f, r.Width - D(12));
                if (g.MeasureString(_items[i], font).Width > usable)
                {
                    float availableSize = Math.Max(7f, Font.Size * usable / Math.Max(1f, g.MeasureString(_items[i], font).Width));
                    font = Theme.Font(availableSize, sel ? FontStyle.Bold : FontStyle.Regular);
                }
                DrawText(g, _items[i], font, sel ? Theme.Text : Theme.Muted, r);
            }
            FocusRing(g, outer, radius);
        }
    }

    /// <summary>Horizontal slider with keyboard, wheel and drag support.</summary>
    internal sealed class Slider : DrawnControl
    {
        private double _value;
        private bool _dragging;
        public double Minimum = 0, Maximum = 1, Step = 0.05;
        public event EventHandler ValueChanged;

        public Slider()
        {
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            Height = 28;
        }

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Math.Max(Minimum, Math.Min(Maximum, Math.Round(value / Step) * Step));
                if (Math.Abs(v - _value) < 1e-9) return;
                _value = v;
                AccessibleDescription = v.ToString("0.##");
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetSilently(double v) { _value = Math.Max(Minimum, Math.Min(Maximum, v)); Invalidate(); }

        private float TrackLeft { get { return D(10); } }
        private float TrackRight { get { return Width - D(10); } }

        private void SetFromX(int x)
        {
            double t = (x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            Value = Minimum + Math.Max(0, Math.Min(1, t)) * (Maximum - Minimum);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _dragging = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromX(e.X); Cursor = Cursors.Hand; base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += Math.Sign(e.Delta) * Step; base.OnMouseWheel(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= Step;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += Step;
            if (e.KeyCode == Keys.Home) Value = Minimum;
            if (e.KeyCode == Keys.End) Value = Maximum;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float cy = Height / 2f, h = D(4);
            double t = (Value - Minimum) / Math.Max(1e-9, Maximum - Minimum);
            float x = TrackLeft + (float)t * (TrackRight - TrackLeft);
            using (var path = Theme.Round(new RectangleF(TrackLeft, cy - h / 2, TrackRight - TrackLeft, h), h / 2))
            using (var b = new SolidBrush(Theme.Subtle))
                g.FillPath(b, path);
            using (var path = Theme.Round(new RectangleF(TrackLeft, cy - h / 2, Math.Max(h, x - TrackLeft), h), h / 2))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);
            float r = D(Hover || _dragging ? 9 : 8);
            var thumb = new RectangleF(x - r, cy - r, 2 * r, 2 * r);
            using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, thumb);
            using (var p = new Pen(Theme.Border, Math.Max(1f, Dpi))) g.DrawEllipse(p, thumb);
            float ir = r * 0.5f;
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, x - ir, cy - ir, 2 * ir, 2 * ir);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Focus, D(2))) g.DrawEllipse(p, RectangleF.Inflate(thumb, D(2), D(2)));
        }
    }

    /// <summary>Switch with a title and optional description.</summary>
    internal sealed class Toggle : DrawnControl
    {
        private bool _checked;
        public string Description = "";
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (value == _checked) return;
                _checked = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetSilently(bool v) { _checked = v; Invalidate(); }

        public int PreferredHeight(int width)
        {
            int h = (int)D(22);
            if (!string.IsNullOrEmpty(Description))
            {
                var sz = TextRenderer.MeasureText(Description, Theme.Font(9f), new Size(width - (int)D(64), 0), TextFormatFlags.WordBreak);
                h += sz.Height + (int)D(2);
            }
            return h;
        }

        protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Checked = !Checked; base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float sw = D(40), sh = D(20);
            var track = new RectangleF(Width - sw - D(2), D(1), sw, sh);
            using (var path = Theme.Round(track, sh / 2))
            {
                using (var b = new SolidBrush(_checked ? Theme.Accent : Theme.Subtle)) g.FillPath(b, path);
                if (!_checked) using (var p = new Pen(Theme.Mix(Theme.Border, Theme.Muted, 0.4), Math.Max(1f, Dpi))) g.DrawPath(p, path);
            }
            float kr = sh / 2 - D(Hover ? 3 : 4);
            float kx = _checked ? track.Right - sh / 2 : track.Left + sh / 2;
            using (var b = new SolidBrush(_checked ? Theme.AccentText : Theme.Muted))
                g.FillEllipse(b, kx - kr, track.Top + sh / 2 - kr, 2 * kr, 2 * kr);
            FocusRing(g, track, sh / 2);

            var textW = Width - sw - D(16);
            TextRenderer.DrawText(g, Text, Theme.Font(10f, FontStyle.Bold), new Rectangle(0, 0, (int)textW, (int)D(24)), Theme.Text,
                TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Description))
                TextRenderer.DrawText(g, Description, Theme.Font(9f), new Rectangle(0, (int)D(24), (int)textW, Height - (int)D(24)), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Rounded push button, primary (filled) or secondary (outlined).</summary>
    internal sealed class FlatButton : DrawnControl
    {
        public bool Primary;

        public FlatButton()
        {
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            Height = 36;
            Font = Theme.Font(10f, FontStyle.Bold);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = D(8);
            Color fill = Primary ? Theme.Accent : Theme.Surface;
            if (Hover) fill = Theme.Mix(fill, Primary ? Theme.Text : Theme.Subtle, Primary ? 0.12 : 0.7);
            if (Pressed) fill = Theme.Mix(fill, Theme.Text, 0.12);
            using (var path = Theme.Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!Primary) using (var p = new Pen(Theme.Border, Math.Max(1f, Dpi))) g.DrawPath(p, path);
            }
            DrawText(g, Text, Font, Primary ? Theme.AccentText : Theme.Text, r);
            FocusRing(g, r, radius);
        }
    }

    /// <summary>Draws a QR code, always dark-on-white with a quiet zone so every camera can read it.</summary>
    internal sealed class QrView : Control
    {
        private QrCode _qr;
        private string _value;

        public QrView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "QR code for connecting your phone";
        }

        public string Value
        {
            get { return _value; }
            set
            {
                if (value == _value) return;
                _value = value;
                _qr = string.IsNullOrEmpty(value) ? null : QrCode.Encode(value);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float dpi = DeviceDpi / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(b, ClientRectangle);
            using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8 * dpi))
            using (var b = new SolidBrush(Color.White))
                g.FillPath(b, path);
            if (_qr == null) return;
            g.SmoothingMode = SmoothingMode.None;
            int quiet = 3, n = _qr.Size + 2 * quiet;
            int cell = Math.Max(1, Math.Min(Width, Height) / n);
            int ox = (Width - cell * _qr.Size) / 2, oy = (Height - cell * _qr.Size) / 2;
            using (var b = new SolidBrush(Color.FromArgb(27, 27, 27)))
                for (int y = 0; y < _qr.Size; y++)
                    for (int x = 0; x < _qr.Size; x++)
                        if (_qr[x, y]) g.FillRectangle(b, ox + x * cell, oy + y * cell, cell, cell);
        }
    }

    /// <summary>
    /// A miniature screen showing the cue dots exactly as configured, moving with the live felt
    /// force. Used as the status visual and as the appearance preview.
    /// </summary>
    internal sealed class CuePreview : Control
    {
        private readonly Settings _settings;
        private readonly MotionHub _hub;
        private readonly Timer _timer = new Timer { Interval = 16 };
        private double _ox, _oy, _vx, _vy, _last;

        public CuePreview(Settings settings, MotionHub hub)
        {
            _settings = settings;
            _hub = hub;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Animation;
            AccessibleName = "Preview of the motion cue dots";
            _timer.Tick += delegate { Step(); Invalidate(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            _timer.Enabled = Visible;
            _last = Clock.Now;
            base.OnVisibleChanged(e);
        }

        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }

        private void Step()
        {
            double now = Clock.Now, dt = Math.Min(0.05, now - _last);
            _last = now;
            var s = _hub.Snapshot();
            double gain = 0.2 * _settings.Intensity, limit = 1.6;
            double tx = limit * Math.Tanh(s.Fx * gain / limit), ty = limit * Math.Tanh(s.Fy * gain / limit);
            const double w = 2 * Math.PI * 1.1, z = 0.85;
            for (int i = 0; i < 4; i++)
            {
                double h = dt / 4;
                _vx += (w * w * (tx - _ox) - 2 * z * w * _vx) * h;
                _vy += (w * w * (ty - _oy) - 2 * z * w * _vy) * h;
                _ox += _vx * h;
                _oy += _vy * h;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float dpi = DeviceDpi / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(b, ClientRectangle);

            // A stylised desktop so the dots have something to sit on.
            var screen = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(screen, 8 * dpi))
            {
                using (var b = new LinearGradientBrush(screen, Theme.Dark ? Theme.Hex(0x2A2A2A) : Theme.Hex(0xE6E6E6),
                    Theme.Dark ? Theme.Hex(0x1F1F1F) : Theme.Hex(0xF5F5F5), 90f))
                    g.FillPath(b, path);
                g.SetClip(path);
            }
            var win = new RectangleF(Width * 0.2f, Height * 0.16f, Width * 0.6f, Height * 0.68f);
            using (var path = Theme.Round(win, 6 * dpi))
            using (var b = new SolidBrush(Theme.Dark ? Theme.Hex(0x363636) : Color.White))
                g.FillPath(b, path);
            using (var b = new SolidBrush(Theme.Dark ? Theme.Hex(0x474747) : Theme.Hex(0xE3E3E3)))
                for (int i = 0; i < 4; i++)
                    g.FillRectangle(b, win.X + win.Width * 0.1f, win.Y + win.Height * (0.22f + i * 0.17f), win.Width * (i == 3 ? 0.45f : 0.8f), 5 * dpi);

            double vis = _settings.Mode == CueMode.Off ? 0.25 : 1;
            float spacing = (float)Height / Math.Max(4, _settings.Rows * 0.55f);
            float r = (float)Math.Min(spacing * 0.3, 3.2 * dpi * _settings.DotSize);
            double alpha = _settings.Opacity * vis;
            Color fill, ring;
            switch (_settings.Style)
            {
                case DotStyle.Dark: fill = Theme.Hex(0x18181B); ring = Color.FromArgb(70, 255, 255, 255); break;
                case DotStyle.Light: fill = Theme.Hex(0xFAFAFA); ring = Color.FromArgb(90, 0, 0, 0); break;
                case DotStyle.Accent: fill = Theme.Accent; ring = Color.White; break;
                default: fill = Theme.Hex(0x202124); ring = Color.White; break;
            }
            double px = _ox * spacing, py = _oy * spacing;
            double wx = px - spacing * Math.Round(px / spacing), wy = py - spacing * Math.Round(py / spacing);
            float depth = spacing * 2;
            bool all = _settings.Edges == EdgeLayout.AllEdges;
            for (int side = 0; side < 2; side++)
                for (int k = -1; k <= 2; k++)
                {
                    double fromEdge = spacing * (k + 0.5) + (side == 0 ? wx : -wx);
                    double x = side == 0 ? fromEdge : Width - fromEdge;
                    double a = alpha * (1 - SmoothStep(depth - spacing * 0.75, depth, fromEdge));
                    for (int j = -8; j <= 8; j++)
                        Dot(g, x, Height / 2.0 + spacing * (j - 0.5) + wy, r, a, fill, ring, dpi);
                }
            if (all)
                for (int side = 0; side < 2; side++)
                    for (int k = -1; k <= 2; k++)
                    {
                        double fromEdge = spacing * (k + 0.5) + (side == 0 ? wy : -wy);
                        double y = side == 0 ? fromEdge : Height - fromEdge;
                        double a = alpha * (1 - SmoothStep(depth - spacing * 0.75, depth, fromEdge));
                        for (int i = -12; i <= 12; i++)
                        {
                            double x = Width / 2.0 + spacing * (i - 0.5) + wx;
                            double end = Math.Min(x - depth, Width - depth - x);
                            Dot(g, x, y, r, a * SmoothStep(0, spacing * 0.75, end), fill, ring, dpi);
                        }
                    }
            g.ResetClip();
            using (var path = Theme.Round(screen, 8 * dpi))
            using (var p = new Pen(Theme.Border, Math.Max(1f, dpi)))
                g.DrawPath(p, path);
        }

        private static void Dot(Graphics g, double x, double y, float r, double a, Color fill, Color ring, float dpi)
        {
            if (a <= 0.01) return;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a * 0.92), fill)))
                g.FillEllipse(b, (float)x - r, (float)y - r, 2 * r, 2 * r);
            using (var p = new Pen(Color.FromArgb((int)(ring.A * a), ring), Math.Max(1f, 1.1f * dpi)))
                g.DrawEllipse(p, (float)x - r, (float)y - r, 2 * r, 2 * r);
        }

        private static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }
    }
}
