using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View;

namespace RBMCombat
{
    public partial class RangedRework
    {
        /// <summary>
        /// Better Arrow Visuals flies the real arrow/bolt mesh instead of the vanilla streak (XmlLoadingPatches sets
        /// flying_mesh = mesh), which is true to size and so hard to follow. This optionally fattens that mesh in
        /// flight only: the copy handed to the engine as the missile's flying mesh gets its two short axes scaled by
        /// <c>arrowThicknessScale</c>, length untouched. The quiver, the nocked arrow and stuck arrows use other
        /// meshes and stay true to size; the hit test is the engine's and does not change.
        /// </summary>
        [HarmonyPatch(typeof(ItemCollectionElementViewExtensions))]
        internal class FlyingArrowThicknessPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ItemCollectionElementViewExtensions.GetFlyingMeshCopy))]
            private static void Postfix(ItemObject item, MetaMesh __result)
            {
                float scale = RBMConfig.RBMConfig.arrowThicknessScale;
                if (__result == null || scale <= 1f || !RBMConfig.RBMConfig.betterArrowVisuals)
                {
                    return;
                }
                if (item.ItemType != ItemObject.ItemTypeEnum.Arrows && item.ItemType != ItemObject.ItemTypeEnum.Bolts)
                {
                    return;
                }
                // Only the realistic mesh: an item that kept its own flying mesh (a streak) is already easy to see.
                if (string.IsNullOrEmpty(item.MultiMeshName) || item.FlyingMeshName != item.MultiMeshName)
                {
                    return;
                }

                // The shaft runs along the mesh's longest axis; thicken the other two.
                BoundingBox box = __result.GetBoundingBox();
                Vec3 size = box.max - box.min;
                float longest = MathF.Max(size.x, MathF.Max(size.y, size.z));
                if (longest <= 0f)
                {
                    return;
                }
                Vec3 scaleXYZ = new Vec3(scale, scale, scale);
                if (size.x == longest)
                {
                    scaleXYZ.x = 1f;
                }
                else if (size.y == longest)
                {
                    scaleXYZ.y = 1f;
                }
                else
                {
                    scaleXYZ.z = 1f;
                }

                MatrixFrame frame = __result.Frame;
                frame.rotation.ApplyScaleLocal(in scaleXYZ);
                __result.Frame = frame;
            }
        }
    }
}
