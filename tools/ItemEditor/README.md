# Item editor

A local page for editing equipment items (armor, weapons, shields, ranged weapons and ammo, horses, harness, trade
goods, crafted weapons) in RBM's item files under `RBMXML/` and `RBM_WS_XML/`. It lists every item the game loads in
a campaign, shows each value next to the vanilla one, previews RBM's tier and price, compares items of a type side by
side for balancing, and exports the patched file(s), touching only the edited items.

## 1. Generate the data

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\ItemEditor\Build-ItemData.ps1
```

This writes `item-editor-data.js` next to the page (about 4.5 MB, 20 seconds to build). It reads:

- **Items**: the `Items` XML that each module's `SubModule.xml` registers (a folder path loads every `*.xml` in it), in
  load order Native, SandBoxCore, SandBox, StoryMode, CustomBattle, NavalDLC (if installed), then RBM's own `RBMXML/`
  and `RBM_WS_XML/` (War Sails only) from this repo, merged the way the game merges them: plain files through
  `MBObjectManager.MergeElements` (NavalDLC adds bits to vanilla items that way), RBM's files through
  `RBM/XmlLoadingPatches.cs` (see below). A file not loaded in campaigns (multiplayer `mpitems.xml`) never changes a
  campaign definition; its items are listed as "not in campaign". Per item the data holds the exact text of the
  definition the game uses (re-formatted when several plain files merged into it), the definition before any RBM file
  replaced it ("vanilla"), the merge chain, and every RBM file that defines the id.
- **The full text of every RBM item file** (18 with War Sails), with its BOM and line ends, which the page splices on
  export, and the file's RBM tags.
- **The attribute vocabulary**: every attribute `XmlSchemas/Items.xsd` allows on `Item`, `CraftedItem`, `Armor`,
  `Weapon`, `Horse`, `Trade`, `Banner`, `Flags`, `WeaponFlags` and `Piece`, with its type, and the enums the game parses
  attributes with (`ItemObject.ItemTypeEnum`, `WeaponClass`, `DamageTypes`, `ArmorMaterialTypes`, the cover types,
  `CraftingPiece.PieceTypes`, `ItemFlags`, `WeaponFlags`) from `decompiled/`. If `decompiled/` is missing, run
  `tools\Decompile-Bannerlord.ps1` first.
- **Lookups** for pickers and checks: cultures (`SPCultures`, XSLT applied), crafting pieces and templates (piece types,
  usable pieces; a template defined again accumulates its usable pieces), item modifier groups, item categories (created
  in code: `DefaultItemCategories`, War Sails' `NavalItemCategories`, RBMCampaign's `TradeGoodCategories`), item
  holsters (Native's `item_holsters.xml` + `RBMXML/RBMCombat_item_holsters.xml`), monsters, item usage sets.

Assumed RBM settings: combat **and** campaign on, so `RBM_COMBAT_ONLY_XML_TAG` files (`RBMCombat_ranged.xml`) are
skipped and `RBM_ECONOMY_COMBAT_XML_TAG` ones (`RBMEconomyCombat_ranged.xml`) load. The script warns about RBM items
without a `Type`, ids defined twice in one file and ids only defined in RBM files that do not load in campaigns.

Re-run it after a game update, after changing RBM's item XML by hand, and after saving an export. The game's Modules
folder is taken to be the one this repo sits in; pass `-ModulesRoot <path>` otherwise. `item-editor-data.js` is
gitignored: it contains TaleWorlds text.

## 2. Edit

Open `index.html` in a browser (double-click it; no server needed). It loads `item-editor-data.js`,
`item-editor-core.js` (the model, checks, tier/price preview and export splicing, also usable from node) and
`../TroopLoadoutEditor/troop-loadout-core.js`, whose XML text helpers (tokenizer, attribute get/set/remove,
re-indent) both editors share.

- **Left**: items, with search (name or id) and filters: type (grouped into armor, melee and
  ranged weapons, ammo, shields, mounts and other, each group with an "All ..." choice), weapon class (the `weapon_class` of
  any Weapon usage; crafted weapons have no Weapon element, so they are listed by crafting template), culture, RBM
  tier, module, source (in an RBM file, not in
  one, in two or more RBM files, new), edited, problems, and "not in campaign" (multiplayer items, hidden by default).
  Tick items (checkbox, Ctrl+click, Shift+click for a range) for **bulk** changes: set, change by %, add to or remove
  any attribute of the item or of every Armor/Weapon usage/Horse component; set or clear a Flags or WeaponFlags flag;
  set the culture. "Revert ticked" undoes them.
- **Middle**: the selected item. Where it is defined, which RBM files the export writes to (or the file a copy is
  appended to, with a target picker), its problems, the **RBM tier and price** preview (with the formula and its
  numbers), the general attributes, one box per component (Armor, each Weapon usage, Horse, ...) with its key stats up
  front, the WeaponFlags and Flags as checkboxes, "All ... attributes" to add, edit or remove any attribute (enum
  attributes get a list; `culture` is a dropdown of the cultures by name; `modifier_group` is an autocomplete of the
  item modifier groups with how many items use each, and only a known group or an empty field (removes the attribute)
  is accepted; other references get suggestions), the pieces of a crafted item (each piece id field is an
  autocomplete: it lists the pieces of that type, the template's usable ones first, with name, culture, length, weight
  and tier; every typed word must match; ↑↓/Enter/Tab pick, Esc cancels, and text that is not a piece id is put back),
  and the element as it will be
  exported (changed lines highlighted). Next to each value: the file's value and ↺ when edited, and the **vanilla**
  value (before RBM replaced the item) when it differs. Bows and crossbows also show what RBM makes of the draw weight:
  ideal ammo weight and launch speed. "Duplicate as new item" copies the item, with its current changes, under a new id.
- **Right**: the **comparison** table, for balancing: the items of the selected item's type (or the filtered list, or
  the ticked items) with the stats that matter for that kind of item, its tier and price, sortable, the selected item
  highlighted, changed cells marked with the file's value. Double-click a number to edit it in place.
- **Problems** (header button, for edited and new items; the filter covers all items): an RBM `Item` without `Type`, an
  attribute not in `Items.xsd`, an invalid enum value, a non-numeric number (or a fraction where the game uses
  `int.Parse`), unknown cultures, item categories, modifier groups, holsters, monsters, item usages, crafting templates
  or pieces, a piece of the wrong type or not usable by its template, a `Type` the weapon class overrides, a duplicate id
  on a new item, a missing target file, an `<Armor>` attribute vanilla sets that the item's definition leaves out
  (RBM replaces the whole item, so it falls back to the game default; only reported when vanilla's value differs from
  that default, per `ArmorComponent.Deserialize`); and, as a note, an item defined in several RBM files.
- **Data warnings** (yellow button next to the edited-items pill in the header): warnings about the data as a whole rather than the edited items: the
  generator's warnings, and every RBM item whose definition leaves out a vanilla `<Armor>` attribute (as above). Each
  item links to it, and a warning goes away once the attribute is set.

Work in progress is kept in the browser's local storage (`rbm-item-editor.v1`) as changes against the files, so after
re-running the script they are re-applied to the new data (changes to a component or piece that is gone are dropped,
and the page says so). "Reset to file" drops all of it, "Revert item" one item.

## 3. Save the result

Click **Export…**. Only files with changes are offered; each lists its edited items (patched in place, appended, or
new), what changed and their problems, with the full patched file. **Download** (or **Save to file…** in browsers that
support it, or Copy) and replace the file in the repository with it. Building the solution copies it into the RBM
module, so do not copy it into the module folder by hand. Then re-run the script so the page starts from the new files.

The export splices text, so everything outside the edited items stays byte-identical (BOM and CRLF line ends included;
the textarea preview shows LF, Download/Save/Copy keep the file's line ends). It is diff-based:

- An item already in RBM files is patched in **every** RBM file that defines its id, and only the attributes, flags and
  pieces that changed are written: start tags are patched in place (quote style and multi-line alignment kept), a
  cleared attribute is removed, a missing `<Flags>`/`<WeaponFlags>` is created shaped like the file's own (and removed
  when its last flag is cleared), a piece is added shaped like its siblings. So editing a ranged weapon changes both
  `RBMCombat_ranged.xml` and `RBMEconomyCombat_ranged.xml`, while their different `value`s stay unless you edit
  `value` itself.
- An item not yet in an RBM file is appended before the closing root tag as a **full copy** of the definition the game
  uses (re-indented to the file's indentation and line ends), with the edits applied and a comment naming where it was
  copied from. The target file follows the type: body/head/arm/leg armor, capes, harness, horses, shields to their
  `RBMCombat_*` file; arrows and bolts to `RBMCombat_arrow_visuals.xml`; bows, crossbows, slings, sling stones and
  thrown weapons to **both** ranged twins; siege ammo (boulders, ballista stones) to `RBMCombat_siege_ranged.xml`;
  goods and animals to `RBMEconomy_trade_goods.xml`; crafted polearms to `RBMCombat_lances.xml`, other crafted weapons
  to `RBMCombat_gladius.xml`; War Sails items to `RBM_WS_XML/RBMCombat_WS_items.xml` (crafted:
  `RBMCombat_WS_Weapons.xml`). Banners and other types have no default: pick a target in the middle pane.
- A new item (duplicate) is appended the same way, to the file(s) of the item it was copied from.

Not supported: adding or removing a component (`<Armor>`, a `<Weapon>` usage, ...), editing `<Horse>` materials and
meshes, comments inside an element (kept as they are). `<WeaponFlag name=""/>` children, which a few vanilla items use,
are ignored by the game (it reads `WeaponFlags` attributes only) and by the editor.

## How RBM applies item files (RBM/XmlLoadingPatches.cs, MergeTwoXmlsPatch)

- A file with an `RBM_XML_TAG` comment bypasses the game's keyed merge: every `Item` (or `CraftedItem`) already loaded
  whose id matches one in the RBM file is **removed**, then RBM's elements are appended. So an RBM item is a **full
  replacement**: there is no attribute merge, and anything the RBM element omits is gone (the game's default applies).
  That is why the export copies the whole definition.
- **Every RBM `Item` needs a `Type` attribute**: with `betterArrowVisuals` on (the default) the patch reads
  `Attribute("Type").Value` of each RBM `Item` to give `Arrows`/`Bolts` `flying_mesh` := `mesh`, and a missing one
  throws while loading. (The
  game itself parses `Type` case-insensitively and then replaces it with the first weapon's class's type,
  `WeaponComponent.GetItemType`; the editor shows that type.)
- File tags gate loading: `RBM_COMBAT_XML_TAG` needs RBM combat on, `RBM_CAMPAIGN_XML_TAG` campaign on,
  `RBM_ECONOMY_COMBAT_XML_TAG` both, `RBM_COMBAT_ONLY_XML_TAG` combat on **and campaign off**. The ranged weapons
  therefore live twice: `RBMCombat_ranged.xml` (combat only, campaign off) and `RBMEconomyCombat_ranged.xml` (combat
  and campaign), the same ids with different `value`s. The editor treats them as twins.
- Load order: RBMXML's files in `SubModule.xml` order, then RBM_WS_XML's, so a War Sails file can replace an RBMXML
  item again (the export patches both).

## RBM tier and price

The tier is `ItemObject.Tierf`: `tier_override` when set (≥ 0), else `ItemValueModel.CalculateTier`, which RBM patches
in `RealisticBattleCombatModule/CombatModule/Items/ItemValuesTiers.Tiers.cs`. The game shows
`clamp(round(Tierf), 0, 6)` (`ItemObject.Tier` is that minus one; `Math.Round` rounds .5 to even).

| Item | Tierf (RBM) |
|---|---|
| Body armor | body × 0.05 + leg × 0.035 + arm × 0.025 |
| Head / hand / leg armor | head × 0.06 / arm × 0.10 / leg × 0.10 |
| Cape | (body + arm) × 0.15 |
| Horse harness | body × 0.02 + leg × 0.04 + arm × 0.02 + head × 0.02 |
| Horse | pack animal: 1; else extra_health × 0.009 + maneuver × 0.03 + speed × 0.03 |
| Bow, sling, gun / crossbow | (draw weight − 60) × 0.049 + 1 / (draw weight − 80) × 0.031 + 1 (draw weight = `missile_speed`) |
| Arrows, bolts, stones | damage × 0.01 × ((weight × 100 − 4 + 0.01) × 0.8), at most 6 |
| Shield | ((hit points − 400) × 0.005 + armor × 0.2) × (length / 60), at most 6.5 |
| Crafted weapon | 0.6 × RBM melee tier + 0.4 × piece tier, from the weapon design: **not previewed** |
| Other non-crafted weapon | 0 (vanilla `CalculateTierNonCraftedWeapon`) |

The price is the `value` attribute when present; otherwise `CalculateValue`, which RBM patches in
`ItemValuesTiers.Pricing.cs`: with tier value t = 2.75^clamp(Tierf, −1, 7.5) (1 without a component), armor is a
type-specific base plus the armor values × a material factor from t (cloth 0.4 × clamp(t − 1, 0, 4), leather
0.6 × clamp(t − 1, 0, 6), mail 1.6 × clamp(t − 3, 1, 6), plate 1.7 × clamp(t − 3, 1, 6), none 50); weapons
(30 + 24t) (polearms 16t, two-handed × 1.5, slings × 0.25, shields 120 + 2t, ammo 20 + t); horses 600 + 1000 × t ×
the horse multiplier; truncated to a whole number. That is the RBMCampaign pricing; the page also shows the legacy
pricing used with RBM campaign off. The multipliers are assumed at the RBM config defaults (armor 1, weapon 1,
horse 0.2). Launch speed for bows and crossbows is `RBMConfig/Shared/MissileBallistics.CalculateMissileSpeed`, the
ideal ammo weight is from `CombatModule/Magnitude/Tooltips/MagnitudeChanges.WeaponTooltip.cs`.
