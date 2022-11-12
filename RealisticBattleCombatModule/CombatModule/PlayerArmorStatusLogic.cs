using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;
using TaleWorlds.Core;

namespace RBMCombat
{
    public class PlayerArmorStatus : MissionLogic
    {
        public int tickTimer = 0;
        public int tickTimerReset = 120;

        public PlayerArmorStatusVM _dataSource;

        private GauntletLayer _gauntletLayer;

        public bool IsEnabled
        {
            get
            {
                bool result = true;
                return result;
            }
        }

        public override void AfterStart()
        {
            MissionScreen missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            _dataSource = new PlayerArmorStatusVM();
            _gauntletLayer = new GauntletLayer(-1, "GauntletLayer");
            missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("PlayerArmorStatus", (ViewModel)_dataSource);
        }

        public string decideIconColor(int num)
        {
            if(num > 100)
            {
                return PlayerArmorStatusVM.green;
            }
            else if(num == 100)
            {
                return PlayerArmorStatusVM.grey;
            }
            else if (num >= 90)
            {
                return PlayerArmorStatusVM.lightOrange;
            }
            else if (num >= 80)
            {
                return PlayerArmorStatusVM.orange;
            }
            else if (num >= 70)
            {
                return PlayerArmorStatusVM.darkOrange;
            }
            else
            {
                return PlayerArmorStatusVM.red;
            }
        }

        public int getModifierPercentage(ItemModifier im)
        {
            if(im != null)
            {
                return im.ModifyArmor(100);
            }
            else
            {
                return 100;
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (tickTimer < tickTimerReset) {
                tickTimer++;
                //if (Mission.Current.MainAgent != null && Mission.Current.MainAgent.IsDoingPassiveAttack)
                //{
                //    MatrixFrame mf = MatrixFrame.CreateLookAt(Mission.Current.MainAgent.LookFrame.origin, new Vec3(Mission.Current.MainAgent.Formation.Direction.x, Mission.Current.MainAgent.Formation.Direction.y, 1), new Vec3(20,1,1));
                //    MatrixFrame mf2 = MatrixFrame.CreateLookAt(Mission.Current.MainAgent.LookFrame.origin, new Vec3(Mission.Current.MainAgent.Formation.Direction.x, Mission.Current.MainAgent.Formation.Direction.y, 1), new Vec3(20, 1, 1));
                //    //mf2.Rotate(1f, new Vec3(0f, 1f, 1f));

                //    Mission.Current.MainAgent.SetHandInverseKinematicsFrame(ref mf, ref mf2);
                //}
            }
            else
            {
                tickTimer = 0;
                if (Mission.Current != null && Mission.Current.MainAgent != null)
                {
                    Agent playerAgent = Mission.Current.MainAgent;
                    int helmet = 0;
                    int shoulders = 0;
                    int body = 0;
                    int gloves = 0;
                    int legs = 0;
                    int harness = 0;

                    for (int i = (int)EquipmentIndex.ArmorItemBeginSlot; i < (int)EquipmentIndex.ArmorItemEndSlot; i++)
                    {
                        if (playerAgent.SpawnEquipment[(EquipmentIndex)i].Item != null && playerAgent.SpawnEquipment[(EquipmentIndex)i].Item.ArmorComponent != null)
                        {
                            switch (playerAgent.SpawnEquipment[(EquipmentIndex)i].Item.ItemType)
                            {
                                case ItemObject.ItemTypeEnum.HeadArmor:
                                    {
                                        helmet = getModifierPercentage(playerAgent.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
                                        break;
                                    }
                                case ItemObject.ItemTypeEnum.Cape:
                                    {
                                        shoulders = getModifierPercentage(playerAgent.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
                                        break;
                                    }
                                case ItemObject.ItemTypeEnum.ChestArmor:
                                case ItemObject.ItemTypeEnum.BodyArmor:
                                    {
                                        body = getModifierPercentage(playerAgent.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
                                        break;
                                    }
                                case ItemObject.ItemTypeEnum.HandArmor:
                                    {
                                        gloves = getModifierPercentage(playerAgent.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
                                        break;
                                    }
                                case ItemObject.ItemTypeEnum.LegArmor:
                                    {
                                        legs = getModifierPercentage(playerAgent.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
                                        break;
                                    }
                            }
                        }
                    }

                    if (playerAgent.HasMount && playerAgent.MountAgent != null && playerAgent.MountAgent.SpawnEquipment[EquipmentIndex.HorseHarness].Item != null)
                    {
                        harness = getModifierPercentage(playerAgent.MountAgent.SpawnEquipment[EquipmentIndex.HorseHarness].ItemModifier);
                    }

                    _dataSource.Helmet = decideIconColor(helmet);
                    _dataSource.Shoulders = decideIconColor(shoulders);
                    _dataSource.Body = decideIconColor(body);
                    _dataSource.Gloves = decideIconColor(gloves);
                    _dataSource.Legs = decideIconColor(legs);
                    _dataSource.Harness = decideIconColor(harness);
                }
            }
            
        }

        //public override void OnRegisterBlow(Agent attacker, Agent victim, GameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        //{
        //    bool isPlayerHit = victim != null && victim.IsPlayerControlled;
        //    bool isPlayerMountHit = !victim.IsHuman && victim.RiderAgent != null && victim.RiderAgent.IsPlayerControlled;
        //    if(isPlayerHit || isPlayerMountHit)
        //    {
        //        if (isPlayerHit && victim.SpawnEquipment != null)
        //        {
        //            int helmet = 0;
        //            int shoulders = 0;
        //            int body = 0;
        //            int gloves = 0;
        //            int legs = 0;

        //            for (int i = (int)EquipmentIndex.ArmorItemBeginSlot; i < (int)EquipmentIndex.ArmorItemEndSlot; i++)
        //            {
        //                if (victim.SpawnEquipment[(EquipmentIndex)i].Item != null && victim.SpawnEquipment[(EquipmentIndex)i].Item.ArmorComponent != null)
        //                {
        //                    switch (victim.SpawnEquipment[(EquipmentIndex)i].Item.ItemType)
        //                    {
        //                        case ItemObject.ItemTypeEnum.HeadArmor:
        //                            {
        //                                helmet = getModifierPercentage(victim.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
        //                                break;
        //                            }
        //                        case ItemObject.ItemTypeEnum.Cape:
        //                            {
        //                                shoulders = getModifierPercentage(victim.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
        //                                break;
        //                            }
        //                        case ItemObject.ItemTypeEnum.ChestArmor:
        //                        case ItemObject.ItemTypeEnum.BodyArmor:
        //                            {
        //                                body = getModifierPercentage(victim.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
        //                                break;
        //                            }
        //                        case ItemObject.ItemTypeEnum.HandArmor:
        //                            {
        //                                gloves = getModifierPercentage(victim.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
        //                                break;
        //                            }
        //                        case ItemObject.ItemTypeEnum.LegArmor:
        //                            {
        //                                legs = getModifierPercentage(victim.SpawnEquipment[(EquipmentIndex)i].ItemModifier);
        //                                break;
        //                            }
        //                    }
        //                }
        //            }

        //            _dataSource.Helmet = "Helmet: " + helmet + "%";
        //            _dataSource.Shoulders = "Shoulders: " + shoulders + "%";
        //            _dataSource.Body = "Body: " + body + "%";
        //            _dataSource.Gloves = "Gloves: " + gloves + "%";
        //            _dataSource.Legs = "Legs: " + legs + "%";
        //        }else if (isPlayerMountHit)
        //        {
        //            int harness = 0;
        //            if(victim.SpawnEquipment != null && victim.SpawnEquipment[EquipmentIndex.HorseHarness].Item != null)
        //            {
        //                harness = getModifierPercentage(victim.SpawnEquipment[EquipmentIndex.HorseHarness].ItemModifier);
        //            }
        //            _dataSource.Harness = "Harness: " + harness + "%";
        //        }
                
        //    }
            //private string helmet = "Helmet: 100%";
            //private string shoulders = "Shoulders: 100%";
            //private string body = "Body: 100%";
            //private string gloves = "Gloves: 100%";
            //private string legs = "Legs: 100%";
    
        //}
    }
}