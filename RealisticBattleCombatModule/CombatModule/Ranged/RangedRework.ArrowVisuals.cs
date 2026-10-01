using HarmonyLib;
using JetBrains.Annotations;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        /// <summary>
        /// Better Arrow Visuals flies the real arrow/bolt mesh instead of the vanilla streak (XmlLoadingPatches sets
        /// flying_mesh = mesh), which is true to size and so hard to follow. This optionally fattens it in flight:
        /// the live missile entity's mesh components get their two short axes scaled by <c>arrowThicknessScale</c>,
        /// length untouched. Visual only, the hit test is the engine's.
        ///
        /// It has to be the live entity: a scale put on the flying mesh copy before the engine builds the missile
        /// (GetFlyingMeshCopy) is lost, the engine sets the mesh frames itself. The original frames are put back the
        /// moment the missile lands (<see cref="RestoreFlyingArrowThickness"/>, from the collision patch), so stuck
        /// and dropped arrows are true to size again.
        /// </summary>
        [HarmonyPatch(typeof(Mission))]
        internal class FlyingArrowThicknessPatch
        {
            // Not patched at all at 1.00 (or without Better Arrow Visuals), so the default costs nothing per shot.
            // PatchAll re-runs on every game start/load and re-reads the config, so a changed setting applies then.
            [UsedImplicitly]
            private static bool Prepare()
            {
                return RBMConfig.RBMConfig.arrowThicknessScale > 1f && RBMConfig.RBMConfig.betterArrowVisuals;
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnAgentShootMissile")]
            [UsedImplicitly]
            private static void Postfix(Mission __instance, Agent shooterAgent, Vec3 velocity)
            {
                float scale = RBMConfig.RBMConfig.arrowThicknessScale;
                if (scale <= 1f || !RBMConfig.RBMConfig.betterArrowVisuals || __instance.MissilesList.Count == 0)
                {
                    return;
                }
                // The engine callback appends the shot's missile last (none for a client-side prediction).
                Missile missile = __instance.MissilesList[__instance.MissilesList.Count - 1];
                if (missile.ShooterAgent != shooterAgent || missile.Entity == null || missile.Weapon.IsEmpty)
                {
                    return;
                }
                ItemObject item = missile.Weapon.Item;
                if (item.ItemType != ItemObject.ItemTypeEnum.Arrows && item.ItemType != ItemObject.ItemTypeEnum.Bolts)
                {
                    return;
                }
                // Only the realistic mesh: an item that kept its own flying mesh (a streak) is already easy to see.
                if (string.IsNullOrEmpty(item.MultiMeshName) || item.FlyingMeshName != item.MultiMeshName)
                {
                    return;
                }
                if (velocity.LengthSquared < 0.0001f)
                {
                    return;
                }

                if (_thickenedMission != __instance)
                {
                    _thickenedMission = __instance;
                    _thickenedArrows.Clear();
                }

                GameEntity entity = missile.Entity;
                // The shaft lies along the flight direction; take it into the entity's local space.
                Vec3 localDir = entity.GetGlobalFrame().rotation.TransformToLocal(velocity.NormalizedCopy());
                int count = entity.MultiMeshComponentCount;
                List<KeyValuePair<MetaMesh, MatrixFrame>> originals = new List<KeyValuePair<MetaMesh, MatrixFrame>>(count);
                for (int i = 0; i < count; i++)
                {
                    MetaMesh metaMesh = entity.GetMetaMesh(i);
                    if (metaMesh == null)
                    {
                        continue;
                    }
                    MatrixFrame frame = metaMesh.Frame;
                    originals.Add(new KeyValuePair<MetaMesh, MatrixFrame>(metaMesh, frame));

                    // ...and on into the mesh's own axes, which is where the scale is applied.
                    Vec3 meshDir = frame.rotation.TransformToLocal(localDir);
                    float ax = MathF.Abs(meshDir.x);
                    float ay = MathF.Abs(meshDir.y);
                    float az = MathF.Abs(meshDir.z);
                    Vec3 scaleXYZ = new Vec3(scale, scale, scale);
                    if (ax >= ay && ax >= az)
                    {
                        scaleXYZ.x = 1f;
                    }
                    else if (ay >= az)
                    {
                        scaleXYZ.y = 1f;
                    }
                    else
                    {
                        scaleXYZ.z = 1f;
                    }
                    frame.rotation.ApplyScaleLocal(in scaleXYZ);
                    metaMesh.Frame = frame;
                }
                if (originals.Count > 0)
                {
                    _thickenedArrows[missile] = originals;
                }
            }
        }

        // Missiles in flight that were thickened, with each mesh component's original frame. An entry is dropped when
        // its missile lands; one that never lands (flew off the map) lives until the next mission's first shot.
        private static readonly Dictionary<Missile, List<KeyValuePair<MetaMesh, MatrixFrame>>> _thickenedArrows = new Dictionary<Missile, List<KeyValuePair<MetaMesh, MatrixFrame>>>();
        private static Mission _thickenedMission;

        /// <summary>Puts a thickened missile's mesh frames back, before it sticks, drops or vanishes.</summary>
        internal static void RestoreFlyingArrowThickness(Missile missile)
        {
            if (_thickenedArrows.Count == 0 || missile == null || !_thickenedArrows.TryGetValue(missile, out List<KeyValuePair<MetaMesh, MatrixFrame>> originals))
            {
                return;
            }
            _thickenedArrows.Remove(missile);
            foreach (KeyValuePair<MetaMesh, MatrixFrame> original in originals)
            {
                original.Key.Frame = original.Value;
            }
        }
    }
}
