using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>
    /// 本地崩溃日志：捕获未处理异常，写入 ConfigDirectory/logs/crash_&lt;date&gt;.txt。
    /// 纯本地、脱敏（不记录截图内容），用于开源项目排障。
    /// </summary>
    internal static class CrashLogger
    {
        private static int _logged; // 0=未记录, 1=已记录（防多回调重复写）

        public static void Install()
        {
            try
            {
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    Log("UI线程异常", e.Exception);
                    try { System.Windows.Forms.Application.Restart(); } catch { }
                    System.Environment.Exit(1);
                };
            }
            catch { }

            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log("未处理异常", e.ExceptionObject as Exception);
                };
            }
            catch { }
        }

        /// <summary>记录一条非致命异常（供各处 catch 块调用，替代静默 catch{}）。</summary>
        public static void Log(string context, Exception ex)
        {
            if (ex == null) return;
            Log(context, ex.GetType().FullName + ": " + ex.Message + "\r\n" + ex.StackTrace);
        }

        public static void Log(string context, string detail)
        {
            try
            {
                if (Interlocked.Exchange(ref _logged, 1) == 1 && context == "未处理异常") return;
                string dir = AppPaths.LogDirectory;
                string file = Path.Combine(dir, "crash_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("====================================================");
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine("场景: " + context);
                sb.AppendLine("版本: " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                sb.AppendLine("OS:   " + Environment.OSVersion.VersionString + " 64位=" + Environment.Is64BitOperatingSystem);
                sb.AppendLine("----- 详情 -----");
                sb.AppendLine(detail ?? "(无)");
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 日志本身失败不能再抛，必须静默
            }
        }
    }
}
