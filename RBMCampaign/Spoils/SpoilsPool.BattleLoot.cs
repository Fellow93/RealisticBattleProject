using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The field a stack holds after a battle is stripped of the kit its dead wore, and that kit is
    /// shared out among the victors: the veterans pick first, the further beneath a man a piece lies
    /// the likelier he is to step over it, and a man can only carry so much.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// ItemObject.Tier is clamp(round(Tierf), 0, 6) - 1, so it yields -1 for anything whose
        /// Tierf rounds down to zero. Fold those into Tier1 rather than indexing off the array.
        /// </summary>
        private static int GetItemTier(ItemObject item)
        {
            return MathF.Min(MathF.Max((int)item.Tier, 0), (int)ItemObject.ItemTiers.NumTiers - 1);
        }

        /// <summary>
        /// How far beneath a troop a piece of kit is. Item tiers are zero based (Tier1 == 0) and troop
        /// tiers are one based, so an item matches a troop's tier when its index is one lower. Zero or
        /// less means the kit is as good as his own or better.
        /// </summary>
        private static int GetTierGap(int itemTier, CharacterObject character)
        {
            return (character.Tier - 1) - itemTier;
        }

        /// <summary>
        /// The share of a heap of kit a troop stoops to pick up. He never walks past his own tier or
        /// better, but the further beneath him a piece is the likelier he is to leave it lying: a
        /// veteran picking over a field steps over the recruits' spears without quite seeing them.
        /// What he passes over stays on the field for the greener troops behind him, which is how the
        /// cheap kit reaches the men it would actually be an upgrade for.
        /// </summary>
        /// <remarks>
        /// Compounded per tier of gap rather than subtracted, so nothing is ever certain to be
        /// overlooked and a rag of tier 1 is still worth something to the tier 6 man who finds
        /// himself alone on the field. An overlook chance of 1 is the exception: then a troop sees
        /// nothing at all beneath his own tier, which is how this worked before.
        /// </remarks>
        private static float GetNoticeFraction(int tierGap)
        {
            if (tierGap <= 0)
            {
                return 1f;
            }
            float overlook = MathF.Clamp(RBMConfig.RBMConfig.troopLootOverlookChancePerTier, 0f, 1f);
            return MathF.Pow(1f - overlook, tierGap);
        }

        /// <summary>
        /// Of <paramref name="available"/> pieces lying at a troop's feet, how many he sees. The
        /// fractional piece is rolled for rather than dropped, so a lone piece a veteran would notice
        /// a quarter of the time is not silently unlootable.
        /// </summary>
        private static long GetNoticedPieces(long available, int tierGap)
        {
            float exact = available * GetNoticeFraction(tierGap);
            long whole = (long)exact;
            return whole + ((MBRandom.RandomFloat < exact - whole) ? 1L : 0L);
        }

        /// <summary>Pieces of kit one man will carry off a field, however much of it he sees.</summary>
        private static int GetCarryCapacity(int menInStack)
        {
            return MathF.Max(0, RBMConfig.RBMConfig.troopLootPiecesPerMan) * menInStack;
        }

        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier <= 0f)
            {
                return;
            }
            MapEventSide winner = mapEvent.Winner;
            if (winner == null || winner.OtherSide == null)
            {
                return;
            }

            // What the field yields, bucketed by the tier of the kit so a man's own tier can decide
            // how likely he is to stoop for it. Only the dead are stripped: the wounded are carried
            // off still wearing their kit, and the routed fled with theirs. The victors hold the
            // field, so they recover their own fallen as well as the enemy's.
            //
            // Counted twice over: pieces, because a man can only carry so many, and value, because
            // that is what a piece is worth once it is his. Both are long, since a big battle sums
            // well past what an int would hold.
            long[] spoilsByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long[] piecesByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long intactValue = 0L;
            foreach (MapEventParty loser in winner.OtherSide.Parties)
            {
                CountStrippedEquipment(spoilsByTier, piecesByTier, loser.DiedInBattle, ref intactValue);
            }
            foreach (MapEventParty victor in winner.Parties)
            {
                CountStrippedEquipment(spoilsByTier, piecesByTier, victor.DiedInBattle, ref intactValue);
            }

            long totalContribution = 0L;
            foreach (MapEventParty victor in winner.Parties)
            {
                totalContribution += MathF.Max(0, victor.ContributionToBattle);
            }
            if (SpoilsLog.IsEnabled)
            {
                long salvagedValue = 0L;
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    salvagedValue += spoilsByTier[tier];
                }
                SpoilsLog.Log("LOOT", "battle ended: " + mapEvent.EventType + ", winner side " + mapEvent.WinningSide
                    + ", " + winner.Parties.Count + " victor party(s), " + winner.OtherSide.Parties.Count + " loser party(s)");
                SpoilsLog.Log("LOOT", "  the dead wore " + intactValue + " value; " + salvagedValue + " salvaged ("
                    + (intactValue > 0L ? (100L * salvagedValue / intactValue) : 0L) + "%)");
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    if (spoilsByTier[tier] > 0L)
                    {
                        SpoilsLog.Log("LOOT", "  field yields tier " + (tier + 1) + ": " + piecesByTier[tier]
                            + " pieces worth " + spoilsByTier[tier]);
                    }
                }
            }

            foreach (MapEventParty victor in winner.Parties)
            {
                // Simulated battles can leave every contribution at zero; fall back to an even split
                // rather than silently paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, victor.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : winner.Parties.Count;
                float share = (float)weight / divisor * RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier;
                SpoilsLog.Log("LOOT", victor.Party, SpoilsLog.Describe(victor.Party) + ": contribution " + victor.ContributionToBattle
                    + "/" + totalContribution + ", share " + share.ToString("0.000"));
                int granted = GrantToParty(victor.Party, spoilsByTier, piecesByTier, share);
                if (victor.Party == PartyBase.MainParty)
                {
                    AnnounceSpoilsToPlayer(granted);
                }
            }
        }

        /// <summary>
        /// The stockpiles fill silently otherwise: the party screen shows a bar the player has to go
        /// looking for, and nothing on the map says a battle paid for anything.
        /// </summary>
        /// <remarks>
        /// A victory can still grant nothing: a small party with its arms already full, or one whose
        /// men walked past everything the field had left. Saying so is more use than saying nothing.
        /// </remarks>
        private static void AnnounceSpoilsToPlayer(int granted)
        {
            TextObject message = new TextObject((granted > 0)
                ? "{=RBM_SPOILS_009}Your men strip the fallen and recover {AMOUNT} in spoils."
                : "{=RBM_SPOILS_010}Your men find nothing on the fallen they can use.");
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }

        /// <summary>Narrowest and widest share of its worth a piece of kit can survive a battle with.</summary>
        private const float MinSalvageFraction = 0.25f;
        private const float MaxSalvageFraction = 0.75f;

        /// <summary>
        /// Nothing comes off a battlefield intact, and nothing is destroyed outright either. Armour
        /// is battered, weapons are chipped, and a quiver is only worth the arrows still in it. The
        /// exact condition of a dead man's kit is not knowable after the fact -- RBM's armour
        /// degradation lives on the mission's agents and dies with them, and simulated battles never
        /// spawn agents at all -- so each piece salvages a random fraction of its worth, between a
        /// quarter and three quarters.
        /// </summary>
        /// <remarks>
        /// For a quiver of arrows or a bundle of javelins -- anything whose PrimaryWeapon is
        /// IsConsumable -- the roll is the share still unspent when its owner fell. For armour and
        /// weapons it is the share that survived the fighting. Same distribution, different reason.
        /// The mean is still a half, so the loot a stack yields averages to half what replacing it
        /// costs, exactly as it did when the roll spanned the whole range.
        /// </remarks>
        private static float RollSalvageFraction(ItemObject item)
        {
            return MBRandom.RandomFloatRanged(MinSalvageFraction, MaxSalvageFraction);
        }

        /// <summary>
        /// Every equipment slot of every fallen man yields part of its item's value, bucketed by the
        /// item's tier so a looter's own tier can decide how likely he is to notice it. Rolled per man
        /// rather than per troop type, so a hundred casualties average out.
        /// </summary>
        /// <remarks>
        /// Each man is stripped of one battle set drawn at random, the way the game dressed him when
        /// it spawned him. Over a stack this averages to the same value GetEquipmentValue prices an
        /// upgrade against, so a troop cannot yield kit worth more than it costs to replace.
        /// </remarks>
        private static void CountStrippedEquipment(long[] spoilsByTier, long[] piecesByTier, TroopRoster roster, ref long intactValue)
        {
            if (roster == null)
            {
                return;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                List<Equipment> sets = GetBattleEquipments(element.Character);
                if (sets.Count == 0)
                {
                    continue;
                }
                for (int man = 0; man < element.Number; man++)
                {
                    foreach (EquipmentElement item in EnumerateEquipmentSlots(sets[MBRandom.RandomInt(sets.Count)]))
                    {
                        int tier = GetItemTier(item.Item);
                        intactValue += item.ItemValue;
                        spoilsByTier[tier] += (long)(item.ItemValue * RollSalvageFraction(item.Item));
                        piecesByTier[tier]++;
                    }
                }
            }
        }

        /// <summary>
        /// The party's share of the field, tier by tier. Every stack has a claim on every tier now --
        /// a veteran will take a recruit's spear if nothing better is lying near him -- but the
        /// veterans walk the field first, and the further beneath them a piece is the likelier they
        /// are to step over it. What they overlook or cannot carry is left for the greener troops.
        /// </summary>
        /// <remarks>
        /// The pieces a stack has already carried off are tracked across the whole field, not per
        /// tier, so a man who filled his arms with mail cannot also stoop for six spears.
        /// </remarks>
        /// <returns>The points the party's stacks actually took, which is less than its share
        /// whenever a tier is overlooked or is more than the men have arms to carry.</returns>
        private static int GrantToParty(PartyBase party, long[] spoilsByTier, long[] piecesByTier, float share)
        {
            if (party == null || share <= 0f)
            {
                return 0;
            }
            Dictionary<CharacterObject, int> carried = new Dictionary<CharacterObject, int>();
            int granted = 0;
            for (int tier = 0; tier < spoilsByTier.Length; tier++)
            {
                if (piecesByTier[tier] <= 0L)
                {
                    continue;
                }
                // A piece is worth the tier's average. Scaling the count by the share rather than the
                // worth keeps a party's total at exactly the fraction of the field it earned.
                float valuePerPiece = (float)spoilsByTier[tier] / piecesByTier[tier];
                int pieces = (int)MathF.Min(piecesByTier[tier] * share, (float)int.MaxValue);
                if (pieces > 0 && valuePerPiece > 0f)
                {
                    granted += GrantTierToParty(party, carried, tier, pieces, valuePerPiece);
                }
            }
            return granted;
        }

        private static int GrantTierToParty(PartyBase party, Dictionary<CharacterObject, int> carried, int itemTier, int pieces, float valuePerPiece)
        {
            List<TroopRosterElement> claimants = new List<TroopRosterElement>();
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero && GetCarryRoom(carried, element) > 0)
                {
                    claimants.Add(element);
                }
            }
            // Highest troop tier first, so the veterans take their pick before the recruits.
            claimants.Sort((a, b) => b.Character.Tier.CompareTo(a.Character.Tier));

            if (claimants.Count == 0)
            {
                SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + " (" + pieces + " pieces): no claimant in "
                    + SpoilsLog.Describe(party) + ", discarded");
                return 0;
            }

            int granted = 0;
            int remaining = pieces;
            int groupStart = 0;
            while (groupStart < claimants.Count && remaining > 0)
            {
                int groupEnd = groupStart;
                int groupTier = claimants[groupStart].Character.Tier;
                while (groupEnd < claimants.Count && claimants[groupEnd].Character.Tier == groupTier)
                {
                    groupEnd++;
                }
                // What this rank of troops sees of what is still lying there. The rest they walk past.
                int gap = GetTierGap(itemTier, claimants[groupStart].Character);
                int noticed = (int)MathF.Min(GetNoticedPieces(remaining, gap), (long)remaining);
                remaining -= GrantToTierGroup(party, carried, claimants, groupStart, groupEnd, noticed, itemTier, valuePerPiece, ref granted);
                groupStart = groupEnd;
            }

            if (remaining > 0)
            {
                SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + ": " + remaining
                    + " of " + pieces + " pieces left lying in " + SpoilsLog.Describe(party)
                    + " (overlooked, full, or no arms to carry them)");
            }
            return granted;
        }

        /// <summary>Pieces this stack has arms left to carry.</summary>
        private static int GetCarryRoom(Dictionary<CharacterObject, int> carried, TroopRosterElement element)
        {
            int taken;
            carried.TryGetValue(element.Character, out taken);
            return MathF.Max(0, GetCarryCapacity(element.Number) - taken);
        }

        /// <summary>
        /// Stacks of equal troop tier have equal claim, so they split by head count, and anything a
        /// stack cannot take, because its arms are already full, is passed around the group before
        /// cascading. Returns how many pieces the group actually carried off.
        /// </summary>
        private static int GrantToTierGroup(PartyBase party, Dictionary<CharacterObject, int> carried, List<TroopRosterElement> claimants, int start, int end, int available, int itemTier, float valuePerPiece, ref int granted)
        {
            int groupMen = 0;
            for (int i = start; i < end; i++)
            {
                groupMen += claimants[i].Number;
            }
            if (groupMen <= 0 || available <= 0)
            {
                return 0;
            }

            int[] shares = new int[end - start];
            int allocated = 0;
            for (int i = start; i < end; i++)
            {
                int proportional = (int)((long)available * claimants[i].Number / groupMen);
                shares[i - start] = MathF.Min(GetCarryRoom(carried, claimants[i]), proportional);
                allocated += shares[i - start];
            }

            // Hand what the full-handed stacks left behind to their peers before it cascades down.
            int leftover = available - allocated;
            for (int i = start; i < end && leftover > 0; i++)
            {
                int room = GetCarryRoom(carried, claimants[i]) - shares[i - start];
                int extra = MathF.Min(leftover, room);
                shares[i - start] += extra;
                leftover -= extra;
            }

            int consumed = 0;
            for (int i = start; i < end; i++)
            {
                TroopRosterElement element = claimants[i];
                int taken = shares[i - start];
                if (taken <= 0)
                {
                    continue;
                }
                int points = MathF.Round(taken * valuePerPiece);
                if (SpoilsLog.IsEnabled)
                {
                    int before = GetSpoils(party, element.Character);
                    SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + " -> " + SpoilsLog.Describe(element.Character)
                        + " x" + element.Number + " in " + SpoilsLog.Describe(party)
                        + ": " + taken + " pieces, +" + points + " (pool " + before + " -> " + (before + points)
                        + ", arms left " + (GetCarryRoom(carried, element) - taken)
                        + ", gap " + GetTierGap(itemTier, element.Character) + ")");
                }
                AddSpoils(party, element.Character, points);
                int alreadyCarried;
                carried.TryGetValue(element.Character, out alreadyCarried);
                carried[element.Character] = alreadyCarried + taken;
                granted += points;
                consumed += taken;
            }
            return consumed;
        }
    }
}
