# Crafting editor

A local page for editing crafting pieces (`CraftingPiece`) and crafting templates (`CraftingTemplate`) in RBM's crafting
files under `RBMXML/` and `RBM_WS_XML/`. It lists every piece and template the game loads in a campaign, shows each value
next to the file's and the vanilla one, recomputes the stats of every crafted weapon that uses a piece or template (the
game's crafting math with RBM's patches), has a design sandbox, and exports the patched file(s), touching only the edited
entries. Crafted weapon designs (`CraftedItem`) stay in the item editor (`../ItemEditor/`); this page only reads them.

## 1. Generate the data

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\CraftingEditor\Build-CraftingData.ps1
```

This writes `crafting-data.js` next to the page (about 3 MB, 20 seconds to build). It reads:

- **Crafting pieces, templates and weapon descriptions**: the `CraftingPieces`, `CraftingTemplates` and
  `WeaponDescriptions` XML each module's `SubModule.xml` registers, in load order Native, SandBoxCore, SandBox,
  StoryMode, CustomBattle, NavalDLC (if installed), then RBM's own `RBMXML/` and `RBM_WS_XML/` from this repo, merged the
  way the game merges them (see below): per module its XSLT transforms over the XML merged so far, then its XML; plain
  files through `MBObjectManager.MergeElements`, RBM's files appended. The data keeps **every definition of an id in
  load order** (the exact text, re-formatted only when a plain file or an XSLT changed it, with the pre-XSLT text kept
  for copies), the piece ids an XSLT added to an RBM template definition, and, for templates and weapon descriptions,
  the same pipeline without RBM ("vanilla"). Multiplayer files (`mp_crafting_pieces.xml`, `mpitems.xml`) are read but
  never change a campaign definition; their entries are marked "not in campaign".
- **Crafted items**: every `CraftedItem` of all modules' `Items` XML, RBM's item files replacing by id as the item
  editor describes, with the definition RBM replaced ("vanilla").
- **The full text of every RBM crafting file** (7 with War Sails), with its BOM and line ends, which the page splices on
  export.
- **The attribute vocabulary** of `XmlSchemas/CraftingPieces.xsd` and `CraftingTemplates.xsd`, and the enums the game
  parses with (`CraftingPiece.PieceTypes`, `DamageTypes`, `WeaponFlags`, `ItemFlags`, `ItemObject.ItemTypeEnum`,
  `WeaponClass`, `CraftingMaterials`, `CraftingTemplate.CraftingStatTypes`) from `decompiled/`. If `decompiled/` is
  missing, run `tools\Decompile-Bannerlord.ps1` first.
- **Lookups**: cultures, item modifier groups, item holsters.

Assumed RBM settings: combat on (every RBM crafting file carries `RBM_COMBAT_XML_TAG`), campaign on (for the price), the
RBM config defaults. The script warns about an id defined twice in one file, a piece defined in several RBM files, an
RBM crafting file without `RBM_XML_TAG`, and an XSLT that changes more than usable/available pieces.

Re-run it after a game update, after changing RBM's crafting XML by hand, and after saving an export. The game's Modules
folder is taken to be the one this repo sits in; pass `-ModulesRoot <path>` otherwise. `crafting-data.js` is gitignored:
it contains TaleWorlds text.

## 2. Edit

Open `index.html` in a browser (double-click it; no server needed). It loads `crafting-data.js`,
`crafting-editor-core.js` (the model, the replay of the game's deserializers, the calculator, checks and export
splicing, also usable from node) and `../TroopLoadoutEditor/troop-loadout-core.js` (shared XML text helpers).

- **Header**: the weapon **skill** and the **armor** value RBM's tooltip numbers are computed for, and "vanilla with RBM
  formulas" (off: the vanilla column is the plain game; on: vanilla pieces/templates with RBM's thrust, tier, price and
  tooltip formulas, to see only what RBM's crafting files change).
- **Left**: pieces or templates (toggle), with search (name or id) and filters: piece type, usable in template (with your
  edits), culture, tier, module of the first definition, source (in an RBM file, not in one, defined in 2+ RBM
  definitions, new), edited, problems, and "not in campaign" (multiplayer pieces, hidden by default). Tick entries
  (checkbox, Ctrl+click, Shift+click for a range) for **bulk** changes: set, change by %, add to or remove any attribute
  of the piece/template or of its `BladeData`, `Swing`, `Thrust`, `BuildData` or `StatContributions`; set the culture.
  "Revert ticked" undoes them.
- **Middle**: the selected entry. Its definitions in load order and RBM files, where the export writes it (or the file a
  copy is appended to, with a target picker), its problems and notes, and for a piece **"As the game reads it"**: length,
  distances, weight, center of mass, inertia, tier, offsets, blade size, swing/thrust type and factor, materials, flags,
  edited | file | vanilla. Then every attribute (enum attributes get a list, `culture` a dropdown of cultures by name,
  references a datalist), each with the file's value and ↺ when edited and the **vanilla** value when it differs;
  `BladeData` (with `Swing` and `Thrust`), `BuildData`, `StatContributions`, `Materials` and `Flags` (WeaponFlags and
  ItemFlags as checkboxes), each addable and removable as a whole. A template has its attributes, `PieceDatas`,
  `WeaponDescriptions`, `StatsData` and **usable pieces** grouped by piece type (✕ to remove, a piece picker filtered by
  type to add; pieces another definition or an XSLT makes usable are shown dashed). "Used by" lists the templates a
  piece is usable in and every campaign crafted item using the entry with its key numbers (edited, then file and vanilla
  when they differ). Last, the element as it will be exported (changed lines highlighted). "Duplicate as new piece" (or
  template) copies the entry, with its current changes, under a new id.
- **Right**: **Compare** — the pieces of the selected piece's type (or the filtered list, or the ticked entries) with
  culture, tier, length, weight, center of mass, swing/thrust factor and type, stack, offset, materials and how many
  crafted items use it; sortable, changed cells marked with the file's value, double-click a number to edit it (for
  templates: item type, usable pieces, usages, build order). **Crafted items** — the items using the selected entry,
  every item an edit affects, or every campaign crafted item, with the calculator's numbers (length, weight, swing
  speed/damage, thrust speed/damage, handling, tier, price, RBM's swing and thrust damage at the header's armor), edited
  in bold with the file's and vanilla values under it; click a row for every usage and RBM's damage tables (armor 0..100).
  **Design sandbox** — pick a template and one usable piece (and scale) per slot, or load a crafted item, and see the
  same results.
- **Problems** (header button, for edited and new entries; the filter covers all): an attribute not in the XSD, an
  invalid enum value (with the game's case rules), a non-numeric number or a fraction where the game uses `int.Parse`,
  a value `Convert.ToBoolean` rejects, unknown cultures, modifier groups, holsters, weapon descriptions or pieces, a
  usable piece of a type the template does not build, a `StatsData` for a description the template does not have, a
  Blade piece without `BladeData`, no length (nor both distances), a duplicate id on a new entry, a missing target file,
  a vanilla start-tag attribute the RBM definition leaves out (reset to the game default), and notes: a child element
  vanilla has and RBM's definition does not (vanilla's is kept), a usable piece removed from RBM's list that vanilla
  still lists (it stays usable).
- **Data warnings** (yellow header button): the generator's warnings, every RBM definition that leaves out a vanilla
  start-tag attribute, and every campaign crafted item that does not build from the files.

Work in progress is kept in the browser's local storage (`rbm-crafting-editor.v1`) as changes against the files, so after
re-running the script they are re-applied to the new data (changes to an element or entry that is gone are dropped, and
the page says so). "Reset to file" drops all of it, "Revert" one entry.

## 3. Save the result

Click **Export…**. Only files with changes are offered; each lists its edited entries (patched in place, appended, or
new), what changed and their problems, with the full patched file. **Download** (or **Save to file…** in browsers that
support it, or Copy) and replace the file in the repository with it. Building the solution copies it into the RBM
module, so do not copy it into the module folder by hand. Then re-run the script so the page starts from the new files.

The export splices text, so everything outside the edited entries stays byte-identical (BOM and CRLF line ends included):

- An entry already in RBM files is patched in **every** RBM definition of its id (a piece defined twice is patched
  twice), and only what changed is written: start-tag attributes in place (quote style and alignment kept), an attribute
  of `BladeData`/`Swing`/`Thrust`/`BuildData`/`StatContributions` in place (the element is created after the other
  single elements when missing), list entries (`Material`, `Flag`, `PieceData`, `WeaponDescription`, `StatData`,
  `UsablePiece`) added after their last sibling and shaped like it, removed, or patched; whole elements and lists added
  or removed when you do so.
- An entry not yet in an RBM file is appended before the closing root tag as a **full copy** of its last vanilla
  definition (the text before any XSLT, re-indented to the file's style), with the edits applied and a comment naming
  where it came from. Default target: a War Sails piece goes to `RBM_WS_XML/RBMCombat_WS_crafting_pieces.xml`; another
  piece to the RBM piece file whose pieces are used by the same templates most often; a template to
  `RBMXML/RBMCombat_no_bastard_axes.xml`. Pick another file in the middle pane.
- A new entry (duplicate) is appended the same way, to the source's RBM file (or the source's default target).

New entries (a new `PieceData`, `UsablePiece`, ...) go at the end of their list. The order of `PieceDatas` matters to the
game (pieces of the same build-order sign stack in list order); the editor does not reorder lists. A new piece is only
craftable once a template lists it; RBM's weapon descriptions do not list it, but `RBM/CraftingCoveragePatches.cs` adds
it to the template's main description at load (combat on), which the calculator replays. Weapon descriptions themselves
(`RBMXML/RBMCombat_weapon_descriptions.xml`) are not edited here.

## How RBM applies crafting files (RBM/XmlLoadingPatches.cs, MergeTwoXmlsPatch)

- `MergeTwoXmlsPatch` (`RBM/XmlLoadingPatches.cs:180-397`) only **removes** earlier `ItemModifier`, `CraftedItem`, `Item`
  and `NPCCharacter` elements whose id an `RBM_XML_TAG` file defines again (`:266-380`); for every other element it just
  **appends** the RBM file's elements to the merged XML (`:390`). So a `CraftingPiece`, `CraftingTemplate` or
  `WeaponDescription` RBM defines again is in the merged XML **twice**, and `MBObjectManager.LoadXml`
  (`decompiled/TaleWorlds.ObjectSystem/.../MBObjectManager.cs:1383-1391`) deserializes the same object twice, vanilla's
  definition first (`GetPresumedObject` returns the existing object). There is no full replacement:
  - `CraftingPiece.Deserialize` (`decompiled/TaleWorlds.Core/TaleWorlds.Core/CraftingPiece.cs:153-297`) sets **every
    start-tag attribute again** (absent = the default: appearance 0.5, weight 0, center_of_mass 0.5, tier 1, flags
    false, excluded_item_usage_features "", item_holster_pos_shift 0, full_scale true for guards/pommels), but a child
    element (`BladeData`, `BuildData`, `StatContributions`, `Materials`, `Flags`) only changes what it holds **when
    present**: a missing one keeps vanilla's. `BladeData` is rebuilt whole when present (`blade_length` defaults to the
    piece's length, `blade_width` to 0.15 + 0.3 × blade length). A `<CraftingTemplates>` child adds the piece to those
    templates.
  - `CraftingTemplate.Deserialize` (`CraftingTemplate.cs:144-254`) sets the start-tag attributes again except
    `modifier_group`, which only changes when present; `PieceDatas` and `WeaponDescriptions` replace vanilla's when
    present (a new `WeaponDescriptions` resets the stat data); **`UsablePieces` accumulate** (`:209-218`, `Pieces` is never
    cleared): RBM cannot remove a piece vanilla lists.
  - `WeaponDescription.Deserialize` (`WeaponDescription.cs:26-63`): `WeaponFlags` accumulate (`|=`), `AvailablePieces`
    is replaced by the last definition that has one (RBM's).
- File tags gate loading (`:198-253`): `RBM_COMBAT_XML_TAG` needs RBM combat on, which every crafting file carries.
  War Sails' own XSLTs (`NavalDLC/ModuleData/XSLT/NavalDLC_Native_CraftingTemplates.xslt`, `...WeaponDescriptions.xslt`)
  add the naval pieces to vanilla templates/descriptions before RBM's files load; RBM_WS's
  `RBMCombat_WS_CraftingTemplates.xslt` and `RBMCombat_WS_WeaponDescriptions.xslt` run after them, over the merged XML
  that already holds RBM's copies, and add the naval pieces to **every** matching definition (vanilla's and RBM's). With
  combat off, `CreateMergedXmlFilePatch` (`:154-178`) blanks every `RBMCombat_*` XSLT.
- Load order: RBMXML's files in `SubModule.xml` order, then RBM_WS_XML's (`RBMCombat_WS_crafting_pieces.xml`, then the
  two XSLTs). RBM's crafting files: `RBMCombat_couched_lances.xml`, `RBMCombat_sword_pieces.xml` (empty),
  `RBMCombat_sword_blades.xml`, `RBMCombat_mace_pieces.xml`, `RBMCombat_axe_pieces.xml` (CraftingPieces),
  `RBMCombat_no_bastard_axes.xml` (CraftingTemplates), `RBM_WS_XML/RBMCombat_WS_crafting_pieces.xml`.
- After loading, `RBM/CraftingCoveragePatches.cs:46-102` (combat on) adds every template piece no weapon description
  lists to the description covering most of the template's pieces.

## The calculator

`createState(data, mode, work)` replays the deserializers above on the definitions of each id (`'file'`: the files;
`'cur'`: with your edits applied to every RBM definition, or your copy appended; `'van'`: without RBM's files), then
`calcDesign` builds a crafted item the way `Crafting.CraftedItemGenerationHelper.GenerateCraftedItem` does. Ported
from `decompiled/TaleWorlds.Core` (float math in float32 via `Math.fround` where the game uses `float`, doubles where it
uses `double`):

- **Pieces in the design** (`ItemObject.Deserialize`): one per `Type`, scaled by `scale_factor` (`WeaponDesignElement`:
  length, distances, offsets, center of mass × scale; weight × scale, or × scale³ for full-scale pieces). A piece the
  template does not list makes `GenerateCraftedItem` return null: the item is not loaded (reported).
- **Length** (`WeaponDesign.CalculatePivotDistances`, `CalculateWeaponLength`): pivot distances along the template's
  `PieceDatas` (order 0 grips, positive up, negative down), length = max(blade pivot + blade's distance to next, any
  piece's distance to next + offset); `weapon_length` = (int)(round(length, 2) × 100).
- **Weight**: Σ scaled weights, rounded to 0.01. **Usages**: every weapon description of the template whose
  `AvailablePieces` lists all valid pieces (counting as the game does, repeats included), with RBM's coverage heal.
- **Per usage** (`CraftingStats.CalculateStats`/`SetWeaponData`): center of mass, inertia (parallel axis with the
  unscaled piece inertia 1/12 × w × L²), inertia around shoulder/grip, swing and thrust speed (the three-layer torque /
  force simulations, with the two-handed and wide-grip variants), handling (100 × (1 / I_grip')^0.55), balance, piece
  tier, swing damage = max over impact points of `CombatStatCalculator.CalculateBaseBlowMagnitudeForSwing` × the blade's
  swing factor, thrust damage = `CalculateStrikeMagnitudeForThrust` × the thrust factor, thrown weapons (×2 throwing axe
  swing, ×3.3 knife, ×9 javelin), missile speed (thrust speed × 3.2/3.9/3.6), stack (1 for the alternative usage),
  accuracy, item usage (the description's features minus the pieces' excluded ones), sweet spot.
- **RBM's patches**: `CombatModule/Magnitude/MagnitudeChanges.Thrust.cs` replaces `CalculateStrikeMagnitudeForThrust`
  (active when crafted items load, since patching runs from the main menu): thrust magnitude = 0.5 × 8 × clamp(thrust
  speed, 4, 6)² × `ThrustMagnitudeModifier` (0.05), whatever the weapon weighs or whether it is thrown. Tier
  (`ItemValuesTiers.Tiers.cs`): Tierf = 0.6 × RBM melee tier (from the first usage's damage factors and speeds, 0.3..6.5)
  + 0.4 × vanilla `CalculateTierCraftedWeapon` (piece tiers and Iron1..6 materials); the game shows clamp(round(Tierf),
  0, 6). Price (`ItemValuesTiers.Pricing.cs`, RBM campaign on, multipliers at defaults): the `value` attribute, else
  (30 + 24t) (polearms 30 + 16t; two-handed × 1.5), t = 2.75^clamp(Tierf, −1, 7.5). The vanilla column uses vanilla's
  `CalculateTierMeleeWeapon` (every usage) and `CalculateValue` instead (unless "vanilla with RBM formulas" is on).
- **RBM's tooltip numbers** (`CombatModule/Magnitude/Tooltips/MagnitudeChanges.WeaponTooltip.cs`
  `GetRBMMeleeWeaponStats`, for a character with the header's skill, no item modifier, melee usages only): swing/thrust
  speed in m/s (`Utilities.CalculateVisualSpeeds`), the sweet spot and its swing magnitude
  (`CalculateSweetSpotSwingMagnitude`, real length = weapon_length − CoM for descriptions with
  `use_center_of_mass_as_hand_base`), the thrust magnitude (`CalculateThrustMagnitude`,
  `Utilities.CalculateThrustMagnitudeForOne/TwoHandedWeapon`), then `RBMConfig/Shared/SkillDamage.GetSkillBasedDamage`
  and `BlowDamage.RBMComputeDamage` with √(damage factor) at armor 0..100 (damage, penetrated, blunt), the config's
  default weapon-type factors (`RBMConfig/Utilities.cs createWeaponTypesFactors`) and armor settings.

Approximations and limits (marked in the page where they apply):

- **Missile speed (≈)** is the item's stored `missile_speed` (the crafting formula). RBM sets the real launch speed of a
  thrown weapon at throw time (`RBMConfig/Shared/MissileBallistics`, skill and armor dependent), which is not computed;
  nor is RBM's missile damage table for thrown weapons.
- **Float rounding**: the port follows the game's float/double steps, but `Math.pow` and the CLR's `Math.Pow` may differ
  in the last bit, so a value that lands exactly on a rounding edge (handling, a speed's floor) can be off by 1.
- **Not modelled**: item modifiers (quality), smithing perks (`SharpenedEdge`/`SharpenedTip`), RBM's in-battle magnitude
  code (`MagnitudeChanges.Melee.cs`, hit position, swing direction, mounted speed), holster positions, and the smithy's
  `StatsData` bars (shown and edited, not used by the item's stats).
- **Ground truth check** (node, data of 2026-10-06): 94 campaign crafted items carry the comment
  `Crafting.GetXmlCodeForCurrentItem` writes ("Length: N Weight: W"). With vanilla data, 60 weights and 44 lengths match
  exactly (40 both). Where the weight differs too the comment is stale; 20 items match the weight but not the length
  (e.g. three maces on the 78 cm `mace_handle_11/12`, 17 cm longer than their comment, and the War Sails throwing axes),
  most likely pieces resized after the comment was written, but that cannot be confirmed without the game. The 8
  comments with speeds all have a stale weight, so speeds have no usable ground truth.
