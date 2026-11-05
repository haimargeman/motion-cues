using System;
using SteadyCuesV2;

namespace SteadyCuesV2.Tests
{
    internal static class Runner
    {
        static int passed;
        static void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
        static void Run(MotionEngine e,V3 a,int count,V3? gyro)
        {
            for(int i=0;i<count;i++)e.Process(a,gyro,1.0/60);
        }
        static int Main()
        {
            try
            {
                const double g=9.80665;
                var e=new MotionEngine();
                Run(e,new V3(0,0,g),120,new V3());
                Check(e.Strength<0.02,"stationary sensor has no false acceleration");
                Run(e,new V3(0,2.0,g),30,new V3());
                Check(e.Y>1.2,"strong forward acceleration remains responsive");
                Check(e.Moving,"acceleration triggers movement without slow 2-second detector");
                e.Reset();
                Run(e,new V3(0,0,g),120,new V3());
                Run(e,new V3(-2.0,0,g),30,new V3());
                Check(e.X>1.2,"left acceleration creates rightward inertial cue");
                e.Reset();
                Run(e,new V3(0,0,g),120,new V3());
                e.Direction=2;
                Run(e,new V3(0,2.0,g),30,new V3());
                Check(e.Y< -1.2,"180 degree orientation correction reverses cue");
                e.Reset();
                Run(e,new V3(0,0,g),120,new V3());
                e.Process(new V3(0,3,g),new V3(2.0,0,0),1.0/60);
                Check(!double.IsNaN(e.Strength) && e.Strength<2.0,"gyroscope rotation is damped without disabling the motion stream");
                e.Reset();
                e.Direction=0;
                Run(e,new V3(0,0,g),120,(V3?)null);
                Run(e,new V3(0,1.5,g),30,(V3?)null);
                Check(e.Y>0.8,"accelerometer alone is supported");
                Console.WriteLine("All "+passed+" rebuild engine checks passed.");
                return 0;
            }
            catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex);return 1; }
        }
    }
}
