using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// THE FORMATIONS AUTO-RESOLVE DOES NOT HAVE, AND THE CAPTAINS WHO WOULD HAVE LED THEM.
    ///
    /// A simulated battle has no formations in it. Not "a simplified version of them" -- none: the token Formation
    /// does not occur anywhere in the whole MapEvents namespace, because a Formation is a thing that belongs to a
    /// Mission and a Mission is exactly what auto-resolve exists to avoid constructing. What the simulation has is
    /// a flat roster per party and a random pick out of it for every blow.
    ///
    /// But the captain perks are worth nothing without one. A captain perk applies to "troops in your formation",
    /// and if there is no formation there is no answer to which men a hero's training reaches. So the formations
    /// are synthesised here: the side's men are bucketed by the only formation signal that survives into the
    /// campaign layer (CharacterObject.GetFormationClass, which for a hero is derived live off his horse and his
    /// bow, and for a line troop is the formation_group in his XML), and a captain is put over each bucket.
    ///
    /// PER SIDE, NOT PER PARTY -- because that is how a battle works. Every party on a side merges into one team
    /// with one set of formations, and the captains are drawn from the whole side's heroes. Three lords who each
    /// brought forty archers have, between them, one body of archers, and one man commands it.
    ///
    /// WHO THAT MAN IS, IS NATIVE'S OWN ANSWER AND NOT A BETTER ONE. See PickBucket: the assignment below is a port
    /// of GeneralsAndCaptainsAssignmentLogic.AssignBestCaptainsForTeam, down to its ordering and its tie-breaks. It
    /// would have been easy to do better -- the game even ships a per-troop-type captain rating (BattleCaptainModel.
    /// GetCaptainRatingForTroopUsages) that scores exactly this pairing, and native never once calls it outside a
    /// tooltip. Using it here was rejected deliberately: a battle the player auto-resolves and the same battle
    /// fought by hand must be led by the same men, or the sim is quietly answering a different question than the
    /// mission would. Fidelity to the game beats fidelity to good sense, when the whole point is to predict the
    /// game.
    /// </summary>
    internal static class SimulationCommandStructure
    {
        internal const int InfantryBucket = 0;

        internal const int RangedBucket = 1;

        internal const int CavalryBucket = 2;

        internal const int HorseArcherBucket = 3;

        /// <summary>The four default formation classes, which is what every regular class folds down to.</summary>
        internal const int BucketCount = 4;

        /// <summary>
        /// One side's chain of command: the man in charge of the whole thing, and the man over each body of troops.
        /// </summary>
        internal class SideCommand
        {
            /// <summary>The side's commanding lord -- vanilla's own LeaderParty.LeaderHero, the hero whose
            /// PowerModifier auto-resolve has been using as a stand-in for all of this. Kept for the log.</summary>
            public Hero Commander;

            /// <summary>
            /// The commander's <c>PowerModifier</c> that this battle threw away -- his captain-perk tally, which the
            /// blow no longer carries now that the real captains above are priced (see
            /// SimulationEquipmentPower.GetVanillaPowerNeutralizingFactor). Zero when the perk system is off, and
            /// then nothing is lifted.
            ///
            /// Recorded for the log, and it is the single most useful number there for calibrating this: it is
            /// exactly what a side LOST when the proxy came out, to be weighed against what its captains gave back.
            /// </summary>
            public float LeaderPowerLifted;

            /// <summary>Who leads each bucket, or null where nobody does. A bucket with no men never gets one.</summary>
            public readonly CharacterObject[] Captains = new CharacterObject[BucketCount];

            /// <summary>
            /// Who was APPOINTED, as against who is still on his feet. RetireTheFallen empties Captains as lords go
            /// down, so by the last round it says who SURVIVED -- and a battle's write-up is owed the men who led
            /// it. Written once, in Build, and never touched again.
            /// </summary>
            public readonly CharacterObject[] Appointed = new CharacterObject[BucketCount];

            /// <summary>
            /// Each captain's perk signature, worked out once here rather than on every blow he influences.
            /// SignatureOf walks the whole perk table and asks the hero for each one; doing that twice per blow in a
            /// battle of several thousand blows is exactly the sort of thing this file exists to precompute.
            /// </summary>
            public readonly int[] Signatures = new int[BucketCount];

            /// <summary>
            /// The captain over the men this troop stands with -- and null when the troop IS that captain, because a
            /// captain does not receive his own captain perks. Native applies that exclusion at the top of every
            /// model that reads a captain; it is applied here instead, once, at the only place the answer is
            /// produced, so no caller can forget it.
            /// </summary>
            public CharacterObject CaptainFor(CharacterObject troop, out int signature)
            {
                signature = 0;
                if (troop == null)
                {
                    return null;
                }
                int bucket = BucketOf(troop);
                CharacterObject captain = Captains[bucket];
                if (captain == null || captain == troop)
                {
                    return null;
                }
                signature = Signatures[bucket];
                return captain;
            }

            /// <summary>
            /// A captain who has fallen commands nobody. Called once a round -- four checks a side, no roster walk,
            /// which is the whole reason it is a validation and not a rebuild (see the note in AdvanceRound about
            /// what walking the rosters every round costs).
            ///
            /// He is not REPLACED, only cleared, and that is native's behaviour and not a shortcut: captains are
            /// assigned once when the team deploys, and Agent.RelieveFromCaptaincy simply nulls the formation's
            /// captain when its leader goes down. Nobody is promoted mid-battle in a real Bannerlord fight either.
            /// </summary>
            public void RetireTheFallen()
            {
                for (int i = 0; i < BucketCount; i++)
                {
                    CharacterObject captain = Captains[i];
                    if (captain == null)
                    {
                        continue;
                    }
                    Hero hero = captain.HeroObject;
                    if (hero == null || !hero.IsAlive || hero.IsWounded)
                    {
                        Captains[i] = null;
                        Signatures[i] = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Which body of men a troop stands with. FallbackClass is native's own fold of the eight regular formation
        /// classes onto the four default ones -- a Skirmisher is Ranged, a Heavy Infantryman is Infantry, a Light
        /// Cavalryman is (native's choice, not ours) a Horse Archer -- so the buckets here are the game's buckets
        /// and not a scheme invented for this file.
        /// </summary>
        internal static int BucketOf(CharacterObject troop)
        {
            if (troop == null)
            {
                return InfantryBucket;
            }
            switch (troop.GetFormationClass().FallbackClass())
            {
                case FormationClass.Ranged:
                    return RangedBucket;
                case FormationClass.Cavalry:
                    return CavalryBucket;
                case FormationClass.HorseArcher:
                    return HorseArcherBucket;
                default:
                    return InfantryBucket;
            }
        }

        /// <summary>Whether the men in a bucket are on horses -- which is the whole of native's hero-to-formation
        /// matching rule, and collapses to a property of the bucket now that the buckets ARE the formation classes.</summary>
        private static bool BucketIsMounted(int bucket)
        {
            return bucket == CavalryBucket || bucket == HorseArcherBucket;
        }

        /// <summary>
        /// Build one side's chain of command, from the muster it has already taken.
        ///
        /// The muster is passed in rather than re-walked: the rosters are walked exactly once per battle, at round
        /// one, and this hangs off that walk (see SimulationBattleState.AdvanceRound for why round one is the first
        /// moment a battle can be seen whole -- before it, a lord's allies have not attached themselves to the event
        /// yet, and a side counted at MapEventStarted is a fraction of the side that turns up).
        /// </summary>
        internal static SideCommand Build(MapEventSide side, MapEvent battle, Dictionary<CharacterObject, int> muster)
        {
            SideCommand command = new SideCommand();
            if (side == null || !SimulationPerks.Enabled)
            {
                return command;
            }

            command.Commander = (side.LeaderParty != null) ? side.LeaderParty.LeaderHero : null;
            command.LeaderPowerLifted = (command.Commander != null) ? command.Commander.PowerModifier : 0f;

            // How many men stand in each body. Native picks the BIGGEST formation a hero can lead, so this is the
            // number that decides it -- and a body with nobody in it is not led at all.
            int[] headcount = new int[BucketCount];
            if (muster != null)
            {
                foreach (KeyValuePair<CharacterObject, int> entry in muster)
                {
                    if (entry.Key != null && entry.Value > 0)
                    {
                        headcount[BucketOf(entry.Key)] += entry.Value;
                    }
                }
            }

            List<CharacterObject> heroes = HeroesOf(side);
            if (heroes.Count == 0)
            {
                return command;
            }

            bool[] taken = new bool[BucketCount];

            // THE PLAYER'S OWN ORDER OF BATTLE, IF HE SET ONE. A player who arranged his captains by hand and then
            // pressed "send troops" should see the men he chose leading the men he gave them.
            SeedFromOrderOfBattle(command, taken, heroes, headcount, battle, side);

            // Sorted by the score native sorts by -- which is campaign clout and not soldiering. See SergeantScore.
            SortByPriority(heroes, command.Commander);

            foreach (CharacterObject hero in heroes)
            {
                if (IsAlreadyCaptain(command, hero))
                {
                    continue;
                }
                int bucket = PickBucket(hero, headcount, taken);
                if (bucket >= 0)
                {
                    command.Captains[bucket] = hero;
                    taken[bucket] = true;
                }
            }

            for (int i = 0; i < BucketCount; i++)
            {
                command.Signatures[i] = SimulationPerks.SignatureOf(command.Captains[i]);
                command.Appointed[i] = command.Captains[i];
            }
            return command;
        }

        /// <summary>Every hero standing on this side and fit to lead, across all its parties.</summary>
        private static List<CharacterObject> HeroesOf(MapEventSide side)
        {
            List<CharacterObject> heroes = new List<CharacterObject>();
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = mapEventParty.Party;
                if (party == null || party.MemberRoster == null)
                {
                    continue;
                }
                for (int i = 0; i < party.MemberRoster.Count; i++)
                {
                    TroopRosterElement element = party.MemberRoster.GetElementCopyAtIndex(i);
                    CharacterObject character = element.Character;
                    if (character == null || !character.IsHero || character.HeroObject == null)
                    {
                        continue;
                    }
                    // A lord already carried off the field on a stretcher leads nothing. The roster still lists him
                    // (that is what WoundedNumber means for a hero stack), so the healthy count is the test.
                    if ((element.Number - element.WoundedNumber) <= 0 || !character.HeroObject.IsAlive)
                    {
                        continue;
                    }
                    if (!heroes.Contains(character))
                    {
                        heroes.Add(character);
                    }
                }
            }
            return heroes;
        }

        /// <summary>
        /// Native's pick order: the general first, then everyone else by GetCharacterSergeantScore, descending.
        ///
        /// Worth knowing what that score is, because it is not what anyone would assume. It is clan tier times a
        /// hundred for a clan leader (twenty for anyone else), plus two thousand for a king, plus the size of his
        /// army and the size of his party. It contains no skill, no perk and no hint of what the man is good at.
        /// Bannerlord hands the biggest formation to the most POLITICALLY IMPORTANT hero present and has always done
        /// so, and this is a port of Bannerlord.
        /// </summary>
        private static void SortByPriority(List<CharacterObject> heroes, Hero general)
        {
            heroes.Sort(delegate (CharacterObject a, CharacterObject b)
            {
                float scoreA = (general != null && a.HeroObject == general) ? float.MaxValue : SergeantScore(a);
                float scoreB = (general != null && b.HeroObject == general) ? float.MaxValue : SergeantScore(b);
                return scoreB.CompareTo(scoreA);
            });
        }

        private static float SergeantScore(CharacterObject hero)
        {
            if (hero == null || hero.HeroObject == null || Campaign.Current == null
                || Campaign.Current.Models == null || Campaign.Current.Models.EncounterModel == null)
            {
                return 0f;
            }
            return Campaign.Current.Models.EncounterModel.GetCharacterSergeantScore(hero.HeroObject);
        }

        /// <summary>
        /// The formation this hero takes, by native's rule -- which is worth stating plainly because it is so much
        /// less than it sounds: his mountedness must match the body's, and among the bodies that match he takes the
        /// BIGGEST one still going. That is the whole of PickBestRegularFormationToLead. No skill, no perk, no
        /// preference for the arm he is trained in. A rider takes the largest mounted body, a man on foot the
        /// largest foot body, and if nothing matches he leads nobody at all.
        ///
        /// Native runs a second pass here for the heroes who came away with nothing, offering them any leftover
        /// formation of matching mountedness. It is omitted, and not by accident: that pass tests the same predicate
        /// over a list that has only shrunk since the first, so a hero who found no match then cannot find one now.
        /// It is unreachable in native and would be unreachable here.
        /// </summary>
        private static int PickBucket(CharacterObject hero, int[] headcount, bool[] taken)
        {
            bool heroMounted = BucketIsMounted(BucketOf(hero));
            int best = -1;
            int bestCount = 0;
            for (int bucket = 0; bucket < BucketCount; bucket++)
            {
                if (taken[bucket] || headcount[bucket] <= bestCount)
                {
                    continue;
                }
                if (BucketIsMounted(bucket) != heroMounted)
                {
                    continue;
                }
                best = bucket;
                bestCount = headcount[bucket];
            }
            return best;
        }

        private static bool IsAlreadyCaptain(SideCommand command, CharacterObject hero)
        {
            for (int i = 0; i < BucketCount; i++)
            {
                if (command.Captains[i] == hero)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The captains the player picked himself, on the pre-battle Order of Battle screen, for his own side.
        ///
        /// This survives into the campaign layer, which is not obvious and is the only reason it can be honoured
        /// here: OrderOfBattleCampaignBehavior saves a Hero per formation index and the class he put it over. What
        /// it saves is a TEMPLATE, though, and a coarse one -- four flat lists keyed on nothing but (siege?, army?),
        /// shared by every party the player ever fights with. So it is his LAST layout, not this battle's, and
        /// everything below treats it as a suggestion to be checked rather than a fact.
        ///
        /// Checked how: the hero must actually be standing on this side today. That is worth doing for the obvious
        /// reason (his last layout may name a companion he left at home) and for a less obvious one -- native's own
        /// cleanup of dead heroes out of this store, OnHeroUnregistered, walks the two non-army lists and forgets
        /// the two ARMY lists entirely, so a hero killed while the player was in an army stays named as a captain
        /// forever. Verifying him against the muster closes that regardless of whose bug it is.
        /// </summary>
        private static void SeedFromOrderOfBattle(SideCommand command, bool[] taken, List<CharacterObject> heroes,
            int[] headcount, MapEvent battle, MapEventSide side)
        {
            if (battle == null || Campaign.Current == null || MobileParty.MainParty == null
                || !IsPlayerSide(side))
            {
                return;
            }

            OrderOfBattleCampaignBehavior behavior = Campaign.Current.GetCampaignBehavior<OrderOfBattleCampaignBehavior>();
            if (behavior == null)
            {
                return;
            }

            bool isSiege = battle.IsSiegeAssault;
            bool isInArmy = MobileParty.MainParty.Army != null;

            // Eight indices, because that is how many regular formations the screen has. Several of them will fold
            // onto the same bucket here (native's own eight classes fold onto four), and the first captain named for
            // a bucket keeps it -- there is no sensible way to have two men lead one body.
            for (int index = 0; index < 8; index++)
            {
                OrderOfBattleCampaignBehavior.OrderOfBattleFormationData data =
                    behavior.GetFormationDataAtIndex(index, isSiege, isInArmy);
                if (data == null || data.Captain == null)
                {
                    continue;
                }

                CharacterObject captain = data.Captain.CharacterObject;
                if (captain == null || !heroes.Contains(captain))
                {
                    // Named in the template but not on the field today. See the note above.
                    continue;
                }

                foreach (int bucket in BucketsOf(data.FormationClass))
                {
                    // A body with no men in it needs no captain, and a body that already has one keeps him.
                    //
                    // Note there is deliberately no "has this hero already got a formation" test here. He may well
                    // have: a merged class hands him BOTH its buckets, which is the whole point of the player having
                    // merged them. The greedy pass below is where a hero is stopped from collecting a second body,
                    // and it checks for exactly that.
                    if (headcount[bucket] > 0 && !taken[bucket])
                    {
                        command.Captains[bucket] = captain;
                        taken[bucket] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Which bucket (or buckets) a deployment class covers. The screen lets the player merge two classes under
        /// one man -- foot and bows together, or all his horse together -- and when he has, that man captains both
        /// bodies here, which is exactly what he asked for.
        /// </summary>
        private static IEnumerable<int> BucketsOf(DeploymentFormationClass formationClass)
        {
            switch (formationClass)
            {
                case DeploymentFormationClass.Infantry:
                    yield return InfantryBucket;
                    break;
                case DeploymentFormationClass.Ranged:
                    yield return RangedBucket;
                    break;
                case DeploymentFormationClass.Cavalry:
                    yield return CavalryBucket;
                    break;
                case DeploymentFormationClass.HorseArcher:
                    yield return HorseArcherBucket;
                    break;
                case DeploymentFormationClass.InfantryAndRanged:
                    yield return InfantryBucket;
                    yield return RangedBucket;
                    break;
                case DeploymentFormationClass.CavalryAndHorseArcher:
                    yield return CavalryBucket;
                    yield return HorseArcherBucket;
                    break;
                default:
                    break;
            }
        }

        /// <summary>Whether the player's own party is standing on this side -- the only side whose Order of Battle
        /// he ever set.</summary>
        private static bool IsPlayerSide(MapEventSide side)
        {
            if (side == null || MobileParty.MainParty == null)
            {
                return false;
            }
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                if (mapEventParty.Party != null && mapEventParty.Party.MobileParty == MobileParty.MainParty)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
