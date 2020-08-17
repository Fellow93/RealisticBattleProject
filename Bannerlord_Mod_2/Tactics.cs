using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using static RealisticBattle.Behaviours;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattle
{
    class Tactics
    {
        public class TacticFullScaleAttackMy : TacticComponent
        {
            private bool _hasBattleBeenJoined;

            private int _AIControlledFormationCount;

            protected Formation meleeInfantry;
            protected Formation rangedInfantry;
            protected Formation skirmishersLeft;
            protected Formation skirmishersRight;
            protected Formation cavalryLeft;
            protected Formation cavalryRight;
            protected Formation cavalryRanged;

            protected void ManageFormationCountsMy(int infantryCount, int rangedCount, int cavalryCount, int rangedCavalryCount)
            {
                SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsInfantryFormation, infantryCount);
                SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsRangedFormation, rangedCount);
                SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsCavalryFormation, cavalryCount);
                SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsRangedCavalryFormation, rangedCavalryCount);
            }

            private void CopyOrdersFrom(Formation source, Formation target)
            {
                source.MovementOrder = target.MovementOrder;
                source.FormOrder = target.FormOrder;
                source.SetPositioning(null, null, target.UnitSpacing);
                source.RidingOrder = target.RidingOrder;
                source.WeaponUsageOrder = target.WeaponUsageOrder;
                source.FiringOrder = target.FiringOrder;
                source.IsAIControlled = (target.IsAIControlled || !target.Team.IsPlayerGeneral);
                source.AI.Side = target.AI.Side;
                source.MovementOrder = target.MovementOrder;
                source.FacingOrder = target.FacingOrder;
                source.ArrangementOrder = target.ArrangementOrder;
            }

            internal void TransferUnitsAux(Formation source, Formation target)
            {
                CopyOrdersFrom(target, source);
            }

            protected void AssignTacticFormations1121()
            {
                ManageFormationCountsMy(1, 1, 2, 1);
                meleeInfantry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

                //Formation formation2 = meleeInfantry.Team.GetFormation((FormationClass)5);
                if (meleeInfantry != null)
                {
                    int j = 0;
                    int testcount = 0;
                    ArrayList agentsToTransfer = new ArrayList();
                    for (int i = 0; i < meleeInfantry.CountOfUnits; i++)
                    {
                        Agent agent = meleeInfantry.GetUnitWithIndex(i);

                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown)
                                {
                                    testcount++;
                                    agentsToTransfer.Add(agent);
                                }
                            }
                        }
                    }

                    j = 0;
                    foreach (Agent agent in agentsToTransfer)
                    {
                        if (j % 2 == 0)
                        {
                            agent.Formation = meleeInfantry.Team.GetFormation((FormationClass)5);
                        }
                        else
                        {
                            //agent.Formation = meleeInfantry.Team.GetFormation((FormationClass)6);
                            agent.Formation = meleeInfantry.Team.GetFormation((FormationClass)5);
                        }
                        j++;
                    }

                    skirmishersLeft = meleeInfantry.Team.GetFormation((FormationClass)5);
                    //skirmishersRight = meleeInfantry.Team.GetFormation((FormationClass)6);

                }

                if (meleeInfantry != null)
                {
                    _mainInfantry = meleeInfantry;
                    PropertyInfo property = typeof(FormationAI).GetProperty("IsMainFormation", BindingFlags.NonPublic | BindingFlags.Instance);
                    property.DeclaringType.GetProperty("IsMainFormation");
                    property.SetValue(meleeInfantry.AI, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                rangedInfantry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
                List<Formation> list = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
                if (list.Count > 0)
                {
                    cavalryLeft = list[0];
                    cavalryLeft.AI.Side = FormationAI.BehaviorSide.Left;
                    if (list.Count > 1)
                    {
                        cavalryRight = list[1];
                        cavalryRight.AI.Side = FormationAI.BehaviorSide.Right;
                    }
                    else
                    {
                        cavalryRight = null;
                    }
                }
                else
                {
                    cavalryLeft = null;
                    cavalryRight = null;
                }
                if (skirmishersLeft != null)
                {
                    skirmishersLeft.AI.Side = FormationAI.BehaviorSide.Left;
                }
                if (skirmishersRight != null)
                {
                    skirmishersRight.AI.Side = FormationAI.BehaviorSide.Right;
                }
                cavalryRanged = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
            }

            protected override void ManageFormationCounts()
            {
                AssignTacticFormations1121();
            }

            private void Advance()
            {
                if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
                {
                    SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
                }
                if (meleeInfantry != null)
                {
                    meleeInfantry.AI.ResetBehaviorWeights();
                    //SetDefaultBehaviorWeights(meleeInfantry);
                    meleeInfantry.AI.SetBehaviorWeight<BehaviorCautiousAdvance>(1f);
                }
                if (rangedInfantry != null)
                {
                    rangedInfantry.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(rangedInfantry);
                    rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmishLine>(1f);
                    rangedInfantry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                }
                if (skirmishersLeft != null)
                {
                    skirmishersLeft.AI.ResetBehaviorWeights();
                    //SetDefaultBehaviorWeights(skirmishersLeft);
                    //skirmishersLeft.AI.SetBehaviorWeight<BehaviorSkirmishFlank>(1f).victimFormation = skirmishersLeft.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
                    //skirmishersLeft.AI.GetBehavior<BehaviorSkirmishFlank>().attackingFormation = skirmishersLeft;
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorSkirmishLine>(1f);
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                }
                if (skirmishersRight != null)
                {
                    skirmishersRight.AI.ResetBehaviorWeights();
                   // SetDefaultBehaviorWeights(skirmishersRight);
                    //skirmishersRight.AI.SetBehaviorWeight<BehaviorSkirmishFlank>(1f).victimFormation = skirmishersRight.QuerySystem.ClosestSignificantlyLargeEnemyFormation.Formation;
                    //skirmishersRight.AI.GetBehavior<BehaviorSkirmishFlank>().attackingFormation = skirmishersRight;
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorSkirmishLine>(1f);
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                }
                if (cavalryLeft != null)
                {
                    cavalryLeft.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryLeft);
                    cavalryLeft.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                    cavalryLeft.AI.SetBehaviorWeight<BehaviorCavalryScreen>(1f);
                }
                if (cavalryRight != null)
                {
                    cavalryRight.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryRight);
                    cavalryRight.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                    cavalryRight.AI.SetBehaviorWeight<BehaviorCavalryScreen>(1f);
                }
                if (cavalryRanged != null)
                {
                    cavalryRanged.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryRanged);
                    cavalryRanged.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    cavalryRanged.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
                }
            }

            private void Attack()
            {
                if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
                {
                    SoundTacticalHorn(TacticComponent.AttackHornSoundIndex);
                }
                if (meleeInfantry != null)
                {
                    meleeInfantry.AI.ResetBehaviorWeights();
                    //SetDefaultBehaviorWeights(meleeInfantry);
                    meleeInfantry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //meleeInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
                    //meleeInfantry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
                }
                if (rangedInfantry != null)
                {
                    rangedInfantry.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(rangedInfantry);
                    rangedInfantry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                }
                if (skirmishersLeft != null)
                {
                    skirmishersLeft.AI.ResetBehaviorWeights();
                    //SetDefaultBehaviorWeights(skirmishersLeft);
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    skirmishersLeft.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    //skirmishersLeft.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
                }
                if (skirmishersRight != null)
                {
                    skirmishersRight.AI.ResetBehaviorWeights();
                    //SetDefaultBehaviorWeights(skirmishersRight);
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    skirmishersRight.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    //skirmishersRight.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
                }
                if (cavalryLeft != null)
                {
                    cavalryLeft.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryLeft);
                    cavalryLeft.AI.SetBehaviorWeight<BehaviorFlank>(1f);
                    cavalryLeft.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
                }
                if (cavalryRight != null)
                {
                    cavalryRight.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryRight);
                    cavalryRight.AI.SetBehaviorWeight<BehaviorFlank>(1f);
                    cavalryRight.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
                }
                if (cavalryRanged != null)
                {
                    cavalryRanged.AI.ResetBehaviorWeights();
                    SetDefaultBehaviorWeights(cavalryRanged);
                    cavalryRanged.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    cavalryRanged.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
                }
            }

            public TacticFullScaleAttackMy(Team team)
                : base(team)
            {
                _hasBattleBeenJoined = false;
                _AIControlledFormationCount = base.Formations.Count((Formation f) => f.IsAIControlled);
            }

            private bool HasBattleBeenJoined()
            {
                if (meleeInfantry?.QuerySystem.ClosestEnemyFormation != null && !(meleeInfantry.AI.ActiveBehavior is BehaviorCharge) && !(meleeInfantry.AI.ActiveBehavior is BehaviorTacticalCharge))
                {
                    return meleeInfantry.QuerySystem.MedianPosition.AsVec2.Distance(meleeInfantry.QuerySystem.ClosestEnemyFormation.MedianPosition.AsVec2) / meleeInfantry.QuerySystem.ClosestEnemyFormation.MovementSpeedMaximum <= 15f + (_hasBattleBeenJoined ? 5f : 0f);
                }
                return true;
            }

            protected override bool CheckAndSetAvailableFormationsChanged()
            {
                int num = base.Formations.Count((Formation f) => f.IsAIControlled);
                bool num2 = num != _AIControlledFormationCount;
                if (num2)
                {
                    _AIControlledFormationCount = num;
                }
                if (!num2)
                {
                    if ((meleeInfantry == null || (meleeInfantry.CountOfUnits != 0 && meleeInfantry.QuerySystem.IsInfantryFormation)) && (rangedInfantry == null || (rangedInfantry.CountOfUnits != 0 && rangedInfantry.QuerySystem.IsRangedFormation)) && (cavalryLeft == null || (cavalryLeft.CountOfUnits != 0 && cavalryLeft.QuerySystem.IsCavalryFormation)) && (cavalryRight == null || (cavalryRight.CountOfUnits != 0 && cavalryRight.QuerySystem.IsCavalryFormation)))
                    {
                        if (cavalryRanged != null)
                        {
                            if (cavalryRanged.CountOfUnits != 0)
                            {
                                return !cavalryRanged.QuerySystem.IsRangedCavalryFormation;
                            }
                            return true;
                        }
                        return false;
                    }
                    return true;
                }
                return true;
            }

            protected override void TickOccasionally()
            {
                if (!base.AreFormationsCreated)
                {
                    return;
                }
                if (CheckAndSetAvailableFormationsChanged())
                {
                    ManageFormationCounts();
                    if (_hasBattleBeenJoined)
                    {
                        Attack();
                    }
                    else
                    {
                        Advance();
                    }
                }
                bool flag = HasBattleBeenJoined();
                if (flag != _hasBattleBeenJoined || IsTacticReapplyNeeded)
                {
                    _hasBattleBeenJoined = flag;
                    if (_hasBattleBeenJoined)
                    {
                        Attack();
                    }
                    else
                    {
                        Advance();
                    }
                    IsTacticReapplyNeeded = false;
                }
                base.TickOccasionally();
            }

            internal float GetTacticWeight()
            {
                float num = team.QuerySystem.RangedCavalryRatio * (float)team.QuerySystem.MemberCount;
                return team.QuerySystem.InfantryRatio * (float)team.QuerySystem.MemberCount / ((float)team.QuerySystem.MemberCount - num) * (float)Math.Sqrt(team.QuerySystem.OverallPowerRatio);
            }

            internal static void SetDefaultBehaviorWeights(Formation f)
            {
                f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                f.AI.SetBehaviorWeight<BehaviorPullBack>(1f);
                f.AI.SetBehaviorWeight<BehaviorStop>(1f);
                f.AI.SetBehaviorWeight<BehaviorReserve>(1f);
            }
        }
    }
}
