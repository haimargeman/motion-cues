using System;
using System.Diagnostics;
using Windows.Devices.Sensors;
using Windows.Foundation;

namespace SteadyCuesV2
{
    // Only Windows internal motion sensors. No phone bridge, network code, or GPS.
    internal sealed class SensorService : IDisposable
    {
        readonly object gate=new object();
        readonly Stopwatch timer=Stopwatch.StartNew();
        readonly MotionEngine engine=new MotionEngine();
        readonly TypedEventHandler<Accelerometer,AccelerometerReadingChangedEventArgs> onAcc;
        readonly TypedEventHandler<Gyrometer,GyrometerReadingChangedEventArgs> onGyro;
        Accelerometer acc;
        Gyrometer gyro;
        V3 angularVelocity;
        double lastGyro=-100, lastAcc=-1, rateStart=-1;
        int count;
        double rate;
        long samples;
        V3 rawAcceleration;
        bool found, hasGyro;
        string error="";
        public SensorService() { onAcc=Receive; onGyro=ReceiveGyro; }
        public void SetDirection(int value) { lock(gate) { engine.Direction=value; engine.Reset(); } }
        public void Calibrate() { lock(gate) engine.Reset(); }

        public void Start()
        {
            try {
                acc=Accelerometer.GetDefault();
                if(acc==null) { lock(gate)error="Windows לא חשף מד תאוצה. בדוק מנהלי התקנים וחיישנים."; return; }
                acc.ReportInterval=Math.Max(acc.MinimumReportInterval,16u);
                acc.ReadingChanged+=onAcc;
                found=true;
                try {
                    gyro=Gyrometer.GetDefault();
                    if(gyro!=null) {
                        gyro.ReportInterval=Math.Max(gyro.MinimumReportInterval,16u);
                        gyro.ReadingChanged+=onGyro;
                        hasGyro=true;
                    }
                } catch(Exception ex) { lock(gate)error="הגירוסקופ אינו זמין: "+ex.Message; }
            } catch(Exception ex) { lock(gate)error="שגיאת חיישן: "+ex.Message; found=false; }
        }
        void ReceiveGyro(Gyrometer source,GyrometerReadingChangedEventArgs args)
        {
            try {
                var r=args.Reading;const double d=Math.PI/180;
                lock(gate) {
                    angularVelocity=new V3(r.AngularVelocityX*d,r.AngularVelocityY*d,r.AngularVelocityZ*d);
                    lastGyro=timer.Elapsed.TotalSeconds;
                }
            } catch {}
        }
        void Receive(Accelerometer source,AccelerometerReadingChangedEventArgs args)
        {
            try {
                var r=args.Reading;
                // Windows acceleration uses g. Negation converts gravity pull to proper acceleration.
                var a=new V3(-r.AccelerationX*9.80665,-r.AccelerationY*9.80665,-r.AccelerationZ*9.80665);
                double now=timer.Elapsed.TotalSeconds;
                lock(gate) {
                    double dt=lastAcc>=0?now-lastAcc:0.016;
                    V3? w=now-lastGyro<0.3?angularVelocity:(V3?)null;
                    rawAcceleration=a;
                    engine.Process(a,w,dt);
                    lastAcc=now;
                    samples++;
                    if(rateStart<0)rateStart=now;
                    count++;
                    if(now-rateStart>=1) {
                        rate=count/(now-rateStart);count=0;rateStart=now;
                    }
                }
            } catch(Exception ex) { lock(gate) error="עיבוד חיישן נכשל: "+ex.Message; }
        }
        public SensorSnapshot Snapshot()
        {
            lock(gate) {
                double age=lastAcc<0?-1:timer.Elapsed.TotalSeconds-lastAcc;
                bool fresh=age>=0&&age<1.2;
                return new SensorSnapshot {
                    Found=found,Gyro=hasGyro,Fresh=fresh,Age=age,
                    Rate=fresh?rate:0,Samples=samples,
                    Moving=fresh&&engine.Moving,
                    X=fresh?engine.X:0,Y=fresh?engine.Y:0,
                    RawX=rawAcceleration.X,RawY=rawAcceleration.Y,RawZ=rawAcceleration.Z,
                    GyroX=angularVelocity.X,GyroY=angularVelocity.Y,GyroZ=angularVelocity.Z,
                    Strength=fresh?engine.Strength:0,Error=error
                };
            }
        }
        public void Dispose()
        {
            try { if(acc!=null){acc.ReadingChanged-=onAcc;acc.ReportInterval=0;} }catch{}
            try { if(gyro!=null){gyro.ReadingChanged-=onGyro;gyro.ReportInterval=0;} }catch{}
        }
    }
}
