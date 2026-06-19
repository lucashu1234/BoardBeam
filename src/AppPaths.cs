using System;
using System.IO;

namespace BoardBeam
{
    internal static class AppPaths
    {
        private static readonly bool _portableInitialized;
        private static readonly string _portableBase;

        static AppPaths()
        {
            // 便携模式：exe 同级存在 BoardBeam.portable 标记文件时，所有数据存到 exe 目录下
            try
            {
                string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                if (exeDir != null && File.Exists(Path.Combine(exeDir, "BoardBeam.portable")))
                {
                    _portableInitialized = true;
                    _portableBase = exeDir;
                }
            }
            catch { }
        }

        public static bool IsPortable { get { return _portableInitialized; } }

        public static string CaptureDirectory
        {
            get
            {
                string dir = _portableInitialized
                    ? Path.Combine(_portableBase, "capture")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BoardBeam");
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
                string dir;
                if (_portableInitialized)
                {
                    dir = Path.Combine(_portableBase, "recording");
                }
                else
                {
                    string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    if (string.IsNullOrEmpty(baseDir))
                        baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    dir = Path.Combine(baseDir, "BoardBeam");
                }
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
                string dir = _portableInitialized
                    ? Path.Combine(_portableBase, "config")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoardBeam");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(ConfigDirectory, "settings.ini"); }
        }

        /// <summary>崩溃日志目录。</summary>
        public static string LogDirectory
        {
            get
            {
                string dir = Path.Combine(ConfigDirectory, "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>贴图组持久化目录。</summary>
        public static string PinGroupsDirectory
        {
            get
            {
                string dir = Path.Combine(ConfigDirectory, "pingroups");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>剪贴板图片历史目录（缩略图）。</summary>
        public static string ClipboardHistoryDirectory
        {
            get
            {
                string dir = Path.Combine(ConfigDirectory, "clip_history");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>标注自动保存目录。</summary>
        public static string AutosaveDirectory
        {
            get
            {
                string dir = Path.Combine(ConfigDirectory, "autosave");
                Directory.CreateDirectory(dir);
                return dir;
            }
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
