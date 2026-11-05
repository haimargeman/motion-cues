using System;
using Windows.Devices.Sensors;
using Windows.Foundation;

namespace SteadyCues
{
    /// <summary>
    /// Reads the PC's own accelerometer (and gyroscope, when present) through the Windows Sensor
    /// API. Present on most 2-in-1s and tablets (Surface, Yoga, ...), rarely on clamshell laptops.
    /// </summary>
    internal sealed class BuiltInSensorSource : IDisposable
    {
        private readonly MotionHub _hub;
        private readonly DeviceFrameProcessor _proc = new DeviceFrameProcessor();
        private readonly TypedEventHandler<Accelerometer, AccelerometerReadingChangedEventArgs> _handler;
        private readonly TypedEventHandler<Gyrometer, GyrometerReadingChangedEventArgs> _gyroHandler;
        private Accelerometer _acc;
        private Gyrometer _gyro;
        private double _lastT, _gyroT = -1;
        private Vec3 _omega;
        private readonly object _gate = new object();

        public BuiltInSensorSource(MotionHub hub)
        {
            _hub = hub;
            _handler = OnReading;
            _gyroHandler = OnGyro;
        }

        public ForwardAdjust Adjust { set { lock (_gate) _proc.Adjust = value; } }

        public void Start()
        {
            try
            {
                _acc = Accelerometer.GetDefault();
            }
            catch (Exception) { _acc = null; }

            if (_acc == null)
            {
                _hub.SetPresent(ActiveSource.BuiltIn, false, "No accelerometer found on this PC");
                return;
            }
            try
            {
                _acc.ReportInterval = Math.Max(_acc.MinimumReportInterval, 16u);
                _acc.ReadingChanged += _handler;
                _hub.SetPresent(ActiveSource.BuiltIn, true, "Accelerometer ready");
            }
            catch (Exception ex)
            {
                _hub.SetPresent(ActiveSource.BuiltIn, false, "Accelerometer unavailable: " + ex.Message);
                _acc = null;
                return;
            }

            try
            {
                _gyro = Gyrometer.GetDefault();
                if (_gyro != null)
                {
                    _gyro.ReportInterval = Math.Max(_gyro.MinimumReportInterval, 16u);
                    _gyro.ReadingChanged += _gyroHandler;
                }
            }
            catch (Exception) { _gyro = null; } // optional: tilt detection falls back to accelerometer only
            _hub.SetPresent(ActiveSource.BuiltIn, true, _gyro != null
                ? "Accelerometer and gyroscope connected; waiting for samples"
                : "Accelerometer connected; no gyroscope available; waiting for samples");
        }

        private void OnGyro(Gyrometer sender, GyrometerReadingChangedEventArgs e)
        {
            var r = e.Reading;
            const double degToRad = Math.PI / 180.0;
            lock (_gate)
            {
                _omega = new Vec3(r.AngularVelocityX, r.AngularVelocityY, r.AngularVelocityZ) * degToRad;
                _gyroT = Clock.Now;
            }
        }

        private void OnReading(Accelerometer sender, AccelerometerReadingChangedEventArgs e)
        {
            var r = e.Reading;
            // Windows reports the gravity "pull" in g (flat, screen up: z = -1). Negate to get
            // proper acceleration, which is what DeviceFrameProcessor expects.
            var a = new Vec3(-r.AccelerationX, -r.AccelerationY, -r.AccelerationZ) * 9.80665;
            double now = Clock.Now, fx, fy, dt;
            lock (_gate)
            {
                dt = _lastT > 0 ? now - _lastT : 0.016;
                _lastT = now;
                Vec3? gyro = now - _gyroT < 0.25 ? _omega : (Vec3?)null;
                if (!_proc.Process(a, gyro, dt, out fx, out fy)) return;
            }
            _hub.Push(ActiveSource.BuiltIn, fx, fy, dt, false);
        }

        public void Dispose()
        {
            if (_acc != null)
            {
                try { _acc.ReadingChanged -= _handler; _acc.ReportInterval = 0; } catch { }
                _acc = null;
            }
            if (_gyro != null)
            {
                try { _gyro.ReadingChanged -= _gyroHandler; _gyro.ReportInterval = 0; } catch { }
                _gyro = null;
            }
        }
    }
}
