// ScatterAroundExpanded.ScatterAroundExpanded
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

public class SiegeArcherPoints : MissionView
{
	public bool firstTime = true;
	public bool xmlExists = false;
	public bool isEditingXml = true;
	public bool editingWarningDisplayed = false;

	public override void OnMissionScreenTick(float dt)
    {
        if (!editingWarningDisplayed && isEditingXml)
        {
			InformationManager.DisplayMessage(new InformationMessage("YOU ARE IN EDIT MODE, YOU WILL REMOVE ALL ARCHER POINTS FROM THIS SCENE AFTER STARTING BATTLE", Color.FromUint(16711680u)));
			editingWarningDisplayed = true;
        }
		if (firstTime && Mission.Current != null && Mission.Current.IsSiegeBattle && Mission.Current.Mode != MissionMode.Deployment)
		{
			((MissionBehavior)this).AfterStart();
			List<GameEntity> gameEntities = new List<GameEntity>();
			Mission.Current.Scene.GetEntities(ref gameEntities);

			XmlDocument xmlDocument = new XmlDocument();
			if (File.Exists(RealisticBattleAiModule.Utilities.GetSiegeArcherPointsPath()))
			{
				xmlDocument.Load(RealisticBattleAiModule.Utilities.GetSiegeArcherPointsPath());
				xmlExists = true;
			}

            if (xmlExists)
            {
                if (isEditingXml)
                {
					XmlNode nodeToRemove = null;
					foreach (XmlNode sceneXmlNode in xmlDocument.ChildNodes)
					{
                        if (sceneXmlNode.LocalName.Equals(Mission.Current.Scene.GetName() + "," + Mission.Current.Scene.GetUpgradeLevelMask()))
                        {
							nodeToRemove = sceneXmlNode;
							break;
                        }
					}
					xmlDocument.RemoveChild(nodeToRemove);
				}
                else
                {
					foreach (XmlNode sceneXmlNode in xmlDocument.ChildNodes)
					{
						if (sceneXmlNode.LocalName.Equals(Mission.Current.Scene.GetName() + "," + Mission.Current.Scene.GetUpgradeLevelMask()))
						{
							foreach(XmlNode pointNode in sceneXmlNode.ChildNodes)
                            {
								double[] parsed = Array.ConvertAll(pointNode.InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries),Double.Parse);

                            }
						}
					}
				}
                return;
            }

            XmlDeclaration xmlDeclaration = xmlDocument.CreateXmlDeclaration("1.0", "UTF-8", null);

			XmlElement root = xmlDocument.DocumentElement;
			xmlDocument.InsertBefore(xmlDeclaration, root);

			XmlElement sceneNode = xmlDocument.CreateElement(string.Empty, Mission.Current.Scene.GetName() + "," + Mission.Current.Scene.GetUpgradeLevelMask(), string.Empty);

			foreach (GameEntity g in gameEntities)
			{
				if (g.HasScriptOfType<StrategicArea>() && (!g.HasTag("PlayerStratPoint") | !g.HasTag("BeerMarkerPlayer")) && g.GetOldPrefabName() == "strategic_archer_point")
				{
					XmlElement pointNode = xmlDocument.CreateElement(string.Empty, "point", string.Empty);
					string stringToBeSaved = "";
					stringToBeSaved += g.GetGlobalFrame().rotation.s.x + "," + g.GetGlobalFrame().rotation.s.y + "," + g.GetGlobalFrame().rotation.s.z + ",";
					stringToBeSaved += g.GetGlobalFrame().rotation.f.x + "," + g.GetGlobalFrame().rotation.f.y + "," + g.GetGlobalFrame().rotation.f.z + ",";
					stringToBeSaved += g.GetGlobalFrame().rotation.u.x + "," + g.GetGlobalFrame().rotation.u.y + "," + g.GetGlobalFrame().rotation.u.z + ",";
					stringToBeSaved += g.GetGlobalFrame().origin.x + "," + g.GetGlobalFrame().origin.y + "," + g.GetGlobalFrame().origin.z;
					pointNode.InnerText = stringToBeSaved;
					sceneNode.AppendChild(pointNode);
				}
			}
			xmlDocument.AppendChild(sceneNode);
			xmlDocument.Save(RealisticBattleAiModule.Utilities.GetSiegeArcherPointsPath());
			firstTime = false;
		}
	}
}
