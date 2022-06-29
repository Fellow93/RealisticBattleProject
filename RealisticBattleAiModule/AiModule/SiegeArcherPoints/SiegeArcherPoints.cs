// ScatterAroundExpanded.ScatterAroundExpanded
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

public class SiegeArcherPoints : MissionView
{
	public static bool firstTime = true;
	public static bool xmlExists = false;
	public static bool isEditingXml = true;

	public override void OnMissionScreenTick(float dt)
    {
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

    //        if (xmlExists)
    //        {
				//foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
				//{
				//	foreach (XmlNode subNode in childNode)
				//	{
				//		XmlConfig.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
				//	}
				//}
				//return;
    //        }

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
