using System;

namespace RBMConfig
{
    public static class Utilities
    {
        public static string GetConfigFilePath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "config.xml");
        }

        public static string GetConfigFolderPath()
        {
           return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Mount and Blade II Bannerlord", "Configs", "RBM");
        }
    }
}

