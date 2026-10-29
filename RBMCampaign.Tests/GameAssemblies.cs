using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // RBMCampaign references the game with Private=False, so TaleWorlds.* never lands in the test output.
    // Resolve them (and RBM's sibling modules) from the install the build already points at.
    [SetUpFixture]
    public class GameAssemblies
    {
        private static string[] _folders;

        [OneTimeSetUp]
        public void Register()
        {
            _folders = typeof(GameAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "GameBin" || a.Key == "RbmBin")
                .Select(a => a.Value)
                .ToArray();
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        [OneTimeTearDown]
        public void Unregister()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string folder in _folders)
            {
                string path = Path.Combine(folder, file);
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        }
    }
}
