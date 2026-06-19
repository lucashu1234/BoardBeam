using System;
using System.IO;

namespace BoardBeam
{
    internal static class AppPaths
    {
        public static string CaptureDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BoardBeam");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string NewImagePath(string suffix, string extension = ".png")
        {
            return Path.Combine(CaptureDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + suffix + extension);
        }

        public static string RecordingDirectory
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }

                string dir = Path.Combine(baseDir, "BoardBeam");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string NewRecordingPath(string suffix)
        {
            return Path.Combine(RecordingDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + suffix + ".gif");
        }

        public static string ConfigDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoardBeam");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(ConfigDirectory, "settings.ini"); }
        }

        /// <summary>开机自启快捷方式路径（Startup 文件夹）。</summary>
        public static string AutostartShortcutPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "BoardBeam.lnk");
            }
        }
    }

    /// <summary>管理开机自启快捷方式的创建/删除。</summary>
    internal static class AutostartHelper
    {
        public static bool IsEnabled()
        {
            return File.Exists(AppPaths.AutostartShortcutPath);
        }

        public static void SetEnabled(bool enabled)
        {
            string path = AppPaths.AutostartShortcutPath;
            if (enabled)
            {
                if (File.Exists(path)) return;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null) return;
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic sc = shell.CreateShortcut(path);
                    sc.TargetPath = System.Windows.Forms.Application.ExecutablePath;
                    sc.WorkingDirectory = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                    sc.Description = "BoardBeam";
                    sc.Save();
                }
                catch
                {
                    // 用户环境无 WScript.Shell 或权限不足，静默失败
                }
            }
            else
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}

