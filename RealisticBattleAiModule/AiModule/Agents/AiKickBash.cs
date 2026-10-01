using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// AI foot soldiers kick, shield bash and weapon bash. The native AI never does: the AiKick driven property has
        /// no effect in game. The engine's Kick input is contextual (a kick, or a bash while the agent blocks), so the
        /// input flag is injected into the AI's per-tick input (OnAIInputSet): a kick drops the agent's own block, a
        /// bash holds a block first. Until the action has played out the AI's own attack/event input is suppressed, or
        /// it cancels the move before the hit frame. Gated by RBMConfig.aiKickBashEnabled (off = vanilla). "Kick"
        /// below means kick, shield bash or weapon bash; the values are the constants of AiKickBash.
        ///
        /// Skill: every roll, lead and cost uses one blended skill, mostly Athletics with a smaller share of the
        /// skill with the weapon in hand (RBMConfig.SkillDamage.GetKickBashSkill). Chances are relative: they scale
        /// with the attacker's share of the two fighters' skills (RelativeSkill).
        ///
        /// Attempt (AI only, AiKickBashComponent): both men on foot, target close and in front, per-agent cooldown
        /// up, cost affordable, then a relative-skill roll; more likely from behind and against a staggered target.
        /// It needs an opening: the target is blocking, holding his weapon ready, showing his back or staggered.
        /// Without one the chance is scaled by the attacker's skill lead (SkillLeadFactor), so only a better fighter
        /// tries it.
        ///
        /// Kick or bash: a raised shield stops a bash but not a kick, so a man with his shield raised and facing the
        /// attacker is always kicked; otherwise either.
        ///
        /// Cost (player too): posture and stamina charged once as the action starts, hit or miss, less with skill.
        ///
        /// Damage (RBMCombat, DamageRework.Core): the unarmed (punch) model with the boot, shield or weapon in place
        /// of the gauntlet, so blunt, skill-scaled, by body part and against that part's armour. None for a bash
        /// blocked by a shield or a kick into a raised shield. The victim's posture and stamina loss is the posture
        /// patch's usual one for the blow, i.e. by its damage.
        ///
        /// Knockdown (player too, AiKickBashKnockDownPatch), for a blow not blocked by a shield on a man on foot:
        /// certain if he is staggered (posture break, posture tiredness, or a kick within KickStaggerSeconds) or if
        /// the blow leaves his posture empty; otherwise a relative-skill roll, at full chance against a man holding
        /// his weapon ready or hit from behind and scaled by the skill lead against anything else. The roll is
        /// higher for a kick than a bash, from behind and against a tired man, lower the heavier his armour.
        ///
        /// Blows that land show up as "kick"/"bash" in the battle hit log; in developer mode the outcome of AI
        /// attempts (which action the engine started) is counted and shown in the battle stats overlay.
        /// </summary>
        public static class AiKickBash
        {
            // Share of attempts that hold the block before the Kick input (a bash); the rest drop the block (a kick).
            public static float BashChance = 0.5f;

            // A kick/bash reaches about a pace; from further out it plays but hits air.
            public const float TriggerDistance = 1.4f;
            // The target has to be in front (cosine of the angle between facing and the direction to him).
            public const float FacingCosine = 0.85f;
            // A man with his shield raised, facing the attacker closer than this (cosine; 0.5 = within 60 degrees), is
            // kicked, never bashed.
            public const float ShieldFacingCosine = 0.5f;
            public const float BlockSeconds = 0.4f;
            public const float KickSeconds = 0.15f;
            // Longest the AI's own input is held off waiting for the action to start and play out.
            public const float WatchSeconds = 1.5f;
            // Chance to take an opening once the cooldown is up, against an equally skilled target. Relative like the
            // knockdown chance: scales with the attacker's share of the two skills, up to twice this, never below
            // MinChance.
            public const float AttemptEvenChance = 0.75f;
            public const float MinChance = 0.05f;
            // Attempt chance multiplier when the attacker is behind his target (see BehindCosine).
            public const float AttemptBehindMultiplier = 1.5f;
            // Attempt chance multiplier against a staggered target (see IsStaggered).
            public const float AttemptStaggeredMultiplier = 2f;
            // Chance a kick/bash that lands knocks its man down when both are equally skilled. The chance is relative:
            // it scales with the attacker's share of the two skills, from 0 (hopelessly outclassed) to twice this
            // (victim has no skill). Skills below MinSkill count as MinSkill.
            public static float KnockDownEvenChance = 0.25f;
            public const int MinSkill = 10;
            // Knockdown chance multiplier against a victim with no stamina left; 1 at full stamina, linear between.
            public static float KnockDownExhaustedMultiplier = 1.3f;
            // Knockdown chance multiplier for a kick (as opposed to a shield/weapon bash).
            public static float KnockDownKickMultiplier = 1.25f;
            // Knockdown chance multiplier when the blow comes from behind the victim: the attacker is further round
            // than BehindCosine from the victim's facing (-0.3 = more than about 107 degrees off).
            public static float KnockDownBehindMultiplier = 1.25f;
            public const float BehindCosine = -0.3f;
            // Knockdown chance multiplier against a victim whose armour weighs HeavyArmorWeight or more; 1 at
            // LightArmorWeight or less, linear between.
            public static float KnockDownHeavyArmorMultiplier = 0.5f;
            public const float LightArmorWeight = 5f;
            public const float HeavyArmorWeight = 40f;
            // Without an opening (see the attempt and knockdown rules) the chance is scaled by the attacker's skill
            // lead: nothing with no lead, the full chance at a lead of FullSkillLead, linear between.
            public static int FullSkillLead = 100;
            // Cost of throwing a kick/bash, when the posture (and stamina) system is on; reduced by skill.
            public const float PostureCost = 20f;
            public const float StaminaCost = 50f;
            // Share of max posture given back after a kick/bash empties it (the posture system's own reset share).
            public const float PostureBreakReset = 0.75f;
            // How long a man counts as staggered after a kick/bash lands on him: a second one in that time knocks him down.
            public const float KickStaggerSeconds = 1.2f;
            public const float CooldownMin = 4f;
            public const float CooldownMax = 8f;
            // Re-check delay after a look that found no opening. Without it every man re-checked every frame once his
            // cooldown was up (engine calls per man per frame); jittered so the army does not check in one frame.
            public const float RetryMin = 0.25f;
            public const float RetryMax = 0.5f;

            private static int _attempts;
            private static int _kicks;
            private static int _bashes;
            private static int _shieldBashes;
            private static int _nothing;

            // The skill every roll, skill lead and cost reduction here uses: mostly Athletics (a kick or a shove is
            // footwork and balance), with a smaller share of the skill with the weapon in hand. Athletics alone when
            // unarmed. Shared with the damage of the blow (RBMCombat), so it lives in RBMConfig.
            public static int KickSkill(Agent agent)
            {
                return RBMConfig.SkillDamage.GetKickBashSkill(agent);
            }

            // 0 with no skill lead over the victim, 1 at FullSkillLead or more.
            public static float SkillLeadFactor(Agent attacker, Agent victim)
            {
                return MathF.Clamp((KickSkill(attacker) - KickSkill(victim)) / (float)FullSkillLead, 0f, 1f);
            }

            // The posture system's forced reactions (StanceLogic.forceStaggerAnimation / forceTiredAnimation and the
            // horse-charge stagger), which it plays on the lower body of a man on foot.
            private static readonly ActionIndexCache[] StaggerActions =
            {
                ActionIndexCache.act_stagger_forward, ActionIndexCache.act_stagger_backward,
                ActionIndexCache.act_stagger_right, ActionIndexCache.act_stagger_left,
                ActionIndexCache.act_stagger_backward_3,
                StanceLogic.TiredAnimation
            };

            // Reeling from a posture break or posture tiredness (the forced reaction is still playing), or from a
            // kick/bash that landed within the last KickStaggerSeconds.
            public static bool IsStaggered(Agent victim)
            {
                ActionIndexCache action = victim.GetCurrentAction(0);
                for (int i = 0; i < StaggerActions.Length; i++)
                {
                    if (action == StaggerActions[i])
                    {
                        return true;
                    }
                }
                AiKickBashComponent component = victim.GetComponent<AiKickBashComponent>();
                return component != null && victim.Mission.CurrentTime - component.LastKickedTime < KickStaggerSeconds;
            }

            // The attacker stands behind the victim: further round than BehindCosine from the victim's facing.
            public static bool IsBehind(Agent attacker, Agent victim)
            {
                Vec2 toAttacker = attacker.Position.AsVec2 - victim.Position.AsVec2;
                toAttacker.Normalize();
                return victim.GetMovementDirection().DotProduct(toAttacker) < BehindCosine;
            }

            // The attacker's share of the two skills, doubled: 1 for equals, towards 2 against no skill, towards 0 when
            // hopelessly outclassed.
            public static float RelativeSkill(Agent attacker, Agent victim)
            {
                float attackerSkill = MathF.Max(MinSkill, KickSkill(attacker));
                float victimSkill = MathF.Max(MinSkill, KickSkill(victim));
                return 2f * attackerSkill / (attackerSkill + victimSkill);
            }

            public static void Report(Agent.ActionCodeType result, bool hadShield)
            {
                _attempts++;
                if (result == Agent.ActionCodeType.WeaponBash)
                {
                    _bashes++;
                    if (hadShield)
                    {
                        _shieldBashes++;
                    }
                }
                else if (result >= Agent.ActionCodeType.KickAllBegin && result < Agent.ActionCodeType.KickAllEnd)
                {
                    _kicks++;
                }
                else
                {
                    _nothing++;
                }
            }

            // The counts of this battle, for the developer stats overlay (BattleStatsLogic).
            public static string CountersText()
            {
                return _attempts + " att, " + _bashes + " bash (" + _shieldBashes + " shield), " + _kicks + " kick, " + _nothing + " none";
            }

            public static void ResetCounters()
            {
                _attempts = 0;
                _kicks = 0;
                _bashes = 0;
                _shieldBashes = 0;
                _nothing = 0;
            }
        }

        /// <summary>
        /// OnAIInputSet runs on the engine's AI thread, so it only sets flags from a field the main thread (OnTick)
        /// keeps up to date; no engine queries there.
        /// </summary>
        public class AiKickBashComponent : AgentComponent
        {
            private const int PhaseIdle = 0;
            private const int PhaseBlock = 1;
            private const int PhaseKick = 2;
            private const int PhaseCommit = 3;

            // Written on the main thread, read on the AI thread.
            private volatile int _phase;
            private volatile bool _bash;

            private float _nextAttemptTime;
            private float _phaseEndTime;
            private float _watchEndTime;
            private bool _watching;
            private bool _wasInAlternativeAttack;

            // Mission time the last kick/bash landed on this man (main thread only).
            public float LastKickedTime = float.MinValue;
            private bool _hadShield;
            private Agent.ActionCodeType _seen = Agent.ActionCodeType.Other;

            public AiKickBashComponent(Agent agent) : base(agent)
            {
                _nextAttemptTime = agent.Mission.CurrentTime + MBRandom.RandomFloatRanged(AiKickBash.CooldownMin, AiKickBash.CooldownMax);
            }

            public override void OnAIInputSet(ref Agent.EventControlFlag eventFlag, ref Agent.MovementControlFlag movementFlag, ref Vec2 inputVector)
            {
                int phase = _phase;
                if (phase == PhaseIdle)
                {
                    return;
                }
                // The native AI keeps issuing its own input while the kick/bash plays, and an attack, a weapon switch
                // or a step away cancels it before the hit frame. Until the action has played out the AI's attack and
                // event input is dropped and the man stands his ground.
                // The Kick input is contextual: a bash while blocking, a kick otherwise. So a bash holds the block and a
                // kick drops the AI's own block.
                eventFlag = Agent.EventControlFlag.None;
                movementFlag &= ~(Agent.MovementControlFlag.AttackMask | Agent.MovementControlFlag.DefendMask);
                if (_bash)
                {
                    // A direction, as the player's block input gives: DefendBlock alone does not raise the guard.
                    movementFlag |= Agent.MovementControlFlag.DefendUp;
                }
                if (phase == PhaseKick)
                {
                    eventFlag = Agent.EventControlFlag.Kick;
                }
                else if (phase == PhaseCommit)
                {
                    inputVector = Vec2.Zero;
                }
            }

            public override void OnTick(float dt)
            {
                if (!RBMConfig.RBMConfig.aiKickBashEnabled)
                {
                    EndAttempt();
                    return;
                }
                bool aiControlled = Agent.IsAIControlled;
                // The effort of the kick/bash itself, charged once as the action starts, hit or miss (a blow that lands
                // costs its usual posture/stamina on top, through the posture patch). The player pays it too. The native
                // AI never kicks, so an AI man only does in an attempt of ours: his action (two engine calls) is only
                // read while one is watched.
                Agent.ActionCodeType action = !aiControlled || _watching ? CurrentAlternativeAttack() : Agent.ActionCodeType.Other;
                bool inAlternativeAttack = action != Agent.ActionCodeType.Other;
                if (inAlternativeAttack && !_wasInAlternativeAttack)
                {
                    ChargeCost();
                }
                _wasInAlternativeAttack = inAlternativeAttack;

                if (!aiControlled)
                {
                    EndAttempt();
                    return;
                }
                float now = Agent.Mission.CurrentTime;

                if (_watching)
                {
                    bool playing = action != Agent.ActionCodeType.Other;
                    if (playing)
                    {
                        _seen = action;
                    }
                    if (_phase == PhaseBlock && now >= _phaseEndTime)
                    {
                        _phase = PhaseKick;
                        _phaseEndTime = now + AiKickBash.KickSeconds;
                    }
                    else if (_phase == PhaseKick && now >= _phaseEndTime)
                    {
                        _phase = PhaseCommit;
                    }
                    // Done once the action that started has played out, or nothing started within the watch time.
                    bool finished = _phase == PhaseCommit && _seen != Agent.ActionCodeType.Other && !playing;
                    if (finished || now >= _watchEndTime)
                    {
                        EndAttempt();
                        AiKickBash.Report(_seen, _hadShield);
                    }
                    return;
                }

                if (now < _nextAttemptTime)
                {
                    return;
                }
                if (!CanAttempt() || !CanAfford())
                {
                    _nextAttemptTime = now + MBRandom.RandomFloatRanged(AiKickBash.RetryMin, AiKickBash.RetryMax);
                    return;
                }
                _nextAttemptTime = now + MBRandom.RandomFloatRanged(AiKickBash.CooldownMin, AiKickBash.CooldownMax);
                // A better fighter takes more of his openings.
                if (MBRandom.RandomFloat >= AttemptChance())
                {
                    return;
                }
                _seen = Agent.ActionCodeType.Other;
                MissionWeapon offhand = Agent.WieldedOffhandWeapon;
                _hadShield = !offhand.IsEmpty && offhand.CurrentUsageItem != null && offhand.CurrentUsageItem.IsShield;
                _watching = true;
                bool bash = !HoldsPolearmTwoHanded() && BashCanLand() && MBRandom.RandomFloat < AiKickBash.BashChance;
                _bash = bash;
                float lead = bash ? AiKickBash.BlockSeconds : 0f;
                _watchEndTime = now + lead + AiKickBash.KickSeconds + AiKickBash.WatchSeconds;
                if (bash)
                {
                    _phaseEndTime = now + AiKickBash.BlockSeconds;
                    _phase = PhaseBlock;
                }
                else
                {
                    _phaseEndTime = now + AiKickBash.KickSeconds;
                    _phase = PhaseKick;
                }
            }

            private void EndAttempt()
            {
                _phase = PhaseIdle;
                _watching = false;
            }

            private float AttemptChance()
            {
                Agent target = Agent.GetTargetAgent();
                if (target == null)
                {
                    return 0f;
                }
                float chance = AiKickBash.AttemptEvenChance * AiKickBash.RelativeSkill(Agent, target);
                // A man's back is an invitation.
                if (AiKickBash.IsBehind(Agent, target))
                {
                    chance *= AiKickBash.AttemptBehindMultiplier;
                }
                // A staggered man goes down to a kick/bash, so he is the one to go for.
                if (AiKickBash.IsStaggered(target))
                {
                    chance *= AiKickBash.AttemptStaggeredMultiplier;
                }
                return MathF.Clamp(chance, AiKickBash.MinChance, 1f) * OpeningFactor(target);
            }

            // 1 against an opening: a man who is blocking (shield or weapon), holding his weapon ready to strike,
            // showing his back, or staggered. Against anything else only a better fighter tries it, the more readily the bigger his
            // skill lead.
            private float OpeningFactor(Agent target)
            {
                Agent.ActionCodeType targetAction = target.GetCurrentActionType(1);
                bool blocking = targetAction >= Agent.ActionCodeType.DefendAllBegin && targetAction < Agent.ActionCodeType.DefendAllEnd;
                if (blocking || targetAction == Agent.ActionCodeType.ReadyMelee || AiKickBash.IsBehind(Agent, target) || AiKickBash.IsStaggered(target))
                {
                    return 1f;
                }
                return AiKickBash.SkillLeadFactor(Agent, target);
            }

            // 100 skill = 10% cheaper, as for the other attacker costs of the posture system.
            private float CostSkillModifier()
            {
                return MathF.Max(0f, 1f - AiKickBash.KickSkill(Agent) * 0.001f);
            }

            private void ChargeCost()
            {
                if (!RBMConfig.RBMConfig.postureEnabled || !AgentStances.values.TryGetValue(Agent, out Stance stance) || stance == null)
                {
                    return;
                }
                float skillModifier = CostSkillModifier();
                stance.reducePosture(AiKickBash.PostureCost * skillModifier);
                if (RBMConfig.RBMConfig.staminaEnabled)
                {
                    stance.reduceStamina(AiKickBash.StaminaCost * skillModifier);
                }
            }

            // The AI does not start one it cannot pay for.
            private bool CanAfford()
            {
                if (!RBMConfig.RBMConfig.postureEnabled || !AgentStances.values.TryGetValue(Agent, out Stance stance) || stance == null)
                {
                    return true;
                }
                float skillModifier = CostSkillModifier();
                return stance.posture > AiKickBash.PostureCost * skillModifier &&
                    (!RBMConfig.RBMConfig.staminaEnabled || stance.stamina > AiKickBash.StaminaCost * skillModifier);
            }

            private bool CanAttempt()
            {
                if (Agent.MountAgent != null || !Agent.IsOnLand() || Agent.IsUsingGameObject || (Agent.GetAgentFlags() & AgentFlag.CanAttack) == 0)
                {
                    return false;
                }
                // With a polearm in two hands he only ever kicks (HoldsPolearmTwoHanded), and the kick has to stay a
                // kick: the Kick input turns into the suspect polearm bash if he is still in a guard, so not while he
                // is defending. Nor while the polearm is in a passive usage (bracing): that usage set has no guard and
                // no kick usage at all, the likeliest cause of the crash other mods saw.
                WeaponComponentData weapon = Agent.WieldedWeapon.IsEmpty ? null : Agent.WieldedWeapon.CurrentUsageItem;
                MissionWeapon offhand = Agent.WieldedOffhandWeapon;
                bool hasShield = !offhand.IsEmpty && offhand.CurrentUsageItem != null && offhand.CurrentUsageItem.IsShield;
                if (weapon != null && (weapon.WeaponClass == WeaponClass.TwoHandedPolearm || (weapon.IsPolearm && !hasShield)))
                {
                    Agent.ActionCodeType ownAction = Agent.GetCurrentActionType(1);
                    if ((ownAction >= Agent.ActionCodeType.DefendAllBegin && ownAction < Agent.ActionCodeType.DefendAllEnd) ||
                        Agent.IsDoingPassiveAttack || MBItem.GetItemIsPassiveUsage(weapon.ItemUsage))
                    {
                        return false;
                    }
                }
                Agent target = Agent.GetTargetAgent();
                if (target == null || !target.IsActive() || target.MountAgent != null)
                {
                    return false;
                }
                Vec2 toTarget = target.Position.AsVec2 - Agent.Position.AsVec2;
                float distance = toTarget.Normalize();
                if (distance > AiKickBash.TriggerDistance || Agent.GetMovementDirection().DotProduct(toTarget) < AiKickBash.FacingCosine)
                {
                    return false;
                }
                // No opening and no skill lead: not an attempt at all, so the cooldown is not spent on it.
                return OpeningFactor(target) > 0f;
            }

            // A raised shield stops a shield/weapon bash (only a kick gets past it), so a bash is for a man without a
            // shield, one whose shield is down, or one who is turned away from the attacker.
            private bool BashCanLand()
            {
                Agent target = Agent.GetTargetAgent();
                if (target == null)
                {
                    return false;
                }
                MissionWeapon offhand = target.WieldedOffhandWeapon;
                bool hasShield = !offhand.IsEmpty && offhand.CurrentUsageItem != null && offhand.CurrentUsageItem.IsShield;
                if (!hasShield || target.GetCurrentActionType(1) != Agent.ActionCodeType.DefendShield)
                {
                    return true;
                }
                Vec2 toAttacker = Agent.Position.AsVec2 - target.Position.AsVec2;
                toAttacker.Normalize();
                return target.GetMovementDirection().DotProduct(toAttacker) < AiKickBash.ShieldFacingCosine;
            }

            // Other mods that made the AI bash hit an unexplained engine crash when the bash was made with a polearm
            // held in two hands, so with one the AI only ever kicks. Uses _hadShield, set just before this is asked.
            private bool HoldsPolearmTwoHanded()
            {
                MissionWeapon main = Agent.WieldedWeapon;
                WeaponComponentData usage = main.IsEmpty ? null : main.CurrentUsageItem;
                return usage != null && (usage.WeaponClass == WeaponClass.TwoHandedPolearm || (usage.IsPolearm && !_hadShield));
            }

            private Agent.ActionCodeType CurrentAlternativeAttack()
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    Agent.ActionCodeType action = Agent.GetCurrentActionType(channel);
                    if (action >= Agent.ActionCodeType.AlternativeAttackAllBegin && action < Agent.ActionCodeType.AlternativeAttackAllEnd)
                    {
                        return action;
                    }
                }
                return Agent.ActionCodeType.Other;
            }
        }

        /// <summary>
        /// The knockdown rules of a kick or bash that lands (see AiKickBash). Applies to the player's kicks and bashes
        /// too. Runs after the posture patch, which may replace the blow and has by then taken the victim's posture.
        /// </summary>
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        internal class AiKickBashKnockDownPatch
        {
            // The blow's own type is not trusted alone: the posture patch may have rebuilt the blow.
            private static bool IsKicking(Agent attacker)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    Agent.ActionCodeType action = attacker.GetCurrentActionType(channel);
                    if (action >= Agent.ActionCodeType.KickAllBegin && action < Agent.ActionCodeType.KickAllEnd)
                    {
                        return true;
                    }
                }
                return false;
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData)
            {
                if (!RBMConfig.RBMConfig.aiKickBashEnabled || !collisionData.IsAlternativeAttack || attackerAgent == null || !attackerAgent.IsHuman ||
                    victimAgent == null || !victimAgent.IsHuman ||
                    victimAgent.MountAgent != null)
                {
                    return;
                }
                // A bash caught on the shield does nothing to the man behind it.
                if (collisionData.AttackBlockedWithShield)
                {
                    return;
                }
                // A man already reeling (posture break, tired, or a kick/bash a moment ago) has no footing left: down.
                bool staggered = AiKickBash.IsStaggered(victimAgent);
                AiKickBashComponent victimComponent = victimAgent.GetComponent<AiKickBashComponent>();
                if (victimComponent != null)
                {
                    victimComponent.LastKickedTime = victimAgent.Mission.CurrentTime;
                }
                if (staggered)
                {
                    __result.BlowFlag |= BlowFlags.KnockDown;
                    return;
                }
                // The man hit has already lost posture and stamina to the posture patch, by the blow's damage.
                if (RBMConfig.RBMConfig.postureEnabled && attackerAgent.IsEnemyOf(victimAgent) &&
                    AgentStances.values.TryGetValue(victimAgent, out Stance victimStance) && victimStance != null)
                {
                    // Posture gone: he goes down, no roll, and gets posture back as after any posture break.
                    if (victimStance.posture <= 0f)
                    {
                        __result.BlowFlag |= BlowFlags.KnockDown;
                        StanceLogic.ResetPostureForAgent(ref victimStance, AiKickBash.PostureBreakReset);
                        return;
                    }
                }

                float chance = AiKickBash.KnockDownEvenChance * AiKickBash.RelativeSkill(attackerAgent, victimAgent);
                // The full chance against a man holding his weapon ready or hit from behind; against anything else it
                // takes a better fighter, the more surely the bigger his skill lead.
                bool fromBehind = AiKickBash.IsBehind(attackerAgent, victimAgent);
                if (victimAgent.GetCurrentActionType(1) != Agent.ActionCodeType.ReadyMelee && !fromBehind)
                {
                    chance *= AiKickBash.SkillLeadFactor(attackerAgent, victimAgent);
                }
                // A kick puts a man down more readily than a bash.
                if (__result.AttackType == AgentAttackType.Kick || IsKicking(attackerAgent))
                {
                    chance *= AiKickBash.KnockDownKickMultiplier;
                }
                // So does one he does not see coming.
                if (fromBehind)
                {
                    chance *= AiKickBash.KnockDownBehindMultiplier;
                }
                // A man in heavy armour is harder to tip over.
                float armorWeight = victimAgent.SpawnEquipment.GetTotalWeightOfArmor(true);
                chance *= MBMath.Lerp(1f, AiKickBash.KnockDownHeavyArmorMultiplier,
                    MathF.Clamp((armorWeight - AiKickBash.LightArmorWeight) / (AiKickBash.HeavyArmorWeight - AiKickBash.LightArmorWeight), 0f, 1f));
                // A tired man is easier to put down.
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.staminaEnabled &&
                    AgentStances.values.TryGetValue(victimAgent, out Stance tiredStance) && tiredStance != null && tiredStance.maxStamina > 0f)
                {
                    chance *= MBMath.Lerp(AiKickBash.KnockDownExhaustedMultiplier, 1f, tiredStance.stamina / tiredStance.maxStamina);
                }
                if (MBRandom.RandomFloat < chance)
                {
                    __result.BlowFlag |= BlowFlags.KnockDown;
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WieldInitialWeapons")]
        internal class AiKickBashSpawnPatch
        {
            private static void Postfix(Agent __instance)
            {
                if (!RBMConfig.RBMConfig.aiKickBashEnabled || !__instance.IsHuman || __instance.GetComponent<AiKickBashComponent>() != null)
                {
                    return;
                }
                // Every man gets the component (the cost applies to the player too); only the AI gets its input driven.
                // Set once at spawn: toggling the callback mid-battle (to have it only during an attempt) crashed the
                // engine with an access violation -- the AI thread reads the flag while it runs.
                __instance.AddComponent(new AiKickBashComponent(__instance));
                if (__instance.Controller == AgentControllerType.AI)
                {
                    __instance.SetHasOnAiInputSetCallback(true);
                }
            }
        }
    }
}
