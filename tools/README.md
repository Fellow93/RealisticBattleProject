# tools/

## TroopPerkEditor/

A local page (`index.html`) for editing `RBMXML/rbm_troop_perks.xml`: every troop and perk, the perks'
personal effects, export/import of the XML. Generate its data first with
`powershell -ExecutionPolicy Bypass -File tools\TroopPerkEditor\Build-TroopPerkData.ps1` (reads
`decompiled/` and the game's module XML; the output is gitignored). See `TroopPerkEditor/README.md`.

## TroopLoadoutEditor/

A local page (`index.html`) for editing troops' level, skills and equipment rosters in RBM's troop overhaul files
(`RBMXML/RBMCombat_unit_overhaul.xml`, `RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml`): every troop the game loads
(merged across modules as the game does), an item picker with stats, skill hints, bulk edits, and an export that
splices only the edited troops into the file (a troop not yet in an RBM file is appended as a full copy). Generate
its data first with `powershell -ExecutionPolicy Bypass -File tools\TroopLoadoutEditor\Build-TroopLoadoutData.ps1`
(the output is gitignored). See `TroopLoadoutEditor/README.md`, which also explains how
`RBM/XmlLoadingPatches.cs` applies these files.

## ItemEditor/

A local page (`index.html`) for editing equipment items in RBM's item files (`RBMXML/RBMCombat_*` armors, shields,
horses, ranged weapons and ammo, crafted weapons, trade goods, and `RBM_WS_XML/`): every item the game loads (merged
as the game does), each value next to the vanilla one, RBM's tier and price preview, a comparison table for balancing,
bulk edits, problem checks against `Items.xsd` and the game's parsers, and a diff-based export that patches only the
changed attributes in every RBM file defining the item (both ranged twins) or appends a full copy of a native item.
Generate its data first with `powershell -ExecutionPolicy Bypass -File tools\ItemEditor\Build-ItemData.ps1` (the
output is gitignored). It shares `TroopLoadoutEditor/troop-loadout-core.js`'s XML text helpers. See
`ItemEditor/README.md`, which also explains how RBM replaces items and where the tier and price formulas live.

## check_crafting_coverage.py

Finds smithing piece combinations that no weapon description covers — the cause
of the "game crashes as soon as I pick piece X in the smithy" reports (e.g.
`battania_blade_6`, `mace_handle_25/26` before `fed0cbb6`).

```powershell
python tools\check_crafting_coverage.py
python tools\check_crafting_coverage.py --repo <worktree of an older commit>
```

Requires Python 3 and `lxml` (`pip install lxml`). Exit code 1 if any RBM setup
has an unbuildable combination.

**Why it happens:** a crafted item gets one weapon per `WeaponDescription` whose
`AvailablePieces` lists *all* of its pieces. RBM's
`RBMCombat_weapon_descriptions.xml` is appended, so its copy of each description
replaces vanilla's list, while template pieces only accumulate. A piece a game
patch or DLC adds after RBM's list was written stays selectable but uncovered:
the item gets zero weapons and the smithy's `RefreshStats` throws.

**What it does:** rebuilds the merged `WeaponDescriptions` / `CraftingTemplates`
the way `MBObjectManager.CreateMergedXmlFile` does (per module: its XSLT, then
its XML; Native → NavalDLC → RBM → RBM_WS), for Vanilla / RBM / War Sails /
War Sails + RBM_WS / War Sails without RBM_WS, both from the XML alone and with
the runtime heal in `RBM/CraftingCoveragePatches.cs` applied. It reports pieces
that crash with *any* other parts, then the remaining pairwise conflicts.

"RBM + War Sails, no RBM_WS" is expected to break without healing (RBM's own XML
never lists War Sails pieces; RBM_WS's XSLT does), so it only fails the check
once healed. Every other RBM setup must be clean from the XML alone.

Run it after a game patch / DLC update and after touching
`RBMXML/RBMCombat_weapon_descriptions.xml`, `RBMXML/RBMCombat_no_bastard_axes.xml`
or the `RBM_WS_XML/*.xslt` crafting files. It reads `Native/` and `NavalDLC/`
from the Modules folder this repo sits in (`--modules` to override).

## Decompile-Bannerlord.ps1

Decompiles the Bannerlord game assemblies into `decompiled/` so the real method
bodies can be read and grepped locally instead of guessing at them.

```powershell
.\tools\Decompile-Bannerlord.ps1            # decompile new/changed assemblies
.\tools\Decompile-Bannerlord.ps1 -Check     # report only; exit 1 if out of date
.\tools\Decompile-Bannerlord.ps1 -Scope Full
.\tools\Decompile-Bannerlord.ps1 -Force     # rebuild everything
```

Requires `ilspycmd` (`dotnet tool install -g ilspycmd`).

### What is and isn't committed

| Path | Git |
|---|---|
| `decompiled/` | **ignored** — derived output, 42MB, TaleWorlds' copyrighted code |
| `tools/bannerlord-assemblies.lock.json` | **committed** — SHA256 of every source DLL |
| `tools/bannerlord-types.lock.txt` | **committed** — SHA256 of every decompiled type (~5,400 lines) |

### Detecting a game update

The lock file is the whole point. After Bannerlord patches:

```powershell
.\tools\Decompile-Bannerlord.ps1 -Check
```

It hashes each source DLL and lists anything `new` / `changed` / `removed`, plus
a loud warning if `gameVersion` moved. Run without `-Check` to re-decompile just
those assemblies, then:

```
git diff tools/bannerlord-assemblies.lock.json
```

The diff is a precise list of which assemblies TaleWorlds actually touched —
i.e. exactly where RBM's Harmony patches are at risk of breaking. Assemblies
whose hash is unchanged cannot have changed behaviour, so they need no review.

### Narrowing it down to individual types

`bannerlord-types.lock.txt` hashes every decompiled `.cs` file (ilspycmd emits
one file per type), so it turns "TaleWorlds.CampaignSystem changed" into the
actual list of types that moved:

```
git diff tools/bannerlord-types.lock.txt
```

The decompile run also prints this directly (first 40, then a pointer to the
diff):

```
Type-level changes: 2 changed, 1 added, 1 removed
  [changed] TaleWorlds.CampaignSystem/.../DefaultPartyWageModel.cs
```

Caveats worth knowing:

- It names *which* types changed, not *how*. `decompiled/` is gitignored, so
  there is no committed before/after source to diff. To see the actual edit,
  stash the old `decompiled/<Assembly>/` folder before re-running.
- Only `.cs` is hashed. The generated `.csproj` gets a fresh GUID per run and
  would churn every invocation.
- Lines are sorted **ordinally** (not PowerShell's culture-aware default) so the
  ordering is stable across machines and locales, and the file works with
  `sort`/`comm`/`grep`.
- `-Check` is assembly-level only — type hashes can't be known without actually
  decompiling.

### Scopes

`Core` (default, ~36 assemblies) is the single-player surface RBM patches:
TaleWorlds.CampaignSystem / Core / Engine / MountAndBlade / GauntletUI\*,
SandBox\*, StoryMode\*, and NavalDLC\* (for the RBM_WS War Sails compat layer).

`Full` adds everything else TaleWorlds-managed, still skipping native P/Invoke
shims, `*.AutoGenerated*` codegen, and the Diamond/online backend.

A decompile failure is **not** written to the lock file, so the next run retries
it rather than silently treating it as up to date.
