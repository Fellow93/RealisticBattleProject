using System.Collections.Generic;

namespace RBMCombat
{
    public class RangedWeaponStatsComparer : IEqualityComparer<string>
    {
        public int GetHashCode(string rac) { return rac.GetHashCode(); }
        public bool Equals(string rac1, string rac2) { return rac1.Equals(rac2); }
    }

    public class RangedWeaponStats
    {
        private int drawWeight = -1;

        public RangedWeaponStats(int drawWeight)
        {
            this.drawWeight = drawWeight;
        }

        public RangedWeaponStats()
        {
        }

        public void setDrawWeight(int drawWeight)
        {
            this.drawWeight = drawWeight;
        }


        public int getDrawWeight()
        {
            return drawWeight;
        }

    }
}
