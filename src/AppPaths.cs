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

        public static string NewImagePath(string suffix)
        {
            return Path.Combine(CaptureDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + suffix + ".png");
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
    }
}

