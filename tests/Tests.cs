using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteadyCues.Tests
{
    /// <summary>
    /// Dependency-free test runner (the project builds with the compiler that ships with
    /// Windows, so there is no test framework). Run with .\test.ps1.
    /// </summary>
    internal static class Runner
    {
        private static int Main()
        {
            int passed = 0, failed = 0;
            var tests = new List<MethodInfo>();
            foreach (var t in new[] { typeof(MotionTests), typeof(QrTests), typeof(SettingsTests) })
                tests.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.Static));
            foreach (var m in tests)
            {
                try { m.Invoke(null, null); passed++; Console.WriteLine("  pass  " + m.DeclaringType.Name + "." + m.Name); }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  FAIL  " + m.DeclaringType.Name + "." + m.Name + ": " + ex.InnerException.Message);
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed", passed, failed);
            return failed == 0 ? 0 : 1;
        }
    }

    internal static class Assert
    {
        public static void True(bool c, string what) { if (!c) throw new Exception(what); }
        public static void Near(double actual, double expected, double tol, string what)
        {
            if (Math.Abs(actual - expected) > tol)
                throw new Exception(string.Format("{0}: expected {1:0.###} +/- {2}, got {3:0.###}", what, expected, tol, actual));
        }
    }

    public static class MotionTests
    {
        private const double G = 9.81, Dt = 1.0 / 60;

        /// <summary>Feed a constant proper acceleration for some seconds; returns the last output.</summary>
        private static double[] Run(DeviceFrameProcessor p, Vec3 a, double seconds, Vec3? gyro = null)
        {
            double fx = 0, fy = 0;
            for (int i = 0; i < seconds * 60; i++) p.Process(a, gyro, Dt, out fx, out fy);
            return new[] { fx, fy };
        }

        public static void AtRestThereIsNoCue()
        {
            var p = new DeviceFrameProcessor();
            var r = Run(p, new Vec3(0, 0, G), 3);
            Assert.Near(r[0], 0, 0.01, "fx at rest");
            Assert.Near(r[1], 0, 0.01, "fy at rest");
        }

        public static void FlatDeviceSpeedingUpMovesDotsDown()
        {
            // Flat, screen up, top edge pointing forward. Car accelerates at 2.5 m/s².
            var p = new DeviceFrameProcessor();
            Run(p, new Vec3(0, 0, G), 3, new Vec3());
            var r = Run(p, new Vec3(0, 2.5, G), 1, new Vec3());
            Assert.True(r[1] > 2.0, "felt force should push back (dots down), got fy=" + r[1]);
            Assert.Near(r[0], 0, 0.1, "no sideways cue");
        }

        public static void LeftTurnMovesDotsRight()
        {
            var p = new DeviceFrameProcessor();
            Run(p, new Vec3(0, 0, G), 3, new Vec3());
            // Turning left: the car accelerates toward the left (device -x).
            var r = Run(p, new Vec3(-3, 0, G), 1, new Vec3());
            Assert.True(r[0] > 2.4, "passenger is pushed right, got fx=" + r[0]);
        }

        public static void UprightLaptopLidBrakingMovesDotsUp()
        {
            // Screen upright facing the passenger: its back (-z) points forward. Braking = accel toward +z.
            var p = new DeviceFrameProcessor();
            Run(p, new Vec3(0, G, 0), 3, new Vec3());
            var r = Run(p, new Vec3(0, G, 3), 1, new Vec3());
            Assert.True(r[1] < -2.4, "braking should throw you forward (dots up), got fy=" + r[1]);
        }

        public static void LongAccelerationIsNotForgottenQuicklyWithGyro()
        {
            var p = new DeviceFrameProcessor();
            Run(p, new Vec3(0, 0, G), 3, new Vec3());
            var r = Run(p, new Vec3(0, 2.5, G), 4, new Vec3());
            Assert.True(r[1] > 1.3, "after 4 s of acceleration the cue should persist, got fy=" + r[1]);
        }

        public static void PickingUpTheDeviceProducesNoCue()
        {
            var p = new DeviceFrameProcessor();
            Run(p, new Vec3(0, 0, G), 3, new Vec3());
            double maxCue = 0;
            // Tilt from flat to upright over half a second while the gyro reports the rotation.
            for (int i = 0; i < 30; i++)
            {
                double t = (i + 1) / 30.0 * Math.PI / 2, fx, fy;
                p.Process(new Vec3(0, G * Math.Sin(t), G * Math.Cos(t)), new Vec3(3.1, 0, 0), Dt, out fx, out fy);
                maxCue = Math.Max(maxCue, Math.Sqrt(fx * fx + fy * fy));
            }
            var r = Run(p, new Vec3(0, G, 0), 1.5, new Vec3());
            maxCue = Math.Max(maxCue, Math.Sqrt(r[0] * r[0] + r[1] * r[1]));
            Assert.True(maxCue < 0.3, "handling the device must not fling the dots, max cue " + maxCue);
        }

        public static void ForwardAdjustReversesDirection()
        {
            var p = new DeviceFrameProcessor { Adjust = ForwardAdjust.Rotate180 };
            Run(p, new Vec3(0, 0, G), 3, new Vec3());
            var r = Run(p, new Vec3(0, 2.5, G), 1, new Vec3());
            Assert.True(r[1] < -2.0, "Reverse should flip forward/back, got fy=" + r[1]);
        }

        public static void VehicleAxesAreOrthonormal()
        {
            foreach (var up in new[] { new Vec3(0, 0, 1), new Vec3(0, 1, 0), new Vec3(0, 0.7071, 0.7071).Normalized, new Vec3(0.3, 0.2, 0.93).Normalized })
            {
                Vec3 f, r;
                DeviceFrameProcessor.VehicleAxes(up, ForwardAdjust.None, out f, out r);
                Assert.Near(f.Length, 1, 1e-6, "forward is unit");
                Assert.Near(r.Length, 1, 1e-6, "right is unit");
                Assert.Near(Vec3.Dot(f, up), 0, 1e-6, "forward is horizontal");
                Assert.Near(Vec3.Dot(f, r), 0, 1e-6, "forward perpendicular to right");
            }
        }

        public static void HubRejectsExternalMotionAndAcceptsLocalSensors()
        {
            var s = new Settings();
            var hub = new MotionHub(s);
            Assert.True(hub.Resolve() == ActiveSource.None, "no source initially");
            hub.Push(ActiveSource.Phone, 1, 2, 0.016, false);
            Assert.True(hub.Resolve() == ActiveSource.None, "phone must never become active");
            hub.Push(ActiveSource.Location, 1, 2, 0.016, false);
            Assert.True(hub.Resolve() == ActiveSource.None, "GPS must never become active");
            hub.SetPresent(ActiveSource.BuiltIn, true, "local sensor available");
            hub.Push(ActiveSource.BuiltIn, 3, 4, 0.016, false);
            var snap = hub.Snapshot();
            Assert.True(snap.Source == ActiveSource.BuiltIn && snap.Fresh, "only built-in sensor becomes active");
            Assert.Near(snap.Fy, 4, 1e-9, "snapshot carries local force");
        }
    }

    public static class QrTests
    {
        public static void SizeMatchesVersionForShortLinks()
        {
            var q = QrCode.Encode("https://192.168.1.20:47821/m/abcdefghjkmnpqrs");
            Assert.True(q.Size >= 21 && (q.Size - 17) % 4 == 0, "valid QR size, got " + q.Size);
            Assert.True(q.Size <= 37, "a pairing link fits in a small code, got " + q.Size);
        }

        public static void FinderPatternsArePresent()
        {
            var q = QrCode.Encode("hello");
            int n = q.Size;
            foreach (var o in new[] { new[] { 0, 0 }, new[] { n - 7, 0 }, new[] { 0, n - 7 } })
            {
                for (int i = 0; i < 7; i++)
                {
                    Assert.True(q[o[0] + i, o[1]] && q[o[0] + i, o[1] + 6] && q[o[0], o[1] + i] && q[o[0] + 6, o[1] + i], "finder border");
                }
                Assert.True(q[o[0] + 3, o[1] + 3], "finder centre");
                Assert.True(!q[o[0] + 1, o[1] + 1], "finder gap");
            }
        }
    }

    public static class SettingsTests
    {
        public static void NewTokensAreLongAndDistinct()
        {
            string a = Settings.NewToken(), b = Settings.NewToken();
            Assert.True(a.Length == 16 && b.Length == 16, "16 characters");
            Assert.True(a != b, "random");
        }
    }
}
