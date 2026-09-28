using System;
using System.IO;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace RBM
{
    /// <summary>
    /// Gates RBM_spear_combat_parameters.xml (per-animation combat parameters) behind the RBM Combat toggle.
    ///
    /// The engine reads animation_combat_parameters files itself, once at startup, straight from each module's
    /// ModuleData/project.mbproj: no managed merge is involved and there is no runtime API to override them
    /// afterwards. What we do have is timing: the engine reads the mbproj files after every submodule's
    /// OnSubModuleLoad has returned (rgl_log: "reading animation_combat_parameters xml files" comes ~2s after
    /// RBM's OnSubModuleLoad lines). So OnSubModuleLoad rewrites RBM's own project.mbproj to list the file only
    /// while Combat is enabled. A toggle change in the config screen therefore takes effect on the next launch.
    ///
    /// The native_parameters entry is left in place; NativeParameterGate reverts those at runtime instead.
    /// </summary>
    internal static class CombatParameterGate
    {
        private const string FileId = "soln_combat_system";
        private const string FileName = "ModuleData/RBM_spear_combat_parameters.xml";
        private const string FileType = "animation_combat_parameters";

        public static void Apply()
        {
            try
            {
                string mbprojPath = Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModuleId), "ModuleData", "project.mbproj");
                if (!File.Exists(mbprojPath))
                {
                    TaleWorlds.Library.Debug.Print($"[RBM] Combat parameters: {mbprojPath} not found, spear combat parameters not gated");
                    return;
                }

                XmlDocument mbproj = new XmlDocument { PreserveWhitespace = true };
                mbproj.Load(mbprojPath);
                XmlNode root = mbproj.SelectSingleNode("/base");
                if (root == null)
                {
                    return;
                }
                XmlNode entry = root.SelectSingleNode($"file[@name='{FileName}']");
                bool combatEnabled = RBMConfig.RBMConfig.rbmCombatEnabled;

                if (combatEnabled && entry == null)
                {
                    XmlElement file = mbproj.CreateElement("file");
                    file.SetAttribute("id", FileId);
                    file.SetAttribute("name", FileName);
                    file.SetAttribute("type", FileType);
                    root.AppendChild(mbproj.CreateWhitespace("  "));
                    root.AppendChild(file);
                    root.AppendChild(mbproj.CreateWhitespace(Environment.NewLine));
                }
                else if (!combatEnabled && entry != null)
                {
                    // Drop the indentation before the entry too, so repeated toggles don't pile up blank lines.
                    if (entry.PreviousSibling is XmlWhitespace indent)
                    {
                        root.RemoveChild(indent);
                    }
                    root.RemoveChild(entry);
                }
                else
                {
                    RBMConfig.RBMConfig.rbmCombatEnabledAtLaunch = combatEnabled;
                    return;
                }

                mbproj.Save(mbprojPath);
                RBMConfig.RBMConfig.rbmCombatEnabledAtLaunch = combatEnabled;
                TaleWorlds.Library.Debug.Print(combatEnabled
                    ? "[RBM] Combat parameters: RBM Combat enabled, listed RBM_spear_combat_parameters.xml in project.mbproj"
                    : "[RBM] Combat parameters: RBM Combat disabled, removed RBM_spear_combat_parameters.xml from project.mbproj");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[RBM] Combat parameters: error gating spear combat parameters: {ex}");
            }
        }
    }
}
