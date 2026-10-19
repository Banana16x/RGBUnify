using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("RGB Unify")]

namespace RGBUnify
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (Mutex m = new Mutex(true, "RGBUnify_SingleInstance", out created))
            {
                EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "RGBUnify_Show");
                if (!created)
                {
                    // Already running (probably in the tray) - just bring it forward.
                    showSignal.Set();
                    return;
                }
                // Below normal priority: games always win the CPU over lighting.
                try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool fromStartup = Array.IndexOf(args, "--tray") >= 0;
                bool hidden = fromStartup && Settings.Load().StartMinimized;
                MainForm form = new MainForm(hidden);

                Thread listener = new Thread(delegate ()
                {
                    while (showSignal.WaitOne())
                        form.BeginInvoke(new Action(form.ShowMain));
                });
                listener.IsBackground = true;
                listener.Start();

                // Setup signals this to close us cleanly (saving settings, stopping the engine) before updating files.
                EventWaitHandle exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "RGBUnify_Exit");
                Thread exitListener = new Thread(delegate ()
                {
                    if (exitSignal.WaitOne()) form.BeginInvoke(new Action(form.ExitApp));
                });
                exitListener.IsBackground = true;
                exitListener.Start();

                Application.Run(form);
            }
        }
    }
}
