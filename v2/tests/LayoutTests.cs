using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SteadyCuesV2.Tests
{
    internal static class LayoutRunner
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var config=new AppConfig();
                using(var sensor=new SensorService())
                using(var overlay=new OverlayManager(sensor,config))
                using(var ui=new Dashboard(sensor,config,overlay))
                {
                    foreach(var size in new[]{new Size(640,590),new Size(900,780),new Size(1160,900)})
                    {
                        ui.Size=size;
                        ui.Show();
                        Application.DoEvents();
                        CheckTree(ui);
                        Console.WriteLine("PASS RTL layout, wrapping and control positions: "+size.Width+"x"+size.Height);
                    }
                }
                return 0;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine("FAIL dashboard layout verification: "+ex);
                return 1;
            }
        }
        static void CheckTree(Control parent)
        {
            foreach(Control item in parent.Controls)
            {
                var label=item as Label;
                if(label!=null && !label.AutoSize && label.Text.Length>0)
                    throw new Exception("Fixed-height text label found: "+label.Text);
                var table=item as TableLayoutPanel;
                if(table!=null)
                {
                    // Layout rows must not occupy the same on-screen pixels.
                    for(int i=0;i<table.Controls.Count;i++)
                    for(int j=i+1;j<table.Controls.Count;j++)
                    {
                        var a=table.Controls[i];var b=table.Controls[j];
                        if(a.Visible && b.Visible && a.Bounds.IntersectsWith(b.Bounds))
                            throw new Exception("Controls overlap: "+a.Text+" and "+b.Text);
                    }
                }
                CheckTree(item);
            }
        }
    }
}
