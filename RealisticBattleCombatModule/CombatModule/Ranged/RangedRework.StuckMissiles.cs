using HarmonyLib;
using System;
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
        // Missiles stuck in a unit work loose and drop to the ground as loose, pickable items.
        //
        //  - Thrown weapons (javelins, throwing axes, throwing knives) always fall out, from a shield and from a
        //    living body alike, after about stuckThrownFallOutSeconds.
        //  - Arrows and bolts fall out of shields after about stuckArrowFallOutSeconds, and a shield holds at most
        //    MaxArrowsPerShield of them: one more and the oldest drops right away.
        //  - Arrows and bolts in a body have no timer, only a cap: a body (man or horse) keeps at most
        //    MaxArrowsPerBody, one more and the oldest drops.
        //  - Either setting at 0 leaves that kind stuck for good, as in the base game.
        //
        // HandleMissileCollisionReactionPatch registers every missile it attaches; the tick does the rest on the
        // main thread. Attachment indices shift as others are removed and the shield may be dropped, broken or
        // swapped in the meantime, so an entry remembers the item and the attach frame and looks its missile up
        // again when its time comes. Not found means the missile is no longer ours to drop (a shield on the ground
        // keeps its missiles, a corpse gets vanilla's handling) and the entry is forgotten.
        //
        // All state is on the MissionLogic instance, so nothing here outlives its mission.
        public class StuckMissileLogic : MissionLogic
        {
            // Arrows and bolts one shield holds before the oldest is pushed out.
            private const int MaxArrowsPerShield = 8;

            // Arrows and bolts one body keeps before the oldest is pushed out.
            private const int MaxArrowsPerBody = 4;

            // Each missile's time is the configured seconds scaled by a random factor in this range, so a volley
            // does not drop on one frame.
            private const float MinTimeFactor = 0.6f;
            private const float MaxTimeFactor = 1.4f;

            // The pending list is looked through this often, and at most this many missiles drop per look.
            private const float ScanInterval = 0.1f;
            private const int MaxDetachesPerScan = 8;

            // A missile whose shield entity does not line up with the attachment list yet is tried again this much
            // later, this many times, then left stuck.
            private const float RetryDelay = 1f;
            private const int MaxRetries = 3;

            // How the dropped missile leaves: pushed away from its bearer, a little downwards, tumbling.
            private const float DropOutwardSpeed = 0.7f;
            private const float DropDownwardSpeed = 0.5f;
            private const float DropSpin = 2.5f;

            // Reason code handed to the engine with the entity removal (the one vanilla uses for a picked up item).
            private const int EntityRemoveReason = 73;

            private const float SameOriginToleranceSquared = 1e-8f;

            private enum DetachResult
            {
                Dropped,
                Gone,
                Retry
            }

            private class Entry
            {
                public Agent Agent;
                public bool InShield;
                public EquipmentIndex Slot;
                public ItemObject Shield;
                public ItemObject Item;
                public Vec3 Origin;
                public bool IsThrown;
                public float Due;
                public int Retries;
            }

            // Mission.SpawnWeaponAux is what gives a dropped weapon its starting velocity; the public spawn calls
            // around it all pass zero.
            private delegate SpawnedItemEntity SpawnWeaponAuxDelegate(Mission mission, WeakGameEntity weaponEntity, MissionWeapon weapon, WeaponSpawnFlags spawnFlags, Vec3 globalVelocity, Vec3 globalAngularVelocity, bool hasLifeTime, bool spawnedOnACorpse);

            private static readonly SpawnWeaponAuxDelegate SpawnWeaponAux = CreateSpawnWeaponAux();

            // In the order the missiles stuck, oldest first.
            private readonly List<Entry> _entries = new List<Entry>();

            private float _nextScan;

            private static SpawnWeaponAuxDelegate CreateSpawnWeaponAux()
            {
                try
                {
                    return AccessTools.MethodDelegate<SpawnWeaponAuxDelegate>(AccessTools.Method(typeof(Mission), "SpawnWeaponAux"));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // Called right after a missile was attached to an agent's shield or body.
            public static void Register(Mission mission, Agent agent, bool inShield, EquipmentIndex shieldIndex, MissionWeapon missileWeapon)
            {
                if (GameNetwork.IsClientOrReplay || mission == null || agent == null || missileWeapon.IsEmpty)
                {
                    return;
                }
                bool isThrown = missileWeapon.HasAnyUsageWithWeaponClass(WeaponClass.Javelin) || missileWeapon.HasAnyUsageWithWeaponClass(WeaponClass.ThrowingAxe) ||
                    missileWeapon.HasAnyUsageWithWeaponClass(WeaponClass.ThrowingKnife);
                if (!isThrown && !(missileWeapon.HasAnyUsageWithWeaponClass(WeaponClass.Arrow) || missileWeapon.HasAnyUsageWithWeaponClass(WeaponClass.Bolt)))
                {
                    return;
                }
                float seconds = isThrown ? RBMConfig.RBMConfig.stuckThrownFallOutSeconds : RBMConfig.RBMConfig.stuckArrowFallOutSeconds;
                if (seconds <= 0f)
                {
                    return;
                }
                mission.GetMissionBehavior<StuckMissileLogic>()?.Add(agent, inShield, shieldIndex, missileWeapon.Item, isThrown, seconds);
            }

            private void Add(Agent agent, bool inShield, EquipmentIndex shieldIndex, ItemObject item, bool isThrown, float seconds)
            {
                // The frame is read back from the record the attach just made (the last one), not taken from the
                // caller: that is the value the missile is looked up by later.
                Entry entry = new Entry { Agent = agent, InShield = inShield, Slot = shieldIndex, Item = item, IsThrown = isThrown };
                if (inShield)
                {
                    if (agent.Equipment == null || shieldIndex < EquipmentIndex.WeaponItemBeginSlot || shieldIndex >= EquipmentIndex.NumAllWeaponSlots)
                    {
                        return;
                    }
                    MissionWeapon shield = agent.Equipment[shieldIndex];
                    int count = shield.IsEmpty ? 0 : shield.GetAttachedWeaponsCount();
                    if (count == 0)
                    {
                        return;
                    }
                    entry.Shield = shield.Item;
                    entry.Origin = shield.GetAttachedWeaponFrame(count - 1).origin;
                }
                else
                {
                    int count = agent.GetAttachedWeaponsCount();
                    if (count == 0)
                    {
                        return;
                    }
                    entry.Origin = agent.GetAttachedWeaponFrame(count - 1).origin;
                }
                // An arrow in a body has no timer: it only leaves when the cap pushes it out.
                entry.Due = !isThrown && !inShield ? float.MaxValue : Mission.CurrentTime + seconds * MBRandom.RandomFloatRanged(MinTimeFactor, MaxTimeFactor);
                _entries.Add(entry);
                if (!isThrown)
                {
                    PushOutOldestArrowOverCap(entry);
                }
            }

            // Runs inside the engine's missile hit callback, so the oldest arrow is only marked due; the next scan
            // drops it. One arrow arrives at a time, so one pushed out per arrival keeps the shield or body at its cap.
            private void PushOutOldestArrowOverCap(Entry added)
            {
                int count = 0;
                Entry oldest = null;
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry entry = _entries[i];
                    if (entry.Agent != added.Agent || entry.InShield != added.InShield || entry.IsThrown || entry.Due <= 0f)
                    {
                        continue;
                    }
                    if (added.InShield && (entry.Slot != added.Slot || entry.Shield != added.Shield))
                    {
                        continue;
                    }
                    count++;
                    if (oldest == null)
                    {
                        oldest = entry;
                    }
                }
                if (count > (added.InShield ? MaxArrowsPerShield : MaxArrowsPerBody) && oldest != null)
                {
                    oldest.Due = 0f;
                }
            }

            public override void OnMissionTick(float dt)
            {
                if (_entries.Count == 0 || GameNetwork.IsClientOrReplay)
                {
                    return;
                }
                float now = Mission.CurrentTime;
                if (now < _nextScan)
                {
                    return;
                }
                _nextScan = now + ScanInterval;

                // One pass that keeps the order: entries not yet due (or over this scan's budget) are kept, the
                // rest are dropped from the list whether or not their missile was still there.
                int dropped = 0;
                int kept = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry entry = _entries[i];
                    bool keep = true;
                    if (entry.Agent == null || !entry.Agent.IsActive())
                    {
                        // Body arrows have no due time, so a dead bearer's entries are cleared out here.
                        keep = false;
                    }
                    else if (entry.Due <= now && dropped < MaxDetachesPerScan)
                    {
                        DetachResult result = Detach(entry);
                        if (result == DetachResult.Dropped)
                        {
                            dropped++;
                        }
                        if (result == DetachResult.Retry && entry.Retries < MaxRetries)
                        {
                            entry.Retries++;
                            entry.Due = now + RetryDelay;
                        }
                        else
                        {
                            keep = false;
                        }
                    }
                    if (keep)
                    {
                        _entries[kept++] = entry;
                    }
                }
                _entries.RemoveRange(kept, _entries.Count - kept);
            }

            public override void OnRemoveBehavior()
            {
                _entries.Clear();
                base.OnRemoveBehavior();
            }

            private DetachResult Detach(Entry entry)
            {
                Agent agent = entry.Agent;
                if (agent == null || !agent.IsActive())
                {
                    return DetachResult.Gone;
                }
                try
                {
                    // The frames of whatever hangs off the skeleton lag the animation until the bones are brought up
                    // to date; without this the loose missile appears where the stuck one was some frames ago (far
                    // off for a running or animation-culled agent). Vanilla does the same before dropping a weapon
                    // or a corpse's stuck missiles (Mission.SpawnWeaponAsDropFromAgentAux, SpawnAttachedWeaponOnCorpse).
                    agent.AgentVisuals?.GetSkeleton()?.ForceUpdateBoneFrames();
                    return entry.InShield ? DetachFromShield(entry, agent) : DetachFromBody(entry, agent);
                }
                catch (Exception)
                {
                    return DetachResult.Gone;
                }
            }

            // The engine has no call that takes a missile back out of a weapon an agent holds. What it has is the
            // missile as a child entity of the shield's entity, in attachment order (vanilla reads a dropped
            // shield's missiles with GetChild(attachmentIndex)), and the managed record in the MissionWeapon. Both
            // are taken out here, and only here: if the engine turns out to keep a record of its own that this
            // leaves dangling, this is the one method to swap for re-equipping the shield with the shortened list.
            private DetachResult DetachFromShield(Entry entry, Agent agent)
            {
                if (agent.Equipment == null)
                {
                    return DetachResult.Gone;
                }
                // A copy of the slot, but the attachment lists inside it are the slot's own.
                MissionWeapon shield = agent.Equipment[entry.Slot];
                if (shield.IsEmpty || shield.Item != entry.Shield)
                {
                    return DetachResult.Gone;
                }
                int attachedCount = shield.GetAttachedWeaponsCount();
                int index = -1;
                for (int i = 0; i < attachedCount; i++)
                {
                    if (shield.GetAttachedWeapon(i).Item == entry.Item && IsSameOrigin(shield.GetAttachedWeaponFrame(i).origin, entry.Origin))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                {
                    return DetachResult.Gone;
                }
                WeakGameEntity shieldEntity = agent.GetWeaponEntityFromEquipmentSlot(entry.Slot);
                if (!shieldEntity.IsValid)
                {
                    return DetachResult.Gone;
                }

                // Children line up with the attachment list as long as the shield entity has no children of its
                // own. When the counts differ the index means nothing, and the missile is taken only if exactly one
                // child sits at its attach frame.
                WeakGameEntity missileEntity = WeakGameEntity.Invalid;
                int childCount = shieldEntity.ChildCount;
                if (childCount == attachedCount)
                {
                    missileEntity = shieldEntity.GetChild(index);
                }
                else
                {
                    for (int i = 0; i < childCount; i++)
                    {
                        WeakGameEntity child = shieldEntity.GetChild(i);
                        if (child.IsValid && IsSameOrigin(child.GetFrame().origin, entry.Origin))
                        {
                            if (missileEntity.IsValid)
                            {
                                return DetachResult.Retry;
                            }
                            missileEntity = child;
                        }
                    }
                }
                if (!missileEntity.IsValid)
                {
                    return DetachResult.Retry;
                }

                MissionWeapon missileWeapon = shield.GetAttachedWeapon(index);
                MatrixFrame stuckMeshFrame = GetGlobalMeshFrame(missileEntity);
                using (new TWSharedMutexWriteLock(Scene.PhysicsAndRayCastLock))
                {
                    missileEntity.Remove(EntityRemoveReason);
                }
                shield.RemoveAttachedWeapon(index);
                SpawnLooseCopy(missileWeapon, stuckMeshFrame, agent);
                return DetachResult.Dropped;
            }

            // Agent.DeleteAttachedWeapon drops the managed record and has the engine delete the missile from the
            // bone; the loose item is a new entity spawned where the stuck one was.
            //
            // Reusing the stuck entity instead (unparent it, then make it an item) was tried and does not work: it
            // stays driven by the agent's skeleton and floats along with its bearer, and the engine has no call to
            // unhook an entity from a bone. Both detaches therefore spawn a copy.
            private DetachResult DetachFromBody(Entry entry, Agent agent)
            {
                int attachedCount = agent.GetAttachedWeaponsCount();
                int index = -1;
                for (int i = 0; i < attachedCount; i++)
                {
                    if (agent.GetAttachedWeapon(i).Item == entry.Item && IsSameOrigin(agent.GetAttachedWeaponFrame(i).origin, entry.Origin))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                {
                    return DetachResult.Gone;
                }
                GameEntity missileEntity = agent.AgentVisuals?.GetAttachedWeaponEntity(index);
                if (missileEntity == null)
                {
                    return DetachResult.Gone;
                }
                MissionWeapon missileWeapon = agent.GetAttachedWeapon(index);
                MatrixFrame stuckMeshFrame = GetGlobalMeshFrame(missileEntity.WeakEntity);
                agent.DeleteAttachedWeapon(index);
                SpawnLooseCopy(missileWeapon, stuckMeshFrame, agent);
                return DetachResult.Dropped;
            }

            // A fresh item entity at the stuck missile's place: pickable, with physics, fading out on the usual
            // dropped-item timer.
            //
            // The stuck missile is the engine's missile entity and the copy an ordinary weapon item; the mesh is the
            // same, but the two entities do not hold it the same way (given the stuck entity's own frame, the copy
            // came out turned 90 degrees). So the copy is placed by its mesh, not its entity: stuckMeshFrame is
            // where the stuck mesh sat in the world, and the copy's entity frame is that, undone by where the copy
            // holds its own mesh.
            private void SpawnLooseCopy(MissionWeapon missileWeapon, MatrixFrame stuckMeshFrame, Agent agent)
            {
                if (missileWeapon.IsEmpty)
                {
                    return;
                }
                WeaponSpawnFlags spawnFlags = WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithPhysics;
                if (SpawnWeaponAux == null)
                {
                    MatrixFrame frame = stuckMeshFrame;
                    OrthonormalizeScale(ref frame);
                    GameEntity spawned = Mission.SpawnWeaponWithNewEntityAux(missileWeapon, spawnFlags, frame, -1, null, hasLifeTime: true);
                    if (spawned != null)
                    {
                        frame = EntityFrameForMeshAt(stuckMeshFrame, spawned.WeakEntity);
                        spawned.SetGlobalFrame(in frame);
                    }
                    return;
                }
                GameEntity itemEntity = TaleWorlds.MountAndBlade.GameEntityExtensions.Instantiate(Mission.Scene, missileWeapon, showHolsterWithWeapon: false, needBatchedVersion: true);
                if (itemEntity == null)
                {
                    return;
                }
                MatrixFrame globalFrame = EntityFrameForMeshAt(stuckMeshFrame, itemEntity.WeakEntity);
                itemEntity.CreateAndAddScriptComponent(typeof(SpawnedItemEntity).Name, callScriptCallbacks: true);
                itemEntity.SetGlobalFrame(in globalFrame);
                GetDropVelocity(globalFrame, agent, out Vec3 velocity, out Vec3 angularVelocity);
                SpawnWeaponAux(Mission, itemEntity.WeakEntity, missileWeapon, spawnFlags, velocity, angularVelocity, true, false);
            }

            // Where an entity's first mesh sits inside it: the meta-mesh's frame, then that mesh's own frame.
            private static MatrixFrame GetMeshFrameInEntity(WeakGameEntity entity)
            {
                MatrixFrame frame = MatrixFrame.Identity;
                if (entity.MultiMeshComponentCount <= 0)
                {
                    return frame;
                }
                MetaMesh metaMesh = entity.GetMetaMesh(0);
                if (metaMesh == null)
                {
                    return frame;
                }
                frame = metaMesh.Frame;
                if (metaMesh.MeshCount > 0)
                {
                    Mesh mesh = metaMesh.GetMeshAtIndex(0);
                    if (mesh != null)
                    {
                        frame = frame.TransformToParent(mesh.GetLocalFrame());
                    }
                }
                return frame;
            }

            // Where an entity's first mesh sits in the world.
            private static MatrixFrame GetGlobalMeshFrame(WeakGameEntity entity)
            {
                return entity.GetGlobalFrame().TransformToParent(GetMeshFrameInEntity(entity));
            }

            // The global frame that puts this entity's first mesh at meshFrame. Scale is dropped from both the
            // target (the stuck entity carries its bearer's scale, a physics body cannot take it) and the copy's own
            // mesh frame (so its inverse is its transpose).
            private static MatrixFrame EntityFrameForMeshAt(MatrixFrame meshFrame, WeakGameEntity entity)
            {
                MatrixFrame target = meshFrame;
                OrthonormalizeScale(ref target);
                MatrixFrame inEntity = GetMeshFrameInEntity(entity);
                OrthonormalizeScale(ref inEntity);
                MatrixFrame frame = target.TransformToParent(inEntity.TransformToLocal(MatrixFrame.Identity));
                OrthonormalizeScale(ref frame);
                return frame;
            }

            // The attached entity carries its bearer's scale, which a physics body cannot take.
            private static void OrthonormalizeScale(ref MatrixFrame frame)
            {
                if (!frame.rotation.IsOrthonormal())
                {
                    frame.rotation.Orthonormalize();
                }
            }

            private static void GetDropVelocity(MatrixFrame globalFrame, Agent agent, out Vec3 velocity, out Vec3 angularVelocity)
            {
                Vec3 outward = globalFrame.origin - agent.Position;
                outward.z = 0f;
                if (outward.Normalize() < 0.01f)
                {
                    outward = Vec3.Zero;
                }
                velocity = outward * DropOutwardSpeed;
                velocity.z = -DropDownwardSpeed;
                angularVelocity = new Vec3(MBRandom.RandomFloatRanged(-DropSpin, DropSpin), MBRandom.RandomFloatRanged(-DropSpin, DropSpin), MBRandom.RandomFloatRanged(-DropSpin, DropSpin));
            }

            private static bool IsSameOrigin(Vec3 a, Vec3 b)
            {
                return (a - b).LengthSquared < SameOriginToleranceSquared;
            }
        }
    }
}
