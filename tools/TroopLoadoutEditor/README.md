# Troop skills / loadout editor

A local page for editing troops' level, skills and equipment rosters in RBM's troop overhaul files,
`RBMXML/RBMCombat_unit_overhaul.xml` and (War Sails) `RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml`. It lists every
troop the game loads, shows where each value comes from, and exports the patched file(s), touching only the
edited troops.

## 1. Generate the data

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\TroopLoadoutEditor\Build-TroopLoadoutData.ps1
```

This writes `troop-loadout-data.js` next to the page (about 6 MB, a minute to build). It reads:

- **Skills**: `decompiled/TaleWorlds.Core/.../DefaultSkills.cs` (and War Sails' `NavalSkills.cs`), for ids, names and
  order. If `decompiled/` is missing, run `tools\Decompile-Bannerlord.ps1` first.
- **Troops, items, equipment sets, skill sets**: the `NPCCharacters`, `Items`, `EquipmentRosters` and `SkillSets`
  XML that each module's `SubModule.xml` registers (a folder path loads every `*.xml` in it), in load order
  Native, SandBoxCore, SandBox, StoryMode, CustomBattle, NavalDLC (if installed), then RBM's own `RBMXML/` and
  `RBM_WS_XML/` (War Sails only) from this repo. Definitions are merged the way the game merges them:
  - plain files go through `MBObjectManager.MergeElements`: a same-id element is merged into the earlier one
    (attributes overlaid, children merged by the `XmlSchemas/*.xsd` AlwaysPreferMerge / `xs:unique` keys, other
    children appended). NavalDLC uses this to add to vanilla troops (the sea raiders, ...) and items;
  - RBM's files go through `RBM/XmlLoadingPatches.cs` instead (see below);
  - a file not loaded in campaigns (custom battle or multiplayer only) never changes a campaign definition; it only
    adds ids no campaign file has (marked "not in campaign" in the item picker).
  Heroes are skipped. Tier is `clamp(ceil((level - 5) / 5), 0, 6)` (`DefaultCharacterStatsModel.GetTier`).
- **The two RBM troop files' full text**, which the page splices on export.

Assumed RBM settings: combat, campaign and troop overhaul on, `passiveShoulderShields` off (the defaults).
So `RBM_COMBAT_ONLY_XML_TAG` item files (`RBMCombat_ranged.xml`) are skipped and `RBM_ECONOMY_COMBAT` ones load.

Item stats come straight from the XML: plain `Item`s show armor, weapon (swing/thrust damage and type, speed, length,
missile speed, stack, shield armor/HP), horse (speed, maneuver, charge, HP = the monster's `hit_points` +
`extra_health`), weight and value when the XML sets them. `CraftedItem`s list their template and pieces only: their
stats, weight and value are computed in game from the pieces. The tier is `tier_override` when set, else the `_tN`
in the id (shown with a `?`), else blank (the game computes it).

The script warns about unknown items, equipment sets, skill templates and upgrade targets, items that do not fit
their slot (`Equipment.IsItemFitsToSlot`), and items or troops defined twice in one file. The warnings it gives
today are real data issues: the vanilla `fighter_*` templates reference equipment sets that do not exist, and the
hidden hand troops put sling ammo in `Item4`, which only takes items flagged `DropOnWeaponChange`.

Re-run it after a game update, after changing RBM's troop or item XML, and after saving an export.
The game's Modules folder is taken to be the one this repo sits in; pass `-ModulesRoot <path>` otherwise.
`troop-loadout-data.js` is gitignored: it contains TaleWorlds text.

## 2. Edit

Open `index.html` in a browser (double-click it; no server needed). It loads `troop-loadout-data.js` and
`troop-loadout-core.js` (the model and the export splicing, also usable from node).

- **Left**: troops, with search and filters (culture, tier, formation, occupation, module, edited, in an RBM file,
  templates/obsolete). Tick troops (or Ctrl+click) for bulk changes; "Revert ticked" undoes them.
- **Middle**: the selected troop: where it is defined, whether the export rewrites it in place or appends a copy,
  the upgrade tree (click to jump), the level (tier follows), and the **skills**: a number and a bar per skill, the
  file's value and its source (an explicit `<skill>` entry or the `skill_template`), the change highlighted, a
  reset per skill and for all skills. "Skill hints" flag a loadout that carries a bow, crossbow, throwing weapon,
  polearm, two-handed or one-handed weapon or a horse while the matching skill is under 75% of the average of
  troops with the same culture, tier, formation and occupation that carry the same kind of weapon. Bulk: add to or
  set a skill on every ticked troop, copy this troop's skills to them, tick the upgrade tree.
- **Right**: the **loadouts**. One card per `EquipmentRoster` (type Battle/Civilian/Stealth; move, duplicate,
  revert, delete, copy to every ticked troop) with all twelve slots. Click a slot to pick an item: only items that
  fit the slot are listed, with search, type/culture/tier filters and sortable stat columns; ✕ clears a slot. Each
  card totals the armor per body part (the sum over the armor slots, as the game does), the gear weight and the
  listed value. "Shared slots" are `<equipment>` elements directly under `<Equipments>`: the game applies them over
  every roster and equipment set of the troop (`MBEquipmentRoster.AddOverriddenEquipments`); RBM uses them for
  horses. `EquipmentSet` references are shown read-only with the referenced equipments of the matching type
  expanded, each with "Copy into an editable roster". With troops ticked, the pane header replaces item X with
  item Y in every roster and shared slot of the ticked troops (only where Y fits the slot).
- **Problems** (header button, for edited troops): unknown item ids, items that do not fit their slot, a troop
  without an `EquipmentRoster`, a battle roster without a weapon in Item0-Item3, a bow/crossbow/sling without its
  ammo, a roster slot hidden by a shared slot, items only defined in files not loaded in campaigns.

Work in progress is kept in the browser's local storage, per troop. "Reset to file" drops all of it, "Revert troop"
one troop.

## 3. Save the result

Click **Export…**. Only files with changes are offered; each shows the changed troops, what changed, their
problems, and the full patched file. **Download** (or **Save to file…** in browsers that support it, or Copy) and
replace the file in the repository with it. Building the solution copies it into the RBM module, so do not copy it
into the module folder by hand. Then re-run the script so the page starts from the new file.

The export splices text, so everything outside the edited troops stays byte-identical (BOM and CRLF line ends
included; the textarea preview shows LF, Download/Save/Copy keep the file's line ends):

- A troop already in the RBM file keeps its element; only the `level` attribute value, the `<skill>` entries'
  values (new skills are added as entries shaped like their siblings) and the changed `<equipment>` entries change.
  Unchanged rosters keep their text, changed ones keep their entries' formatting and comments, new rosters are
  shaped like the troop's first roster. Attributes (names like `{=s3IJIFUw}Imperial Recruit` verbatim), face,
  upgrade targets, comments, `EquipmentSet` references and their order are untouched.
- A troop not yet in an RBM file is appended before `</NPCCharacters>` as a full copy of its definition (from its
  source file; the merged, re-formatted element when several files define it), with the same edits applied and a
  comment naming where it was copied from: to `RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml` when it comes from
  NavalDLC, else to `RBMXML/RBMCombat_unit_overhaul.xml`.
- Skills: an edited skill of a troop with a `skill_template` is written as an explicit `<skill>` entry.
  `BasicCharacterObject.Deserialize` uses the template as is when the troop has no `<skills>` element; with one
  (even empty) it copies the template and applies each `<skill>` entry over it. Entries are never removed.

## How RBM applies these files (RBM/XmlLoadingPatches.cs, MergeTwoXmlsPatch)

- A file with an `RBM_XML_TAG` comment bypasses the game's keyed merge. For every `NPCCharacter` already loaded
  whose id matches one in the RBM file, the earlier node is removed **only when both have an
  `<Equipments><EquipmentRoster>`**, then RBM's nodes are appended. So an RBM troop with a roster is a full
  replacement; one with only `EquipmentSet` references (or replacing one that has none) leaves both definitions in,
  and the game deserializes both: the later sets level and skills, the rosters and sets of both are added. The
  editor flags such troops (today: `fighter_nord`) and warns when an exported troop has no roster.
- RBM `Item` / `CraftedItem` elements replace earlier ones of the same element kind and id.
- With `passiveShoulderShields` off (the default), equipment ids containing `shield` and ending in `_shoulder`
  (also `_kalkan_shoulder` / `_cataphract_shoulder`) are rewritten to the base shield id, but only inside the
  `EquipmentRoster`s of an RBM troop that replaces an earlier one (not in shared slots, not for new troops). The
  editor notes this on such items and shows the base shield's stats.
- `RBMCombat_unit_overhaul.xml` and the War Sails one carry `RBM_COMBAT_OVERHAUL_XML_TAG`: they only load with
  RBM combat on **and** "troop overhaul" on (`troopOverhaulActive`). Both are registered for the Campaign,
  CampaignStoryMode, CustomGame and EditorGame game types (`RBMXML/SubModule.xml`, `RBM_WS_XML/SubModule.xml`),
  so a troop appended there also exists in custom battles.
