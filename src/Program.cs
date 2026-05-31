using System;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "BoardBeam.SingleInstance.2026", out created))
            {
                if (!created)
                {
                    MessageBox.Show("BoardBeam 已经在运行。", "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    NativeMethods.SetProcessDPIAware();
                }
                catch
                {
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PresenterApplicationContext());
            }
        }
    }
}

