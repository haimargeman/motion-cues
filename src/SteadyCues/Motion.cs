using System;

namespace SteadyCues
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.X, -a.Y, -a.Z); }
        public static Vec3 operator *(Vec3 a, double k) { return new Vec3(a.X * k, a.Y * k, a.Z * k); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public static double Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
        public Vec3 Normalized { get { var l = Length; return l > 1e-9 ? this * (1.0 / l) : new Vec3(); } }
    }

    /// <summary>
    /// Turns raw accelerometer samples from a device (laptop, tablet or phone) into the
    /// force a passenger feels in the vehicle's frame of reference.
    ///
    /// Input is "proper acceleration" in the device frame, in m/s² (x = right, y = up the
    /// screen, z = out of the screen; at rest it points up with magnitude g). The gravity
    /// direction is tracked with a slow low-pass filter, which gives the horizontal plane.
    /// The vehicle's forward axis is inferred from how the device is held: an upright screen
    /// (laptop lid, dashboard mount) faces the passenger, so its back points forward; a flat
    /// device (tablet on the lap, phone on a console) points forward with its top edge.
    /// </summary>
    public sealed class DeviceFrameProcessor
    {
        private const double GravityTau = 8.0;       // seconds, when a gyroscope can flag re-orientation
        private const double GravityTauNoGyro = 4.0; // seconds, accelerometer only
        private const double FastGravityTau = 0.15;  // used while the device is being re-oriented
        private const double TiltRate = 0.35;        // rad/s about a horizontal axis = someone is moving the device
        private const double NoiseTau = 0.06;       // ~2.6 Hz low-pass on the output

        private Vec3 _gravity, _linear;
        private bool _init;
        private double _bigSince = -1, _time, _handledUntil = -1;

        public ForwardAdjust Adjust = ForwardAdjust.None;

        public void Reset() { _init = false; _bigSince = -1; _handledUntil = -1; _linear = new Vec3(); }

        /// <summary>Returns false while the sample cannot be interpreted (free fall, garbage).</summary>
        public bool Process(Vec3 a, double dt, out double fx, out double fy)
        {
            return Process(a, null, dt, out fx, out fy);
        }

        /// <summary>
        /// Same, with the device's angular velocity (rad/s, device frame) when a gyroscope exists.
        /// Rotation about a horizontal axis means the device is being tilted or picked up, which
        /// cars hardly ever do; that lets gravity be tracked slowly (keeping long accelerations
        /// and bends visible) yet re-converge instantly when the laptop lid or phone moves.
        /// </summary>
        public bool Process(Vec3 a, Vec3? gyro, double dt, out double fx, out double fy)
        {
            fx = fy = 0;
            if (double.IsNaN(a.X) || double.IsNaN(a.Y) || double.IsNaN(a.Z)) return false;
            if (dt <= 0 || dt > 0.5) dt = 0.016;
            _time += dt;
            if (!_init) { _gravity = a; _linear = new Vec3(); _init = true; }

            // Sustained large deviation means the device was re-oriented (lid moved, phone picked
            // up), not that the car is pulling 0.4 g. Re-converge on gravity quickly in that case.
            double dev = (a - _gravity).Length;
            if (dev > 4.0) { if (_bigSince < 0) _bigSince = _time; }
            else _bigSince = -1;
            if (gyro.HasValue && _gravity.Length > 3)
            {
                Vec3 u = _gravity.Normalized, w = gyro.Value;
                double tilt = (w - u * Vec3.Dot(w, u)).Length;
                if (tilt > TiltRate) _handledUntil = _time + 0.8;
            }
            // Without a gyroscope, fall back to "a large deviation that persists" (which hard braking
            // could also trigger, hence only when nothing better is available).
            bool handled = _time < _handledUntil || (!gyro.HasValue && _bigSince >= 0 && _time - _bigSince > 0.3);
            double tau = handled ? FastGravityTau : gyro.HasValue ? GravityTau : GravityTauNoGyro;

            _gravity = _gravity + (a - _gravity) * (1 - Math.Exp(-dt / tau));
            double gm = _gravity.Length;
            if (gm < 3.0) return false;

            if (handled)
            {
                // Someone is moving the device: its motion says nothing about the car, so hold the
                // cues still instead of flinging the dots around.
                _linear = new Vec3();
                return true;
            }
            Vec3 up = _gravity * (1.0 / gm);

            _linear = _linear + ((a - _gravity) - _linear) * (1 - Math.Exp(-dt / NoiseTau));

            Vec3 fwd, right;
            VehicleAxes(up, Adjust, out fwd, out right);
            double aF = Vec3.Dot(_linear, fwd);
            double aR = Vec3.Dot(_linear, right);

            // The felt (inertial) force is opposite to the vehicle's acceleration.
            // Screen x: + = felt to the right. Screen y: + = felt backwards (dots move down).
            fx = -aR;
            fy = aF;
            return true;
        }

        public static void VehicleAxes(Vec3 up, ForwardAdjust adjust, out Vec3 fwd, out Vec3 right)
        {
            Vec3 yAxis = new Vec3(0, 1, 0), backAxis = new Vec3(0, 0, -1);
            double tilt = Math.Abs(up.Z);                // 1 = lying flat, 0 = upright
            double w = SmoothStep(0.6, 0.85, tilt);
            Vec3 f = Horizontal(backAxis, up) * (1 - w) + Horizontal(yAxis, up) * w;
            if (f.Length < 1e-3) f = Horizontal(yAxis, up) + Horizontal(backAxis, up);
            if (f.Length < 1e-3) f = Horizontal(new Vec3(1, 0, 0), up);
            fwd = f.Normalized;
            right = Vec3.Cross(fwd, up).Normalized;
            switch (adjust)
            {
                case ForwardAdjust.Right90: { var t = fwd; fwd = right; right = -t; break; }
                case ForwardAdjust.Rotate180: fwd = -fwd; right = -right; break;
                case ForwardAdjust.Left90: { var t = fwd; fwd = -right; right = t; break; }
            }
        }

        private static Vec3 Horizontal(Vec3 v, Vec3 up) { return v - up * Vec3.Dot(v, up); }

        private static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }
    }

    public enum ActiveSource { None, BuiltIn, Phone, Location, Demo }

    public struct MotionSnapshot
    {
        public double Fx, Fy;          // felt force, m/s² (x right, y backwards)
        public bool Moving;            // vehicle motion detected recently (drives Automatic mode)
        public ActiveSource Source;
        public bool Fresh;             // the active source delivered data recently
    }

    /// <summary>Collects motion from every source and decides which one drives the dots.</summary>
    public sealed class MotionHub
    {
        private sealed class Channel
        {
            public double Fx, Fy, LastT = -1e9, Activity, LastMovingT = -1e9, Rate, RateWindowT;
            public int RateCount;
            public bool Present;
            public string Status = "";
        }

        private const double MovingThreshold = 0.22; // m/s², smoothed |felt|
        private const double MovingHold = 25.0;      // seconds the dots linger after motion stops

        private readonly object _sync = new object();
        private readonly Channel[] _ch = new Channel[5];
        private readonly Settings _settings;
        private bool _demo;

        public MotionHub(Settings settings)
        {
            _settings = settings;
            for (int i = 0; i < _ch.Length; i++) _ch[i] = new Channel();
        }

        public bool DemoRunning
        {
            get { lock (_sync) return _demo; }
            set { lock (_sync) _demo = value; }
        }

        public void SetPresent(ActiveSource s, bool present, string status)
        {
            lock (_sync) { var c = _ch[(int)s]; c.Present = present; c.Status = status ?? ""; }
        }

        public string GetStatus(ActiveSource s) { lock (_sync) return _ch[(int)s].Status; }
        public bool IsPresent(ActiveSource s) { lock (_sync) return _ch[(int)s].Present; }
        public double GetRate(ActiveSource s) { lock (_sync) { var c = _ch[(int)s]; return Clock.Now - c.LastT < 2 ? c.Rate : 0; } }
        public bool IsFresh(ActiveSource s, double maxAge) { lock (_sync) return Clock.Now - _ch[(int)s].LastT < maxAge; }

        /// <summary>Report a felt-force sample. movingHint forces "vehicle is moving" (e.g. GPS speed).</summary>
        public void Push(ActiveSource s, double fx, double fy, double dt, bool movingHint)
        {
            double now = Clock.Now;
            lock (_sync)
            {
                var c = _ch[(int)s];
                if (dt <= 0 || dt > 2) dt = 0.016;
                c.Fx = fx; c.Fy = fy; c.LastT = now; c.Present = true;
                double mag = Math.Sqrt(fx * fx + fy * fy);
                c.Activity += (mag - c.Activity) * (1 - Math.Exp(-dt / 2.0));
                if (movingHint || c.Activity > MovingThreshold) c.LastMovingT = now;
                c.RateCount++;
                if (now - c.RateWindowT >= 1.0)
                {
                    c.Rate = c.RateCount / (now - c.RateWindowT);
                    c.RateCount = 0; c.RateWindowT = now;
                }
            }
        }

        public ActiveSource Resolve()
        {
            lock (_sync) return ResolveLocked(Clock.Now);
        }

        private ActiveSource ResolveLocked(double now)
        {
            if (_demo) return ActiveSource.Demo;
            // Hard policy: only the computer's own accelerometer/gyroscope.
            if (_ch[(int)ActiveSource.BuiltIn].Present) return ActiveSource.BuiltIn;
            return ActiveSource.None;
        }

        public MotionSnapshot Snapshot()
        {
            double now = Clock.Now;
            lock (_sync)
            {
                var snap = new MotionSnapshot();
                snap.Source = ResolveLocked(now);
                if (snap.Source == ActiveSource.None) return snap;
                var c = _ch[(int)snap.Source];
                double maxAge = snap.Source == ActiveSource.Location ? 4.0 : 1.0;
                snap.Fresh = now - c.LastT < maxAge;
                if (snap.Fresh) { snap.Fx = c.Fx; snap.Fy = c.Fy; }
                snap.Moving = now - c.LastMovingT < MovingHold;
                return snap;
            }
        }

        public static string Describe(ActiveSource s)
        {
            switch (s)
            {
                case ActiveSource.BuiltIn: return "This PC's motion sensor";
                case ActiveSource.Phone: return "Phone";
                case ActiveSource.Location: return "Location (GPS)";
                case ActiveSource.Demo: return "Demo drive";
                default: return "No motion source";
            }
        }
    }
}
