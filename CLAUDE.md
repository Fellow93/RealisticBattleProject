# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
It is loaded into every session, so keep it SHORT: detailed design notes, user rules and thresholds for
each module live in `docs/modules/` (see "Module docs" below). New design notes go there, never here.

## Project Overview

Realistic Battle Mod (RBM) for Mount & Blade II: Bannerlord. A comprehensive combat overhaul mod that rewrites damage calculations, armor mechanics, AI behavior, and adds a stamina/posture system. Built on Harmony 2.4.2 for non-invasive runtime patching of game methods.

Current version: v4.8.1 (`RBMXML/SubModule.xml`). Targets Bannerlord v1.5.4+ (per the
`DependedModules` entries there); currently developed against v1.5.6 (its perk API — `PerkHelper` environment as the
trailing optional argument, `TryGetPerkEffectBonus`, no `BattleEnvironment.None`, no environment overloads of
`GetPerkValue`/`HasPerk` — does not compile against ≤ v1.5.5).

## Build

**Solution:** `RealisticBattle.sln` — .NET Framework 4.7.2, x64 target.

Build with Visual Studio (2017+) or MSBuild:
```
msbuild RealisticBattle.sln /p:Configuration=Release /p:Platform="Any CPU"
```

All 8 projects output DLLs to `../../RBM/bin/Win64_Shipping_Client/` (relative to each project folder, resolving to the `RBM/bin/` directory within the Bannerlord Modules tree).

Single-project build (from memory — MSBuild path is under VS `18\Community`, and `Platform` must be quoted with its space):
```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RealisticBattle.sln /p:Configuration=Debug "/p:Platform=Any CPU" /t:RBM /verbosity:minimal
```

**Debug launch:** Bannerlord.exe with `/singleplayer _MODULES_*Native*SandBoxCore*SandBox*StoryMode*CustomBattle*RBM*_MODULES_`

Testing is mostly manual, in game. The one exception is `RBMCampaign.Tests/` (NUnit, net472, not in the sln; build/run commands in its README). It holds executable specs for RBMCampaign's game-free formulas: market price curve, spoils carried on upgrade, wage table, battle-loot notice/carry/peer split, patrol boost/size/keep+raise reserves/buy check/garrison floor/pot average (settings and random rolls passed in as parameters; tests reach `internal` members through `InternalsVisibleTo`). When you change one of those formulas, run it. To add a spec for a campaign rule, first move the rule's math into a pure function that takes plain values. `RBMAI.Tests/` is the same setup for RBMAI's game-free battle-AI rules (army composition shares / tokens / ramps / main-body verdict via `ArmyComposition.Derive` / `ComputeRamps` / `LeadsVerdict`, `EnemyMotion.TryStepVelocity`); run it when you change those. After a test battle, run `tools/BattleLogCheck/Check-BattleLogs.ps1` on its AI + terrain logs.

## Module docs — READ BEFORE WORKING IN A MODULE

Before editing (or answering detailed questions about) a module, read its doc in `docs/modules/`. They hold the
file maps, every USER RULE, thresholds, threading notes and log formats. Read only the part you need (grep first).
When you change behaviour, update the matching doc (not this file).

| Area | Doc |
|---|---|
| RBMAI overview, layout, MissionStartReset, robustness guard, agents, voices, Frontline, decision trace | `docs/modules/RBMAI.md` |
| Tactic selection, TerrainTacticFactors, flank plan, archer spots, cavalry ground, choke hold, battle position, reserve plug, hedgehog, army scale, attacker approach, shared pieces (PlanSlot, ReplanJob, TerrainLog, re-pick pipeline, CachedMovePoint, HoldRecall, EnemyMotion) | `docs/modules/RBMAI-Tactics.md` |
| Behavior passes: polish pass, gap fixes 1–2, stand vs mounted, support fire, FormationGeometry | `docs/modules/RBMAI-Behaviors.md` |
| Reaction monitor (R1–R7) + gap fixes 3 | `docs/modules/RBMAI-Reactions.md` |
| Army composition (token/no infantry, mounted-heavy, archer stand, escorts, wear-down) | `docs/modules/RBMAI-Composition.md` |
| ChargeGuard / orders, formation grid guards, diagnostics overlays, `BattleTerrain.Current` API | `docs/modules/RBMAI-Terrain.md` |
| Deep water nav blocker (walls, forced exit, slot + steering patches) | `docs/modules/RBMAI-DeepWater.md` |
| RBMStance (posture, stamina, kick/bash, hit stop) | `docs/modules/RBMStance.md` |
| RBMCombat | `docs/modules/RBMCombat.md` |
| RBMConfig, settings UI, ForcedSettings, shared damage math (`RBMConfig/Shared/`) | `docs/modules/RBMConfig.md` |
| RBMAutoResolve | `docs/modules/RBMAutoResolve.md` (+ `RBMAutoResolve/AUTO_RESOLVE.md`) |
| RBMCampaign | `docs/modules/RBMCampaign.md` (+ `RBMCampaign/ARCHITECTURE.md`, `docs/economy-*.md`) |
| Troop / item / crafting / upgrade-tree editors in `tools/` | `docs/modules/Tools.md` |
| Terrain + AI log line formats | `docs/terrain-log-format.md` |
| Gauntlet UI | `docs/BannerlordGauntletUI.md` |

## Architecture

### Module Structure (8 C# projects)

All old-style csprojs list every file with an explicit `<Compile Include>` — **update the csproj when adding/moving files**. Namespaces are flat per project (`RBMAI`, `RBMCombat`, …); large classes are split with `partial class`, and patches are found by attribute (`PatchAll`), so file location is cosmetic.

**RBM** — Main coordinator. Entry point: `RBM.SubModule` (extends `MBSubModuleBase`). Manages Harmony instance lifecycle, conditionally patches/unpatches each subsystem based on config toggles. References RBMAI, RBMCombat, RBMStance, RBMTournament, RBMCampaign and RBMAutoResolve.

**RBMAI** — Battle AI (formation behaviors, tactics, terrain-aware plans, reaction monitor, Frontline, deep water). Patcher `RBMAiPatcher` (class-by-class PatchAll, only from `RBMAIPatchLogic.EarlyStart`). Always-on rules:
- ⚠️ The team/formation AI ticks on an ASYNC AI THREAD: there, read cached formation data only — no engine/spatial query, no MBRandom, no allocation-heavy work; WorldPositions come from cached fields (copy + one `SetVec2MT`). Terrain is computed on the main thread / a background Task and published as immutable snapshots.
- ⚠️ Every new Agent/Formation-keyed static must be cleared in `AiModule/Misc/MissionStartReset.cs`.
- ⚠️ `AiModule/Frontline/` and `AiModule/Formations/` are load-bearing worker-thread crash-safety code — move verbatim, never restructure the gates.
- ⚠️ Beware JIT-inlined targets (`Mission.BatchFormationUnitPositions`, `Formation.SetFacingOrder`, `Agent.SetFormationIntegrityData`): a patch on them never runs.
- ⚠️ `Formation.TargetFormation` is always null for AI formations — use `OverrideBehaviorCharge.ChargeTargetOf(f)`.
- New catches on the AI tick call `AiTickGuard.Report`, never swallow silently.
- Never change player formations, except the deliberate ChargeGuard exception (see RBMAI-Terrain.md).

**RBMStance** — posture/stamina, AI kick/bash, combat slow motion. Gated only on `rbmStanceEnabled` (independent of RBM AI), own Harmony `com.rbmstance`. Never references RBMAI. Agent-keyed statics are cleared in `StanceReset.cs`. `AgentStances.values` must stay concurrent.

**RBMCombat** — damage, armor, magnitude, ranged, horse, campaign-side combat tweaks, troop perks. ⚠️ Two distinct `GetAttackCollisionResultsPatch` classes coexist in `Damage/DamageRework.Entities.cs` — keep them in separate scopes.

**RBMConfig** — config store (`RBMConfig.RBMConfig` statics), Gauntlet settings screen, and the shared damage math in `RBMConfig/Shared/` used by every module. **Retune damage there, never re-copy it into a module.** No project dependencies.

**RBMTournament** — Optional tournament mode enhancements. No project dependencies. `Tournament/` via `internal partial class RBMTournament`: `.Core.cs` (shared `calculatePlayerTournamentTier`), `.FightSimulation.cs`, `.Participants.cs`, `.Prizes.cs`.

**RBMAutoResolve** — equipment-aware campaign battle simulation. Gated only on `simulationEquipmentEnabled`, own Harmony `com.rbmautoresolve`. Never widen `TroopPerks.AppliesInMission`.

**RBMCampaign** — spoils troop economy, settlement wealth ledger, production chain, patrols, mercenary companies, strategic troop power. Mostly `CampaignBehavior`s added in `OnGameStart`. Every log writes through `BufferedLogWriter` + `LogRetention` (never per-line `File.AppendAllText` or `Debug.Print` copies). ⚠️ Mercenary-company patches must be no-ops for non-company clans.

### Dependency Graph
```
RBM → RBMAI → RBMStance → RBMConfig
RBM → RBMCampaign → RBMAutoResolve → RBMConfig
RBM → RBMAutoResolve
RBM → RBMStance
RBM → RBMCombat → RBMStance
RBMAI → RBMConfig
RBMCampaign → RBMConfig
RBMCombat → RBMConfig
RBMTournament (standalone)
```

### Harmony Patching Pattern

All game modifications use Harmony `[HarmonyPatch]` attributes on static inner classes with `Prefix`, `Postfix`, or `Finalizer` methods. Patches target `TaleWorlds.MountAndBlade` types via reflection. Each module has a dedicated `Harmony` instance (`com.rbmai`, `com.rbmcombat`, `com.rbmstance`, `com.rbmt`, `com.rbmmain`, `com.rbmcampaign`, `com.rbmautoresolve`) enabling selective enable/disable.

The patching lifecycle flows: `SubModule.OnSubModuleLoad()` → loads config → `RegisterSubModuleTypes()` / `OnGameStart()` → `ApplyHarmonyPatches()` which conditionally patches each module.

### Configuration

Settings are static fields on `RBMConfig.RBMConfig`, persisted to user XML. Module toggles: `rbmAiEnabled`, `rbmCombatEnabled`, `rbmStanceEnabled`, `rbmTournamentEnabled`, `rbmCampaignEnabled`, `simulationEquipmentEnabled`. To change a saved setting for every player in a release, add a `ForcedSettings` entry (details in `docs/modules/RBMConfig.md`); never bump `CONFIG_VERSION` for that.

### XML Data Files

`RBMXML/` contains 30+ XML files loaded by Bannerlord's XML system (registered in `SubModule.xml`). These modify items (weapons, armor, horses), crafting pieces, siege engines, NPC characters, and weapon descriptions. `XmlLoadingPatches.cs` handles XML merging/preprocessing.

`RBM_WS_XML/` contains compatibility files for the War Sails (Naval) DLC. Editors for troops, upgrade trees, items and crafting: `docs/modules/Tools.md`.

## Key Conventions

- Harmony patch classes are static inner classes within files named after their domain (e.g., `DamageRework.cs` contains damage-related patches)
- Config values use `"1"`/`"0"` strings for booleans in XML
- Module compatibility checks happen in `OnBeforeInitialModuleScreenSetAsRoot()`
- MissionBehaviors (StanceLogic, StanceVisualLogic, PlayerExhaustionLogic, HitStopLogic under `rbmStanceEnabled`; PlayerArmorStatus, SiegeArcherPoints) are added conditionally in `OnMissionBehaviorInitialize()`
- Localization strings use `{=TAG}Text` format via `TextObject`

## Decompiled game sources

**The full TaleWorlds source is already on disk at `decompiled/<AssemblyName>/`.
Read and grep it directly. Do NOT run `ilspycmd` to decompile a type on demand —
that work is already done, and re-decompiling per-question is pure waste.**

36 assemblies, ~5,400 `.cs` files, one file per type, foldered by namespace —
e.g. `decompiled/TaleWorlds.CampaignSystem/TaleWorlds.CampaignSystem.GameComponents/DefaultPartyWageModel.cs`.
Covers TaleWorlds.\* (CampaignSystem, Core, Engine, MountAndBlade, GauntletUI\*, …),
SandBox\*, StoryMode\*, and NavalDLC\*.

Never guess at a TaleWorlds method body or signature — open the file. Grep for a
type name across `decompiled/` to find it; use the Explore agent for broad sweeps
so the main context stays clean.

The folder is **gitignored** (derived, 42MB, TaleWorlds-owned code), so it exists
on this machine but not for a fresh clone. If it is missing or a lookup turns up an
assembly that isn't there, regenerate with `tools\Decompile-Bannerlord.ps1`
(`-Scope Full` widens beyond the single-player set). See `tools/README.md`.

Two manifests **are** committed, and together they localise a game update:

- `tools/bannerlord-assemblies.lock.json` — SHA256 per source DLL. After an
  update, `.\tools\Decompile-Bannerlord.ps1 -Check` lists which assemblies moved.
- `tools/bannerlord-types.lock.txt` — SHA256 per decompiled type. After
  re-running the script, `git diff` on this file names the individual types that
  changed, i.e. exactly where RBM's Harmony patches may have broken.

## Gauntlet UI work

**Any task touching the UI — Gauntlet prefabs (`**/GUI/Prefabs/*.xml`), brushes
(`**/GUI/Brushes/*.xml`), widgets, ViewModels (`[DataSourceProperty]` / `MBBindingList`),
Gauntlet screens/layers/movies, or injecting into native prefabs — MUST first consult
`docs/BannerlordGauntletUI.md`.** Read it before grepping `decompiled/` from scratch.
**When delegating UI research/changes to a subagent, tell it to read
`docs/BannerlordGauntletUI.md` first** and to update that doc if it discovers the
reference is wrong or incomplete.

## Bannerlord documentation online
- https://moddocs.bannerlord.com/
- https://docs.bannerlordmodding.com/

## Rules
- never add Claude as co-author in commits
- never directly copy files to output RBM folder, always place them in project files and if needed make post or pre build commands

## Agentic Routing Policy
- **Rule:** For tasks touching more than 3 files or requiring deep codebase exploration, you MUST delegate the research or modifications to a subagent.
- **Built-in Agents:** Use `@agent-explore` for read-only audits and `@agent-plan` for research and context gathering.
- **Main Context:** Do not bloat the main session with intermediate file scans or read operations. The main session should only be used for high-level orchestration.
- **Module docs:** when delegating work in a module, tell the subagent which `docs/modules/` file to read first.
