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

**RBMAI** — AI and stamina/posture system. Patcher entry: `RBMAiPatcher`. Contains:
- `PostureLogic.cs` — Core stamina system (largest logic file ~111KB). Calculates posture based on weapon weight, length, relative speed.
- `AgentAi.cs` — Agent AI property overrides
- `Frontline.cs` — Formation front-line management
- `Tactics.cs` — Tactical behavior and damage tracking
- `RbmBehaviors/` and `RbmTactics/` — Custom behavior/tactic subclasses
- `SiegeArcherPoints/` — Siege positioning logic
- References RBMConfig.

**RBMCombat** — Combat mechanics overhaul. Patcher entry: `RBMCombatPatcher`. Contains:
- `DamageRework.cs` — Complete damage calculation rewrite (~84KB)
- `ArmorRework.cs` — Armor penetration and absorption
- `MagnitudeChanges.cs` — Weapon property modifications (~98KB)
- `RangedRework.cs` — Ranged combat mechanics (~62KB)
- `HorseChanges.cs` — Mount mechanics
- References RBMConfig.

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
