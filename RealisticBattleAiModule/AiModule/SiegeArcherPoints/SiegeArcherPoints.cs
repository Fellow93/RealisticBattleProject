// ScatterAroundExpanded.ScatterAroundExpanded
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

public class SiegeArcherPoints : MissionLogic
{
    public bool firstTime = true;
    public bool xmlExists = false;
    public static bool isEditingXml = true;
    public bool editingWarningDisplayed = false;
    public bool isFirstTimeLoading = true;

    public override void OnMissionTick(float dt)
    {
        //((MissionView)this).OnMissionScreenTick(dt);
        if (isFirstTimeLoading && Mission.Current != null && Mission.Current.IsSiegeBattle)
        {
            XmlDocument xmlDocument = new XmlDocument();
            if (File.Exists(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + ".xml"))
            {
                xmlDocument.Load(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + ".xml");
                xmlExists = true;
            }

            if (xmlExists)
            {
                List<GameEntity> gameEntities = new List<GameEntity>();
                Mission.Current.Scene.GetEntities(ref gameEntities);
                StrategicArea strategicArea;
                List<StrategicArea> _strategicAreas = (from amo in Mission.Current.ActiveMissionObjects
                                                       where (strategicArea = amo as StrategicArea) != null && strategicArea.IsActive && strategicArea.IsUsableBy(BattleSideEnum.Defender)
                                                       && (strategicArea.GameEntity.GetOldPrefabName().Contains("archer_position") || strategicArea.GameEntity.GetOldPrefabName().Contains("strategic_archer_point"))
                                                       select amo as StrategicArea).ToList();
                foreach (StrategicArea _strategicArea in _strategicAreas)
                {
                    Mission.Current.Teams.Defender.TeamAI.RemoveStrategicArea(_strategicArea);
                    _strategicArea.GameEntity.RemoveAllChildren();
                    _strategicArea.GameEntity.Remove(1);
                }
                foreach (GameEntity g2 in gameEntities)
                {
                    if (g2.HasScriptOfType<StrategicArea>() && (!g2.HasTag("PlayerStratPoint") & !g2.HasTag("BeerMarkerPlayer")) && g2.GetOldPrefabName() == "strategic_archer_point")
                    {
                        if (g2.GetFirstScriptOfType<StrategicArea>().IsUsableBy(BattleSideEnum.Defender))
                        {
                            Mission.Current.Teams.Defender.TeamAI.RemoveStrategicArea(g2.GetFirstScriptOfType<StrategicArea>());
                            g2.RemoveAllChildren();
                            g2.Remove(1);
                        }
                    }
                }
                List<GameEntity> ListBase = Mission.Current.Scene.FindEntitiesWithTag("BeerMarkerBase").ToList();
                foreach (GameEntity b in ListBase)
                {
                    b.RemoveAllChildren();
                    b.Remove(1);
                }
                List<GameEntity> ListG = Mission.Current.Scene.FindEntitiesWithTag("PlayerStratPoint").ToList();
                List<GameEntity> ListArrow = Mission.Current.Scene.FindEntitiesWithTag("BeerMarkerPlayer").ToList();
                foreach (GameEntity g in ListG)
                {
                    Mission.Current.Teams.Defender.TeamAI.RemoveStrategicArea(g.GetFirstScriptOfType<StrategicArea>());
                    g.RemoveAllChildren();
                    g.Remove(1);
                }
                foreach (GameEntity h in ListArrow)
                {
                    h.RemoveAllChildren();
                    h.Remove(1);
                }

                foreach (XmlNode pointNode in xmlDocument.SelectSingleNode("/points").ChildNodes)
                {
                    double[] parsed = Array.ConvertAll(pointNode.InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries), Double.Parse);

                    //WorldFrame worldPos = new WorldFrame(new Mat3((float)parsed[0], (float)parsed[1], (float)parsed[2], (float)parsed[3],
                    //	(float)parsed[4], (float)parsed[5], (float)parsed[6], (float)parsed[7], (float)parsed[8]), new WorldPosition(Mission.Current.Scene, new Vec3((float)parsed[9], (float)parsed[10], (float)parsed[11])));

                    MatrixFrame matFrame = new MatrixFrame((float)parsed[0], (float)parsed[1], (float)parsed[2], (float)parsed[3],
                        (float)parsed[4], (float)parsed[5], (float)parsed[6], (float)parsed[7], (float)parsed[8], (float)parsed[9], (float)parsed[10], (float)parsed[11]);

                    GameEntity gameEntity = GameEntity.Instantiate(Mission.Current.Scene, "strategic_archer_point", matFrame);
                    gameEntity.SetMobility(GameEntity.Mobility.dynamic);
                    gameEntity.AddTag("PlayerStratPoint");
                    gameEntity.SetVisibilityExcludeParents(visible: true);
                    strategicArea = gameEntity.GetFirstScriptOfType<StrategicArea>();
                    strategicArea.InitializeAutogenerated(1f, 1, Mission.Current.Teams.Defender.Side);

                    GameEntity BeerMark = GameEntity.Instantiate(Mission.Current.Scene, "arrow_new_icon", matFrame);
                    BeerMark.AddTag("BeerMarkerPlayer");
                    BeerMark.SetVisibilityExcludeParents(visible: false);
                    BeerMark.GetGlobalScale().Normalize();
                    BeerMark.SetMobility(GameEntity.Mobility.dynamic);
                    strategicArea.IsActive = true;
                    Mission.Current.Teams.Defender.TeamAI.AddStrategicArea(strategicArea);
                }
            }

            isFirstTimeLoading = false;
            return;
        }

        if (firstTime && Mission.Current != null && Mission.Current.IsSiegeBattle && Mission.Current.PlayerTeam.IsDefender && Mission.Current.Mode != MissionMode.Deployment)
        {
            if (firstTime && !RBMConfig.RBMConfig.developerMode)
            {
                firstTime = false;
                return;
            }
            InformationManager.DisplayMessage(new InformationMessage("!!! DEVELOPER MODE, NORMAL USER SHOULDN'T SEE THIS MESSAGE"));
            List<GameEntity> gameEntities = new List<GameEntity>();
            Mission.Current.Scene.GetEntities(ref gameEntities);

            XmlDocument xmlDocument = new XmlDocument();
            if (File.Exists(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + ".xml"))
            {
                xmlDocument.Load(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + ".xml");
                xmlExists = true;
            }
            else
            {
                XmlDeclaration xmlDeclaration = xmlDocument.CreateXmlDeclaration("1.0", "UTF-8", null);

                XmlElement root = xmlDocument.DocumentElement;
                xmlDocument.InsertBefore(xmlDeclaration, root);
                xmlDocument.AppendChild(xmlDocument.CreateElement(string.Empty, "points", string.Empty));
            }

            XmlNode pointNode = xmlDocument.SelectSingleNode("/points");
            if (pointNode == null)
            {
                pointNode = xmlDocument.CreateElement(string.Empty, "points", string.Empty);
            }

            pointNode.RemoveAll();

            foreach (GameEntity g in gameEntities)
            {
                if (g.HasScriptOfType<StrategicArea>() && g.GetFirstScriptOfType<StrategicArea>().IsUsableBy(BattleSideEnum.Defender) && g.GetOldPrefabName() == "strategic_archer_point")
                {
                    XmlElement newPointNode = xmlDocument.CreateElement(string.Empty, "point", string.Empty);
                    string stringToBeSaved = "";
                    stringToBeSaved += g.GetGlobalFrame().rotation.s.x + "," + g.GetGlobalFrame().rotation.s.y + "," + g.GetGlobalFrame().rotation.s.z + ",";
                    stringToBeSaved += g.GetGlobalFrame().rotation.f.x + "," + g.GetGlobalFrame().rotation.f.y + "," + g.GetGlobalFrame().rotation.f.z + ",";
                    stringToBeSaved += g.GetGlobalFrame().rotation.u.x + "," + g.GetGlobalFrame().rotation.u.y + "," + g.GetGlobalFrame().rotation.u.z + ",";
                    stringToBeSaved += g.GetGlobalFrame().origin.x + "," + g.GetGlobalFrame().origin.y + "," + g.GetGlobalFrame().origin.z;
                    newPointNode.InnerText = stringToBeSaved;

                    pointNode.AppendChild(newPointNode);
                }
            }
            xmlDocument.Save(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + ".xml");
            firstTime = false;
        }

        //if (Mission.Current.MainAgent != null)
        //{
        //	if (Mission.Current.IsOrderMenuOpen)
        //	{
        //		if (((MissionView)this).Input.IsKeyPressed(InputKey.O) || ((MissionView)this).Input.IsKeyPressed(InputKey.P)
        //		|| ((MissionView)this).Input.IsKeyPressed(InputKey.K) || ((MissionView)this).Input.IsKeyPressed(InputKey.L))
        //		{
        //			GameEntity cursor = Mission.Current.Scene.FindEntityWithTag("cursormain");
        //			Vec2 rotation = cursor.GetGlobalFrame().rotation.f.AsVec2;
        //			Vec3 flag = ((MissionView)this).MissionScreen.GetOrderFlagPosition();

        //			XmlDocument xmlDocument = new XmlDocument();
        //			if (File.Exists(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + "_inf_positions.xml"))
        //			{
        //				xmlDocument.Load(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + "_inf_positions.xml");
        //				xmlExists = true;
        //			}
        //			else
        //			{
        //				XmlDeclaration xmlDeclaration = xmlDocument.CreateXmlDeclaration("1.0", "UTF-8", null);

        //				XmlElement root = xmlDocument.DocumentElement;
        //				xmlDocument.InsertBefore(xmlDeclaration, root);
        //				xmlDocument.AppendChild(xmlDocument.CreateElement(string.Empty, "points", string.Empty));
        //			}

        //			XmlNode pointNode = xmlDocument.SelectSingleNode("/points");
        //			if (pointNode == null)
        //			{
        //				pointNode = xmlDocument.CreateElement(string.Empty, "points", string.Empty);
        //			}

        //			string innerText = flag.x + "," + flag.y + "," + flag.z + "," + rotation.x + "," + rotation.y;

        //			if (((MissionView)this).Input.IsKeyPressed(InputKey.O)){
        //				XmlNode node = xmlDocument.SelectSingleNode("/points/left_wait");
        //				if (node != null)
        //                      {
        //					node.InnerText = innerText;
        //				}
        //				else
        //                      {
        //					node = xmlDocument.CreateElement(string.Empty, "left_wait", string.Empty);
        //					node.InnerText = innerText;
        //					pointNode.AppendChild(node);
        //				}
        //			}
        //			if (((MissionView)this).Input.IsKeyPressed(InputKey.P))
        //			{
        //				XmlNode node = xmlDocument.SelectSingleNode("/points/right_wait");
        //				if (node != null)
        //				{
        //					node.InnerText = innerText;
        //				}
        //				else
        //				{
        //					node = xmlDocument.CreateElement(string.Empty, "right_wait", string.Empty);
        //					node.InnerText = innerText;
        //					pointNode.AppendChild(node);
        //				}
        //			}
        //			if (((MissionView)this).Input.IsKeyPressed(InputKey.K))
        //			{
        //				XmlNode node = xmlDocument.SelectSingleNode("/points/left_ready");
        //				if (node != null)
        //				{
        //					node.InnerText = innerText;
        //				}
        //				else
        //				{
        //					node = xmlDocument.CreateElement(string.Empty, "left_ready", string.Empty);
        //					node.InnerText = innerText;
        //					pointNode.AppendChild(node);
        //				}
        //			}
        //			if (((MissionView)this).Input.IsKeyPressed(InputKey.L))
        //			{
        //				XmlNode node = xmlDocument.SelectSingleNode("/points/right_ready");
        //				if (node != null)
        //				{
        //					node.InnerText = innerText;
        //				}
        //				else
        //				{
        //					node = xmlDocument.CreateElement(string.Empty, "right_ready", string.Empty);
        //					node.InnerText = innerText;
        //					pointNode.AppendChild(node);
        //				}
        //			}
        //			xmlDocument.Save(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + "_inf_positions.xml");
        //		}
        //	}
        //}
    }

    //[HarmonyPatch(typeof(BehaviorDefendCastleKeyPosition))]
    //[HarmonyPatch("ResetOrderPositions")]
    //class BehaviorDefendCastleKeyPositionPatch
    //{
    //	private enum BehaviorState
    //	{
    //		UnSet,
    //		Waiting,
    //		Ready
    //	}

    //	static void Postfix(ref BehaviorDefendCastleKeyPosition __instance, ref WorldPosition ____readyOrderPosition, ref MovementOrder ____waitOrder,
    //		ref MovementOrder ____readyOrder, ref MovementOrder ____currentOrder, ref BehaviorState ____behaviorState, ref FacingOrder ___CurrentFacingOrder,
    //		ref FacingOrder ____readyFacingOrder, ref FacingOrder ____waitFacingOrder, ref TacticalPosition ____tacticalWaitPos)
    //	{
    //		XmlDocument xmlDocument = new XmlDocument();
    //		if (File.Exists(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + "_inf_positions.xml"))
    //		{
    //			WorldPosition tempPos = ____tacticalWaitPos.Position;
    //			xmlDocument.Load(RBMAI.Utilities.GetSiegeArcherPointsPath() + Mission.Current.Scene.GetName() + "_" + Mission.Current.Scene.GetUpgradeLevelMask() + "_inf_positions.xml");
    //			if (__instance.Formation.AI.Side == FormationAI.BehaviorSide.Left)
    //               {
    //				double[] leftWait = Array.ConvertAll(xmlDocument.SelectSingleNode("/points/left_wait").InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries), Double.Parse);
    //				double[] leftReady = Array.ConvertAll(xmlDocument.SelectSingleNode("/points/left_ready").InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries), Double.Parse);
    //				____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(new Vec2((float)leftWait[3], (float)leftWait[4]));
    //				____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(new Vec2((float)leftReady[3], (float)leftReady[4]));
    //				tempPos.SetVec2(new Vec2((float)leftWait[0], (float)leftWait[1]));
    //				____readyOrderPosition.SetVec3(UIntPtr.Zero, new Vec3((float)leftReady[0], (float)leftReady[1], (float)leftReady[2]), false);

    //			}
    //			else if (__instance.Formation.AI.Side == FormationAI.BehaviorSide.Right)
    //			{
    //				double[] rightWait = Array.ConvertAll(xmlDocument.SelectSingleNode("/points/right_wait").InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries), Double.Parse);
    //				double[] rightReady = Array.ConvertAll(xmlDocument.SelectSingleNode("/points/right_ready").InnerText.Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries), Double.Parse);
    //				____waitFacingOrder = FacingOrder.FacingOrderLookAtDirection(new Vec2((float)rightWait[3], (float)rightWait[4]));
    //				____readyFacingOrder = FacingOrder.FacingOrderLookAtDirection(new Vec2((float)rightReady[3], (float)rightReady[4]));
    //				tempPos.SetVec2(new Vec2((float)rightWait[0], (float)rightWait[1]));
    //				____readyOrderPosition.SetVec3(UIntPtr.Zero, new Vec3((float)rightReady[0], (float)rightReady[1], (float)rightReady[2]), false);

    //			}

    //			____waitOrder = MovementOrder.MovementOrderMove(tempPos);
    //			____readyOrder = MovementOrder.MovementOrderMove(____readyOrderPosition);
    //			____currentOrder = ((____behaviorState == BehaviorState.Ready) ? ____readyOrder : ____waitOrder);
    //			___CurrentFacingOrder = ((__instance.Formation.QuerySystem.ClosestEnemyFormation != null && TeamAISiegeComponent.IsFormationInsideCastle(__instance.Formation.QuerySystem.ClosestEnemyFormation.Formation, includeOnlyPositionedUnits: true)) ? FacingOrder.FacingOrderLookAtEnemy : ((____behaviorState == BehaviorState.Ready) ? ____readyFacingOrder : ____waitFacingOrder));

    //		}
    //	}
    //}

    [HarmonyPatch(typeof(ArrangementOrder))]
    [HarmonyPatch("IsStrategicAreaClose")]
    private class IsStrategicAreaClosePatch
    {
        private static bool Prefix(ref StrategicArea strategicArea, ref Formation formation, ref ArrangementOrder __instance, ref bool __result)
        {
            float distanceToCheck = 200f;
            if (strategicArea.IsUsableBy(formation.Team.Side))
            {
                if (strategicArea.IgnoreHeight)
                {
                    if (strategicArea.GameEntity != null && MathF.Abs(strategicArea.GameEntity.GlobalPosition.x - formation.OrderPosition.X) <= distanceToCheck)
                    {
                        __result = MathF.Abs(strategicArea.GameEntity.GlobalPosition.y - formation.OrderPosition.Y) <= distanceToCheck;
                        return false;
                    }
                    __result = false;
                    return false;
                }
                WorldPosition worldPosition = formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None);
                Vec3 targetPoint = strategicArea.GameEntity.GlobalPosition;
                __result = worldPosition.DistanceSquaredWithLimit(in targetPoint, distanceToCheck * distanceToCheck + 1E-05f) < distanceToCheck * distanceToCheck;
                return false;
            }
            __result = false;
            return false;
            //if (formation.Team?.TeamAI == null)
            //{
            //	__result = new List<StrategicArea>();
            //	return false;
            //}
            //__result = formation.Team.TeamAI.GetStrategicAreas().Where(delegate (StrategicArea sa)
            //{
            //	float customDistanceToCheck = 150f;
            //	if (sa != null && formation != null && sa.GameEntity != null && sa.GameEntity.GlobalPosition != null && sa.IsUsableBy(formation.Team.Side))
            //	{
            //		if (sa.IgnoreHeight)
            //		{
            //			if (MathF.Abs(sa.GameEntity.GlobalPosition.x - formation.OrderPosition.X) <= customDistanceToCheck)
            //			{
            //				return MathF.Abs(sa.GameEntity.GlobalPosition.y - formation.OrderPosition.Y) <= customDistanceToCheck;
            //			}
            //			return false;
            //		}
            //		WorldPosition worldPosition = formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None);
            //		Vec3 targetPoint = sa.GameEntity.GlobalPosition;
            //		return worldPosition.DistanceSquaredWithLimit(in targetPoint, customDistanceToCheck * customDistanceToCheck + 1E-05f) < customDistanceToCheck * customDistanceToCheck;
            //	}
            //	return false;
            //});//.OrderByDescending(o =>o.GameEntity.GlobalPosition.z).ToList();
            ////List<StrategicArea> newlist = __result.ToList();
            ////newlist.Randomize();
            ////__result = newlist;
            //return false;
        }
    }
}