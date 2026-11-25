using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // The game writes each save entry's length as a 16-bit number, so a ledger string over 32,767 bytes
    // corrupts the save. Long strings are cut into pieces for the save and put back together on load.
    public class SaveStringChunksSpecs
    {
        [Test]
        public void A_ledger_that_fits_is_saved_as_it_is()
        {
            var ledger = new Dictionary<string, string> { { "lines", "Lwages=-120;Ltax=300" } };

            Assert.That(SaveStringChunks.Split(ledger), Is.SameAs(ledger));
            Assert.That(SaveStringChunks.Join(ledger), Is.SameAs(ledger));
        }

        [Test]
        public void A_long_ledger_is_saved_in_pieces_the_game_can_hold_and_loads_back_unchanged()
        {
            // 34 KB of ASCII, as in the reported save, and a non-Latin one with emoji (4-byte characters).
            var ledger = new Dictionary<string, string>
            {
                { "lines", new string('x', 34000) },
                { "trade", new StringBuilder().Insert(0, "Жалованье😀", 4000).ToString() },
                { "day", "1,2,3" }
            };

            Dictionary<string, string> saved = SaveStringChunks.Split(ledger);

            foreach (string piece in saved.Values)
            {
                Assert.That(Encoding.UTF8.GetByteCount(piece), Is.LessThan(32000));
            }
            Assert.That(SaveStringChunks.Join(saved), Is.EquivalentTo(ledger));
            Assert.That(ledger["lines"].Length, Is.EqualTo(34000), "the live ledger is not touched by saving");
        }
    }
}
