using System;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class Program
    {
        internal static PresenterApplicationContext AppContext;

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

                // DPI 感知由 app.manifest（PerMonitorV2）声明，build 时通过 /win32manifest 嵌入。
                // 不再调用 SetProcessDPIAware()——它会与 manifest 的 PerMonitorV2 冲突并可能降级为系统级感知。
                // PerMonitorV2 自动缩放由 BoardBeam.exe.config 的 ApplicationConfigurationSection 启用。

                // 安装崩溃日志（捕获未处理异常，写入本地日志，便于排障）
                CrashLogger.Install();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppContext = new PresenterApplicationContext();
                Application.Run(AppContext);
            }
        }
    }
}

