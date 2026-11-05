using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SteadyCues
{
    public enum CueMode { Automatic = 0, AlwaysOn = 1, Off = 2 }
    public enum DotStyle { Adaptive = 0, Dark = 1, Light = 2, Accent = 3 }
    public enum EdgeLayout { Sides = 0, AllEdges = 1 }
    public enum DisplayTarget { Primary = 0, All = 1 }
    public enum SourceKind { Automatic = 0, BuiltInSensor = 1, Phone = 2, Location = 3 }
    public enum ForwardAdjust { None = 0, Right90 = 1, Rotate180 = 2, Left90 = 3 }

    /// <summary>User settings, persisted as a small INI-style file in %APPDATA%\SteadyCues.</summary>
    public sealed class Settings
    {
        public CueMode Mode = CueMode.Automatic;
        public double Intensity = 1.0;     // 0.25 .. 2.0
        public double DotSize = 1.0;       // 0.5 .. 2.0
        public int Rows = 11;              // 6 .. 18 dots per column
        public double Opacity = 0.9;       // 0.2 .. 1.0
        public DotStyle Style = DotStyle.Adaptive;
        public EdgeLayout Edges = EdgeLayout.Sides;
        public DisplayTarget Displays = DisplayTarget.Primary;
        public SourceKind Source = SourceKind.BuiltInSensor;
        public ForwardAdjust Forward = ForwardAdjust.None;
        public bool HideFromCapture = true;
        public bool PhoneBridge = false;
        public int PhonePort = 47821;
        public string PhoneToken = "";
        public bool FirstRun = true;

        public event EventHandler Changed;

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteadyCues"); }
        }
        private static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        public void NotifyChanged()
        {
            Save();
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                    s.Mode = GetEnum(map, "mode", s.Mode);
                    s.Intensity = GetDouble(map, "intensity", s.Intensity, 0.25, 2.0);
                    s.DotSize = GetDouble(map, "dotSize", s.DotSize, 0.5, 2.0);
                    s.Rows = (int)GetDouble(map, "rows", s.Rows, 6, 18);
                    s.Opacity = GetDouble(map, "opacity", s.Opacity, 0.2, 1.0);
                    s.Style = GetEnum(map, "style", s.Style);
                    s.Edges = GetEnum(map, "edges", s.Edges);
                    s.Displays = GetEnum(map, "displays", s.Displays);
                    s.Source = SourceKind.BuiltInSensor; // Local sensors only, including for old settings.
                    s.Forward = GetEnum(map, "forward", s.Forward);
                    s.HideFromCapture = GetBool(map, "hideFromCapture", s.HideFromCapture);
                    s.PhoneBridge = false; // Disable phone bridge unconditionally.
                    s.PhonePort = (int)GetDouble(map, "phonePort", s.PhonePort, 1024, 65535);
                    string tok;
                    if (map.TryGetValue("phoneToken", out tok)) s.PhoneToken = tok;
                    s.FirstRun = GetBool(map, "firstRun", s.FirstRun);
                }
            }
            catch { /* corrupt file: fall back to defaults */ }
            if (string.IsNullOrEmpty(s.PhoneToken) || s.PhoneToken.Length < 12)
            {
                // Persist right away so the phone link (and any bookmark of it) survives restarts.
                s.PhoneToken = NewToken();
                s.Save();
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var ci = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine("# SteadyCues settings");
                sb.AppendLine("mode=" + Mode);
                sb.AppendLine("intensity=" + Intensity.ToString("0.###", ci));
                sb.AppendLine("dotSize=" + DotSize.ToString("0.###", ci));
                sb.AppendLine("rows=" + Rows.ToString(ci));
                sb.AppendLine("opacity=" + Opacity.ToString("0.###", ci));
                sb.AppendLine("style=" + Style);
                sb.AppendLine("edges=" + Edges);
                sb.AppendLine("displays=" + Displays);
                sb.AppendLine("source=" + SourceKind.BuiltInSensor);
                sb.AppendLine("forward=" + Forward);
                sb.AppendLine("hideFromCapture=" + HideFromCapture);
                sb.AppendLine("phoneBridge=False");
                sb.AppendLine("phonePort=" + PhonePort.ToString(ci));
                sb.AppendLine("phoneToken=" + PhoneToken);
                sb.AppendLine("firstRun=" + FirstRun);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { /* settings are best effort; never crash the overlay over them */ }
        }

        public static string NewToken()
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        private static T GetEnum<T>(Dictionary<string, string> m, string key, T def) where T : struct
        {
            string v; T r;
            if (m.TryGetValue(key, out v) && Enum.TryParse(v, true, out r) && Enum.IsDefined(typeof(T), r)) return r;
            return def;
        }

        private static double GetDouble(Dictionary<string, string> m, string key, double def, double min, double max)
        {
            string v; double r;
            if (m.TryGetValue(key, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) && !double.IsNaN(r))
                return Math.Max(min, Math.Min(max, r));
            return def;
        }

        private static bool GetBool(Dictionary<string, string> m, string key, bool def)
        {
            string v; bool r;
            if (m.TryGetValue(key, out v) && bool.TryParse(v, out r)) return r;
            return def;
        }
    }
}
