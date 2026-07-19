# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Realistic Battle Mod (RBM) for Mount & Blade II: Bannerlord. A comprehensive combat overhaul mod that rewrites damage calculations, armor mechanics, AI behavior, and adds a stamina/posture system. Built on Harmony 2.4.2 for non-invasive runtime patching of game methods.

Current version: v4.3.4 (SubModule.xml). Targets Bannerlord v1.3.13+.

## Build

**Solution:** `RealisticBattle.sln` — .NET Framework 4.7.2, x64 target.

Build with Visual Studio (2017+) or MSBuild:
```
msbuild RealisticBattle.sln /p:Configuration=Release /p:Platform="Any CPU"
```

All 6 projects output DLLs to `../../RBM/bin/Win64_Shipping_Client/` (relative to each project folder, resolving to the `RBM/bin/` directory within the Bannerlord Modules tree).

Single-project build (from memory — MSBuild path is under VS `18\Community`, and `Platform` must be quoted with its space):
```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RealisticBattle.sln /p:Configuration=Debug "/p:Platform=Any CPU" /t:RBM /verbosity:minimal
```

**Debug launch:** Bannerlord.exe with `/singleplayer _MODULES_*Native*SandBoxCore*SandBox*StoryMode*CustomBattle*RBM*_MODULES_`

No test suite exists — testing is manual via in-game verification.

## Architecture

### Module Structure (6 C# projects)

**RBM** — Main coordinator. Entry point: `RBM.SubModule` (extends `MBSubModuleBase`). Manages Harmony instance lifecycle, conditionally patches/unpatches each subsystem based on config toggles. References RBMAI and RBMCampaign.

**RBMAI** — AI and stamina/posture system. Patcher entry: `RBMAiPatcher` (uses `Harmony.PatchAll()`, so file location is cosmetic — patches are discovered by attribute, not registered by hand). References RBMConfig. As of 2026-07-18 the module was reorganized from a handful of multi-thousand-line monoliths into subsystem folders under `AiModule/`. Large classes were split across files using `partial class` (and, for `Utilities`, partial static files at project root) so no call sites changed. Namespace stays flat `RBMAI`; the old-style csproj lists every file with an explicit `<Compile Include>` — **update it when adding/moving files**. Layout:
- `AiModule/Stance/` — the posture/stamina system. `StanceLogic` (MissionBehavior) split into `StanceLogic.Core.cs` / `.Visuals.cs` / `StanceLifecyclePatches.cs` / `StanceMiscPatches.cs`; its huge `CreateMeleeBlowPatch` split via nested `partial class` into `MeleeBlowPatch.cs` (orchestration + attributes) / `.Math.cs` (calculators) / `.Ranged.cs`. State types in `Stance.cs` + `PostureDamageTable.cs`. View-models under `Stance/UI/`.
- `AiModule/Behaviors/` — formation behavior patches (`SkirmishBehaviors`, `DefenseBehaviors`, `FlankBehaviors`, `AdvanceBehavior`, `AgentSpeedAndParams`, `MiscBehaviorPatches`); custom `RBMBehavior*` subclasses under `Behaviors/Custom/`.
- `AiModule/Tactics/` — `partial class Tactics` split into `TacticsState`, `DamageTracking`, `Lifecycle`, `TacticOverrides`, `FormationCounts`; custom `RBMTactic*` subclasses under `Tactics/Custom/`. ⚠️ `Tactics.EarlyStartPatch` / `Tactics.CampaignMissionComponentPatch` are referenced by fully-qualified name in `RBMAIPatcher.FirstPatch`, so they must stay nested in `partial class Tactics`.
- `AiModule/Agents/` — `partial class AgentAi` patches (`AgentStats`, `CombatFixes`, `Weather`, `TickPatches`).
- `AiModule/Frontline/` — the Frontline system (everything from the old `Frontline.cs`): `FrontlineDecision.cs` (the `partial class Frontline` decision AI), `FrontlinePositioning.cs` (`Frontline.OverrideFormation`), `FormationMovement.cs` (HumanAIComponent movement gates), `AgentPanicFix.cs`. ⚠️ Load-bearing worker-thread crash-safety code — move verbatim, never restructure the gates.
- `AiModule/Formations/` — general formation-grid safety patches: `FormationGridGuards.cs` (`OverrideLineFormation`, from the old `Behaviours.cs`). ⚠️ Also load-bearing worker-thread crash-safety — move verbatim.
- `AiModule/Orders/` — movement-order patches. `AiModule/UI/` — formation-marker patches. `AiModule/Siege/` (+ `SiegeArcherPoints/`), `AiModule/Spawning/`, `AiModule/Misc/`.
- `Utilities/` (project root) — `public static partial class Utilities` split into `Utilities.Formations.cs` / `.Targeting.cs` / `.Geometry.cs` / `.Combat.cs`.

**RBMCombat** — Combat mechanics overhaul. Patcher entry: `RBMCombatPatcher.DoPatching` (`PatchAll`). References RBMConfig. As of 2026-07-19 the module was reorganized from a handful of multi-thousand-line monoliths (DamageRework/ArmorRework/MagnitudeChanges/RangedRework/CampaignChanges + a 1577-line Utilities) into subsystem folders under `CombatModule/`. Each monolith was split with `partial class` (and `partial static class` for `Utilities`) so no call sites changed; namespace stays flat `RBMCombat`; `PatchAll` finds patches by attribute so file location is cosmetic. The old-style csproj lists every file with an explicit `<Compile Include>` — **update it when adding/moving files**. Layout under `CombatModule/`:
- `Damage/` — `partial class DamageRework` split into `.Core.cs` (the `ComputeBlowDamage` rewrite + nested shield helper/patch), `.Blows.cs`, `.HitReaction.cs`, `.Entities.cs`. ⚠️ two DISTINCT `GetAttackCollisionResultsPatch` classes coexist (a nested-private one in `.Entities.cs` and a separate top-level one in the same file) — the name only disambiguates by nesting, keep them in separate scopes.
- `Armor/` — `partial class ArmorRework` split into `.Human.cs` / `.Horse.cs` / `.Dispatch.cs` (the public `GetBaseArmorEffectivenessForBodyPartRBM` / `GetArmorMaterialForBodyPartRBM` dispatchers DamageRework calls); `ItemModifierPatches.cs` holds the two top-level `ModifyArmor`/`ModifyModifyDamage` patches.
- `Magnitude/` — `partial class MagnitudeChanges` (static): `.Core.cs` (shared fields), `.Melee.cs`, `.Thrust.cs`, `.Missile.cs`, and `Tooltips/` (`.WeaponTooltip.cs` / `.ArmorTooltip.cs` / `.StatCalcs.cs`).
- `Ranged/` — `partial class RangedRework`: `.State.cs` (shared dictionaries + reflection cache), `.EquipWield.cs`, `.MissileSpeed.cs`, `.Reload.cs`, `.Siege.cs`, `.Collision.cs`; plus `RangedWeaponStats.cs` (was `RangedAmmoCombo.cs`) and `RealisticWeaponCollision.cs`. NOTE: `OverrideSetAiRelatedProperties` (reload) was promoted out of the siege patch into `.Reload.cs`.
- `Horse/` — `partial class HorseChanges`: `.MountStats.cs` / `.MountedCombat.cs`.
- `Campaign/` — `partial class CampaignChanges`: `.Survival.cs` / `.Xp.cs` / `.TrainingField.cs` / `.TroopPower.cs` / `.Spawn.cs`. `OverrideDefaultMilitaryPowerModel` (in `.TroopPower.cs`) keeps only its two XP-power helpers; the morale/spawn/equipment patches were promoted into `.Spawn.cs`.
- `Items/` — `partial class ItemValuesTiers`: `.Pricing.cs` / `.Tiers.cs`.
- `Diagnostics/` — `BattleHitLog.cs` (sink) + `BattleHitLogic.cs` (producer `MissionLogic`).
- `UI/PlayerArmorStatus/` — `PlayerArmorStatus.cs` (the `MissionLogic`, renamed from `PlayerArmorStatusLogic.cs`) + `PlayerArmorStatusVM.cs`.
- `RBMCombatPatcher.cs` — bootstrap, at `CombatModule/` root.
- `Utilities/` (project root) — `public static partial class Utilities` split into `.Collision.cs` / `.Physics.cs` / `.Skill.cs` / `.Ranged.cs` / `.ArmorDurability.cs` / `.Damage.cs` / `.VisualStats.cs` / `.WeaponProps.cs` / `.Config.cs` (root `Utilities.cs` holds the shared tuning fields).

**RBMConfig** — Configuration system with no project dependencies. Static fields in `RBMConfig.RBMConfig` loaded from user XML config. Includes Gauntlet-based in-game settings UI (`RBMConfigScreen`/`RBMConfigViewModel`).

**RBMTournament** — Optional tournament mode enhancements. No project dependencies.

**RBMCampaign** — Campaign-layer economy overhaul (the "spoils" system). Patcher entry: `RBMCampaignPatcher.DoPatching` (calls `PatchAll` + registers the party-screen widget). References RBMConfig. Unlike the combat modules, most logic lives in `CampaignBehavior` subclasses (added in `OnGameStart` for `Campaign` sessions), not just Harmony patches. Organized into folders (namespace stays flat `RBMCampaign`; the old-style csproj lists every file with an explicit `<Compile Include>` — **update it when adding/moving files**):
- `Spoils/` — the core system. `SpoilsPool` (a `partial static class` split across several files) is a per-troop-**stack** purse in gold (1 spoils point = 1 gold), keyed by `party.Id + "#" + character.StringId`, persisted via `SyncData` key `RBM_troopSpoilsGold`. Fills from battle loot, plunder (raided villages / stormed towns), and a wage fraction; drains on upgrades, food, and carousing; surplus over a cap spills back to the owner's gold. `RBMSpoilsCampaignBehavior` subscribes the campaign events.
- `Upkeep/` — `TroopUpkeep` (settlement food buying, carousing) + `RBMTroopUpkeepCampaignBehavior`.
- `Upgrades/` — gold-cost/tooltip patches plus reimplemented AI (`UpgradeReadyTroops`) and player-side (`PartyScreenLogic.UpgradeTroop`) upgrade paths.
- `UI/` — `RBMTroopSpoilsBarWidget` (a `FillBarVerticalWidget`) injected into the native party-screen prefab.
- `Diagnostics/SpoilsLog.cs` — file logger under `<configFolder>/logs/campaign/`, gated by `spoilsLoggingEnabled`.

### Dependency Graph
```
RBM → RBMAI → RBMConfig
RBM → RBMCampaign → RBMConfig
RBMCombat → RBMConfig
RBMTournament (standalone)
```

### Harmony Patching Pattern

All game modifications use Harmony `[HarmonyPatch]` attributes on static inner classes with `Prefix`, `Postfix`, or `Finalizer` methods. Patches target `TaleWorlds.MountAndBlade` types via reflection. Each module has a dedicated `Harmony` instance (`com.rbmai`, `com.rbmcombat`, `com.rbmt`, `com.rbmmain`, `com.rbmcampaign`) enabling selective enable/disable.

The patching lifecycle flows: `SubModule.OnSubModuleLoad()` → loads config → `RegisterSubModuleTypes()` / `OnGameStart()` → `ApplyHarmonyPatches()` which conditionally patches each module.

### Configuration

Settings are static fields on `RBMConfig.RBMConfig`, persisted to user XML at `Utilities.GetConfigFilePath()`. Key toggles: `rbmAiEnabled`, `rbmCombatEnabled`, `rbmTournamentEnabled`, `rbmCampaignEnabled`, `postureEnabled`. Each toggle gates whether the corresponding Harmony patches are applied. RBMCampaign also reads tuning multipliers (e.g. `troopUpgradeCostMultiplier`, where `0` disables the spoils system entirely) — see `RBMConfigViewModel` for the full set.

### XML Data Files

`RBMXML/` contains 30+ XML files loaded by Bannerlord's XML system (registered in `SubModule.xml`). These modify items (weapons, armor, horses), crafting pieces, siege engines, NPC characters, and weapon descriptions. `XmlLoadingPatches.cs` handles XML merging/preprocessing.

`RBM_WS_XML/` contains compatibility files for the War Sails (Naval) DLC.

### Large Utility Files

`Utilities.cs` exists in RBMAI (~120KB), RBMCombat (~88KB), and RBMConfig (~17KB). These contain extensive helper functions for combat math, physics calculations, and config management. They are not shared — each module has its own.

## Key Conventions

- Harmony patch classes are static inner classes within files named after their domain (e.g., `DamageRework.cs` contains damage-related patches)
- Config values use `"1"`/`"0"` strings for booleans in XML
- Module compatibility checks happen in `OnBeforeInitialModuleScreenSetAsRoot()`
- MissionBehaviors (PostureLogic, PostureVisualLogic, PlayerArmorStatus, SiegeArcherPoints) are added conditionally in `OnMissionBehaviorInitialize()`
- Localization strings use `{=TAG}Text` format via `TextObject`

## Bannerlord documentation online
- https://moddocs.bannerlord.com/
- https://docs.bannerlordmodding.com/

##
- never add Claude as co-author in commits
- nevery directly copy files to output RBM folder, always place them in project files and if needed make post or pre build commands

## Agentic Routing Policy
- **Rule:** For tasks touching more than 3 files or requiring deep codebase exploration, you MUST delegate the research or modifications to a subagent.
- **Built-in Agents:** Use `@agent-explore` for read-only audits and `@agent-plan` for research and context gathering. 
- **Main Context:** Do not bloat the main session with intermediate file scans or read operations. The main session should only be used for high-level orchestration.
