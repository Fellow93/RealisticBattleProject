using System;
using System.IO;

namespace RBMConfig
{
    public static class SelectiveDebug
    {
        private static readonly object LockObject = new object();

        public static void Log(string category, string message)
        {
            if (!RBMConfig.developerMode)
            {
                return;
            }

            try
            {
                string folder = Utilities.GetConfigFolderPath();
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "rbm_compat_debug.log");
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + category + "] " + message + Environment.NewLine;
                lock (LockObject)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
            }
        }
    }
}
