using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
        public static int numOfHits = 0;
        public static int numOfDurabilityDowngrade = 0;
        public static float throwableCorrectionSpeed = 3f;

        public static float swingSpeedTransfer = 4.5454545f;
        public static float thrustSpeedTransfer = 11.7647057f;
    }
}
