# Troop upgrade tree editor

A local page for editing troops' upgrade targets (`<upgrade_targets>`) in RBM's troop overhaul files,
`RBMXML/RBMCombat_unit_overhaul.xml` and (War Sails) `RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml`. It draws the upgrade
trees, checks the edits against what the game does with them, and exports the patched file(s), touching only the
edited troops' `<upgrade_targets>`.

It is a sibling of the troop loadout editor (`../TroopLoadoutEditor/`) and shares its data and its export: it has no
build script or data file of its own.

## 1. Generate the data

From the repository root, run the loadout editor's script:

```powershell
powershell -ExecutionPolicy Bypass -File tools\TroopLoadoutEditor\Build-TroopLoadoutData.ps1
```

It writes `tools/TroopLoadoutEditor/troop-loadout-data.js`, which this page loads by relative path together with
`tools/TroopLoadoutEditor/troop-loadout-core.js` (the model and the export splicing). Besides the troops it holds,
for this page: each troop's `upgrade_requires`, the hero ids, the campaign cultures' troop references
(`basic_troop`, `elite_basic_troop`, militia, caravan guards, `<basic_mercenary_troops>`, ...) and which campaign party
templates spawn each troop. See `../TroopLoadoutEditor/README.md` for what else it reads and how it merges.

**Both pages export the same RBM files.** After saving an export from either page, re-run the script before using
the other one, or the other page starts from the old file (and its export would undo yours).

## 2. Edit

Open `index.html` in a browser (double-click it; no server needed).

- **Left**: troops, with search and filters (culture, tier, formation, occupation, module, edited, with/without
  targets or with problems, templates/obsolete). Badges: `RBM` (defined in an RBM troop file), the culture role
  (`basic`, `elite`, `merc`, `caravan`, `militia`), the number of upgrade targets. Tick troops (or Ctrl+click) to
  revert several at once.
- **Middle**: the upgrade trees, one column per tier. **Selected troop** shows the trees the troop belongs to, from
  the topmost troops that upgrade into it; **Culture** shows all of a culture's trees from its culture troops, with
  other cultures' troops as dashed, unexpanded boxes and the culture's soldiers without any upgrade edge listed below.
  Added or moved edges are blue, edges removed from the file are dashed red, problem edges red; numbers give the target
  order. Click a node to select the troop; drag anywhere on the diagram (left or middle button) to move it around,
  and use the mouse wheel over it to zoom (25-200%, around the cursor; the % button next to the troop count resets it).
- **Right**: the selected troop's targets in order: move up/down, replace, remove, revert; removed targets can be
  restored. **Add target…** opens a picker (search, culture/tier/formation/occupation filters, sortable columns;
  by default troops of the same culture and a higher tier first, same occupation first). Rows that would close a
  cycle are greyed out and cannot be picked. **Upgraded from** lists the troops that upgrade into it, each removable.
  `upgrade_requires` (the item category a party needs to upgrade into the troop) is shown read-only.
- **Problems** (header button, also on the troop list, the target rows and in the Export dialog) for the edited troops
  and the troops the edits affect; see "Checks" below.

Work in progress is kept in the browser's local storage under its own key (`rbm-troop-upgrade-tree-editor.v1`; the
loadout editor uses another). "Reset to file" drops all of it, "Revert troop" one troop.

## 3. Save the result

Click **Export…**. Only files with changes are offered; each shows the changed troops (`upgrade targets: a, b → a, c`),
their problems and the full patched file. **Download** (or **Save to file…** / Copy) and replace the file in the
repository with it. Building the solution copies it into the RBM module, so do not copy it into the module folder by
hand. Then re-run the script.

The export is the loadout editor's (`troop-loadout-core.js`, `buildExport`/`patchTroop`), so it splices text the same
way: everything outside the edited troops stays byte-identical (BOM, CRLF).

- A troop already in the RBM file keeps its element; only its `<upgrade_targets>` children change. Targets that stay
  keep their entry text, new entries are shaped like the element's first entry (else the file's first
  `<upgrade_target>`), so they follow the file's style (single-line in `RBMXML`, attribute on its own line in
  `RBM_WS_XML`). Comments inside stay put. An emptied list is written `<upgrade_targets></upgrade_targets>`, as RBM's
  files already write a troop without targets. A missing element is created where the game's files put it: after
  `face`/`Traits`/`skills`, before `<Equipments>`.
- A troop not yet in an RBM file is appended before `</NPCCharacters>` as a full copy of its definition (to the War
  Sails file when it comes from NavalDLC), with the new targets, exactly as for a loadout edit.

## What the game does with upgrade targets (verified in `decompiled/`, v1.5.4)

- **Reading** (`CharacterObject.Deserialize`, `TaleWorlds.CampaignSystem/CharacterObject.cs` 554-577): every child
  named exactly `upgrade_targets`, every `upgrade_target` in it, `id` read by `ReadObjectReferenceFromXml`, which
  needs the `NPCCharacter.` prefix (no dot throws `MBInvalidReferenceException`, `MBObjectManager.cs` 1513-1531).
  Then `UpgradeTargets = list.ToArray()`: no element, or an empty one, means no targets.
- **Two definitions of a troop**: `MBObjectManager.LoadXml` (1383-1392) deserializes every node in document order
  into the same object, and each `Deserialize` sets `UpgradeTargets` anew, so **the later definition's list replaces
  the earlier one, it is not added to**. Plain files merged by `MBObjectManager.MergeElements` are different:
  `upgrade_targets` is `AlwaysPreferMerge` with a unique key on `@id` (`XmlSchemas/NPCCharacters.xsd` 342-367), so
  NavalDLC adds targets to vanilla troops that way (e.g. `empire_marine_t3` on `imperial_infantryman`).
- **Unknown id**: `GetPresumedObject` (713-735) registers an empty placeholder; `UnregisterNonReadyObjects`
  (1433-1455) drops it after loading, but the troop's `UpgradeTargets` still holds it: a broken character.
- **No limit on the count.** The party screen's upgrade buttons are a horizontally scrolling list
  (`SandBox/GUI/Prefabs/Party/PartyTroopTuple.xml`, `UpgradesPanel`, 221-225; `PartyCharacterVM` adds one
  `UpgradeTargetVM` per target). AI upgrades (vanilla `PartyUpgraderCampaignBehavior.GetPossibleUpgradeTargets` and
  RBM's replacement, `RBMCampaign/Upgrades/SpoilsUpgradePatches.cs` 147-171), the fork weights
  (`UpgradeFormationWeights.cs` 432-445), notable volunteers (`RecruitmentCampaignBehavior.cs` 250-256) and tavern
  mercenaries (383-411) all loop over any number. The game's own troops have at most 2 (RBM gives up to 4).
- **Order**: the first target is what the party screen's XP bar measures (`PartyCharacterVM.InitializeUpgrades`,
  `i == 0`) and what RBM's spoils bar measures (`RBMCampaign/UI/SpoilsBarWidget.cs` 343-348).
- **XP cost** (`DefaultPartyTroopUpgradeModel.GetXpCostForUpgrade`, 30-74) sums fixed steps for each tier from the
  troop's tier + 1 to the target's: a target not above the troop's tier costs 0 XP, and AI parties then upgrade the
  whole healthy stack at once (`xpCost > 0` gate in `SpoilsUpgradePatches.cs` 163-171).
- **Player upgrades** need the target's level ≥ the troop's (`PartyCharacterVM.InitializeUpgrades`, 1288:
  `level >= Character.Level`); RBM's patches there are postfixes and keep that.
- **Bandit → non-bandit** needs Leadership: Veteran's Respect for the party
  (`DefaultPartyTroopUpgradeModel.DoesPartyHaveRequiredPerksForUpgrade`, 123-133).
- **Cycles crash or hang**: `FightTournamentGame.GetUpgradeTargets` (278-289) and
  `RecruitmentCampaignBehavior.FindTotalMercenaryProbability`/`FindRandomMercenaryTroop` (383-411) recurse with no
  guard (stack overflow), `CharacterHelper.GetTroopTree` (617-634) loops forever, and the encyclopedia unit page shows
  an error (`EncyclopediaUnitPageVM.DoesCharacterHaveCircularUpgradePaths`, 249-267).
- **Roots**: `CultureObject.Deserialize` (283-341, 474-480) reads the culture's troops from attributes
  (`basic_troop`, `elite_basic_troop`, militia, `caravan_guard`, ...) and `<basic_mercenary_troops>`. Volunteers start
  from the basic/elite troop and walk up the tree; tavern mercenaries start from `basic_mercenary_troops` or the
  caravan guard. Party templates (`PartyTemplateObject.Deserialize`, `PartyTemplateStack troop=`) spawn troops
  directly. RBM also walks trees from roots: the noble line is everything reachable from `EliteBasicTroop`
  (`RBMCampaign/Spoils/SpoilsPool.BattleLoot.cs` 99-116), and workshop troop orders walk the basic and elite trees
  (`RBMCampaign/Production/WorkshopTroopOrders.cs` 458, 515-542). Moving a troop between trees changes those too.
- **Alleys**: `DefaultAlleyModel.GetTroopsToRecruitFromAlleyDependingOnAlleyRandom` (180-203) adds
  `BasicTroop.UpgradeTargets[0]` of the mountain/steppe/desert/forest bandits or sea raiders (266-293): with no target
  it throws.

## How RBM applies these files (RBM/XmlLoadingPatches.cs, MergeTwoXmlsPatch, 318-390)

An RBM troop replaces the earlier definition only when both have `<Equipments><EquipmentRoster>`; otherwise both stay
in the document and both are deserialized. For upgrade targets that means the RBM definition's list always wins (it
is appended after the earlier one, so it is deserialized last), even when it does not replace it; but the game then
also adds the equipment of both. Today that is `fighter_nord` (its RBM definition has only an `EquipmentSet`); the
page shows the dropped earlier list. A troop not in an RBM file that has no roster gets a warning on export for the
same reason. A troop defined in both RBM files (e.g. `imperial_infantryman`) is exported to the War Sails file, the
one that loads last; the page notes that the `RBMXML` definition is the one used without War Sails.

## Checks

| Check | Level | Why (see above) |
|---|---|---|
| Unknown troop id | serious | broken placeholder character |
| Target is a hero | serious | a hero is not a troop type |
| Target only in files not loaded in campaigns | serious | unknown id in a campaign |
| Closes an upgrade cycle (and the picker refuses cycle rows) | serious | stack overflow / endless loop |
| A sea raider / mountain, steppe, desert or forest bandit basic troop left without targets | serious | `DefaultAlleyModel` throws |
| Target tier not above the troop's | warning | upgrade costs no XP |
| Target level below the troop's | warning | the player can never upgrade into it |
| Target is a template or obsolete | warning | not a real troop |
| Target listed twice | warning | two buttons; XSD unique key logs an error |
| New cross-culture target (bandit → non-bandit noted) | warning | Veteran's Respect needed for bandits |
| New target that only exists with War Sails, written to the non-War Sails file | warning | unknown id without War Sails |
| More than 2 targets (edited troops) | warning | more than the game's own troops; no hard limit |
| No longer reachable: a culture troop's tree reached it in the file, now none does (party templates that still spawn it are named) | warning | nobody recruits or upgrades into it |
| Appended copy without an EquipmentRoster; RBM troop that does not replace the earlier one; troop also in `RBMXML` but exported to `RBM_WS_XML` | warning | see "How RBM applies these files" |

Cross-culture and War Sails-only targets are flagged only for targets the edit adds: the game's bandits upgrade into
culture troops by design.
