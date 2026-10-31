using NUnit.Framework;
using C = RBMAI.ArmyComposition.Category;

namespace RBMAI.Tests
{
    // How an army's make-up turns into the composition ramps, token forces and the main-body verdict.
    public class ArmyCompositionSpecs
    {
        // men by category, in Category order: infantry, archers, cavalry, horse archers
        private static ArmyComposition.Snapshot Armies(int[] own, int[] enemy, int ownToken = 0, int enemyToken = 0)
        {
            ArmyComposition.Snapshot s = new ArmyComposition.Snapshot();
            int n = ArmyComposition.CategoryCount;
            for (int c = 0; c < n; c++)
            {
                s.MenBy[c] = own[c];
                s.MenBy[n + c] = enemy[c];
                s.S[c] = own[c] * ArmyComposition.CategoryWeights[c];
                s.S[n + c] = enemy[c] * ArmyComposition.CategoryWeights[c];
                s.OwnMen += own[c];
                s.EnemyMen += enemy[c];
                s.EnemyStrength += s.S[n + c];
            }
            ArmyComposition.Derive(s, ownToken, enemyToken);
            ArmyComposition.ComputeRamps(s, footRangedHeld: false);
            return s;
        }

        [Test]
        public void A_normal_mixed_army_against_another_changes_nothing()
        {
            // the most mounted army still called normal: 40 % infantry, 30 % mounted men, on both sides
            int[] army = { 160, 120, 80, 40 };
            ArmyComposition.Snapshot s = Armies(army, army);
            Assert.That(new[] { s.KFoot, s.KCav, s.KWeak, s.KMount }, Is.All.EqualTo(0f));
            // kHA only ever acts through the wear-down gate, kHA x kMount
            Assert.That(s.KHA * s.KMount, Is.EqualTo(0f));
            Assert.That(s.FootRangedEngage, Is.False);
            Assert.That(s.OwnToken, Is.EqualTo(0));
        }

        [Test]
        public void An_enemy_without_melee_infantry_reads_fully_footless()
        {
            ArmyComposition.Snapshot s = Armies(new[] { 200, 80, 40, 0 }, new[] { 0, 120, 0, 100 });
            Assert.That(s.KFoot, Is.EqualTo(1f));
            Assert.That(s.FootRangedEngage, Is.True);
        }

        [Test]
        public void A_handful_of_enemy_infantry_counts_as_no_infantry()
        {
            ArmyComposition.Snapshot s = Armies(new[] { 200, 80, 40, 0 }, new[] { 6, 120, 0, 100 });
            Assert.That(s.IsEnemyToken(C.Infantry), Is.True);
            Assert.That(s.KFoot, Is.EqualTo(1f));
        }

        [Test]
        public void A_few_archers_do_not_count_in_a_heavy_cavalry_army()
        {
            ArmyComposition.Snapshot s = Armies(new[] { 0, 8, 150, 60 }, new[] { 200, 80, 40, 0 });
            Assert.That(s.IsOwnToken(C.Archers), Is.True);
            Assert.That(s.OwnReal(C.Cavalry), Is.True);
        }

        [Test]
        public void A_token_force_stays_token_until_it_clearly_grows()
        {
            // 12 % archers: below the leave share but above the enter share
            int[] own = { 300, 48, 52, 0 };
            int[] enemy = { 200, 80, 40, 0 };
            int archerBit = 1 << (int)C.Archers;
            Assert.That(Armies(own, enemy, ownToken: archerBit).IsOwnToken(C.Archers), Is.True, "was token");
            Assert.That(Armies(own, enemy, ownToken: 0).IsOwnToken(C.Archers), Is.False, "was not token");
        }

        [Test]
        public void A_mostly_mounted_horse_archer_army_reads_cavalry_heavy()
        {
            ArmyComposition.Snapshot s = Armies(new[] { 20, 0, 40, 200 }, new[] { 200, 80, 40, 0 });
            Assert.That(s.KMount, Is.EqualTo(1f));
            Assert.That(s.KHA, Is.EqualTo(1f));
        }

        [TestCase(false, 0.24f, false)]
        [TestCase(false, 0.25f, true)]
        [TestCase(true, 0.16f, true)]
        [TestCase(true, 0.14f, false)]
        public void Infantry_leads_from_a_quarter_of_the_army_and_keeps_the_lead_down_to_15_percent(bool held, float share, bool leads)
        {
            Assert.That(ArmyComposition.LeadsVerdict(held, infantryToken: false, share), Is.EqualTo(leads));
        }

        [Test]
        public void Token_infantry_never_leads()
        {
            Assert.That(ArmyComposition.LeadsVerdict(held: true, infantryToken: true, 0.9f), Is.False);
        }

        [TestCase(100f, 110f, false, true)]
        [TestCase(100f, 125f, false, false)]
        [TestCase(100f, 125f, true, true)]
        public void A_foot_anchor_in_contact_is_not_swapped_mid_fight(float held, float best, bool inContact, bool keeps)
        {
            Assert.That(ArmyComposition.KeepsAnchor(heldValid: true, held, best, inContact), Is.EqualTo(keeps));
            Assert.That(ArmyComposition.KeepsAnchor(heldValid: false, held, best, inContact), Is.False, "a gone anchor is always replaced");
        }

        [TestCase(0.19f, true)]
        [TestCase(0.2f, false)]
        public void Archers_are_dry_under_a_fifth_of_their_ammunition(float ammo, bool dry)
        {
            Assert.That(ArmyComposition.IsDryAmmo(ammo), Is.EqualTo(dry));
        }

        [TestCase(0.4f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(0.7f, 1f)]
        public void A_ramp_is_zero_before_its_start_and_one_from_its_end(float x, float k)
        {
            Assert.That(ArmyComposition.Ramp(x, 0.45f, 0.55f) , Is.EqualTo(k).Within(1e-5f));
        }
    }
}
