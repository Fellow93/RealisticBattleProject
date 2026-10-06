# Troop perk editor

A local page for editing `RBMXML/rbm_troop_perks.xml`, the hero perks RBM gives to regular troops
(loaded by `RBMConfig/Shared/TroopPerks.cs`). It lists every troop and every perk, shows what each perk's
personal effect does, and exports the XML.

## 1. Generate the data

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\TroopPerkEditor\Build-TroopPerkData.ps1
```

This writes `troop-perk-data.js` next to the page. It reads:

- **Perks**: `decompiled/TaleWorlds.CampaignSystem/.../DefaultPerks.cs` and, when present,
  `decompiled/NavalDLC/.../NavalPerks.cs` (War Sails). Each perk's text is filled in the way the game does it
  (`StringHelpers.GetEffectIncrementTypeBonusText`: `AddFactor` bonuses as percent, `+` for positive values).
  If `decompiled/` is missing, run `tools\Decompile-Bannerlord.ps1` first. The epic-perk skill thresholds
  (`Min/MaxSkillRequiredForEpicPerkBonus`, used for the Mighty Blow HP warning) come from
  `DefaultCharacterDevelopmentModel.cs`.
- **Audit**: `audit/*.json` (files starting with `_` are notes and skipped). Each perk gets its verdict,
  summary, conditions, RBM notes and call sites; see "The audit" below. The script also reads
  `NoTroopEffectPerkIds` from `RBMConfig/Shared/TroopPerks.cs` (the loader's do-nothing list) and warns when
  it and the audit disagree, when an audit id is not a perk, and when a perk with a personal effect has no
  audit entry.
- **Troops**: the `NPCCharacters` and `SkillSets` XML that each module's `SubModule.xml` registers, in load
  order Native, SandBoxCore, SandBox, StoryMode, CustomBattle, NavalDLC (if installed), then RBM's own
  `RBMXML/` and `RBM_WS_XML/` (War Sails only) from this repo. A later definition of an id replaces the
  earlier one, except that a file not loaded in campaigns (custom battle only) never replaces a campaign
  troop, since troop perks only load in a campaign. Heroes are skipped. Tier is
  `clamp(ceil((level - 5) / 5), 0, 6)` as in `DefaultCharacterStatsModel.GetTier`.
- **Current mapping**: `RBMXML/rbm_troop_perks.xml`, including its header comment.

Re-run it after a game update, after changing RBM's troop XML, or after editing `rbm_troop_perks.xml` by hand.
The game's Modules folder is taken to be the one this repo sits in; pass `-ModulesRoot <path>` otherwise.

`troop-perk-data.js` is gitignored: it contains TaleWorlds text.

## 2. Edit

Open `index.html` in a browser (double-click it; no server needed).

- Left: troops, with search and filters. Click a troop to edit it; tick several (or Ctrl+click) to add or
  remove a perk on all of them at once.
- Middle: the selected troop's skills, upgrade tree (click to jump) and assigned perks. "Copy perks to
  upgrade tree" adds the troop's perks to every troop it can upgrade into.
- Right: perks by skill. Tick a perk to give it to the troop. Each perk shows its audit verdict as a badge,
  the audit's "troop gets" line (what a regular troop really gets under RBM), the conditions ("When") and RBM
  notes, the game's own text, the captain/party/governor halves dimmed as "not applied to troops", and a
  collapsible "Where it's checked" list of call sites (marked "troop" when the check reaches a troop's own
  perk). "Only perks that work for troops" (on by default) hides the do-nothing verdicts; the verdict menu
  filters by one verdict, "harmful" or "not audited". Perks already given to the troop are always shown.
  Skill requirements are not checked for troops; the troop's skill is shown for reference.
- Warnings: a perk with a do-nothing verdict, or Mighty Blow on a troop with 250 Athletics or less (the loader
  skips it there, as its HP bonus would be negative), is flagged on the troop's assigned-perk row and in the troop list, counted
  in the red "perk problems" button next to Export (click it for the list), and listed in the Export dialog.
  They are still exported: the game loads them and logs them to `rgl_log`.

Work in progress is kept in the browser's local storage. "Reset to file" goes back to the mapping the data
was generated from.

## 3. Save the result

Click **Export XML…**, then **Download rbm_troop_perks.xml** (or copy it), and replace
`RBMXML/rbm_troop_perks.xml` in the repository with it. Building the solution copies it into the RBM module,
so do not copy it into the module folder by hand. Then re-run the script so the page's "matches file" state
is current.

**Import…** loads an existing `rbm_troop_perks.xml` (file or pasted) and replaces the current mapping.
Unknown troop or perk ids are kept and reported, never dropped silently; the game skips them and logs them
to `rgl_log`.

## The audit

`audit/` holds a code audit of every perk (436 on v1.5.4 + War Sails): does its personal half reach a regular
troop in battle under RBM? A troop perk only works when the game asks `CharacterObject.GetPerkValue` for the
troop's own character during a mission (RBM's postfix answers it) and RBM still runs that check.
`audit/README.md` is the summary (counts, the do-nothing list, the harmful perk, RBM/vanilla issues found);
`audit/_rbm-bypass-map.md` lists every place RBM replaces a vanilla method that contains perk checks; each
group has `<group>.json` (the data the editor shows) and `<group>.md` (its table and notes).

Verdicts:

| Verdict | Badge | Meaning for a troop given the perk |
|---|---|---|
| `works` | green | The personal effect applies as described. |
| `conditional` | amber | Applies only in some situations (weapon, mounted, sea, skill threshold, an RBM option...). |
| `partial` | amber | Part of the personal effect applies, part does not. |
| `hero-only` | red | Only checked for heroes (agent.IsHero, a Hero object) or the player's agent. Nothing. |
| `campaign-only` | red | Acts on the campaign map or a hero's own actions; nothing in battle. |
| `no-check-found` | red | No battle code reads it for an AI troop. Nothing. |
| `rbm-bypassed` | red | RBM overrides what it changes, for everyone (heroes too). Nothing. |
| `no-personal-effect` | grey | Only captain/party/governor/... halves, which a troop never gets. Nothing. |
| `unclear` | outline | The audit could not decide. |

The JSON schema is `{group, skills, auditedAt, perks:[{id, skill, personalHalf, verdict, confidence, summary,
conditions, sites:[{method, file, reachesTroop, why}], rbm}]}`. Every perk id must be a perk's StringId
(note the game's own typos, e.g. Steed Killer is `PolearmSteadKiller`).

The audit is maintained by hand (or by an agent), not generated: `Build-TroopPerkData.ps1` only merges it. After
a game update, or after RBM changes a patched method that carries perk checks:

1. Regenerate `decompiled/` (`tools\Decompile-Bannerlord.ps1`) and `git diff tools/bannerlord-types.lock.txt` to
   see which types changed (`DefaultPerks`, the Sandbox stat/damage/strike models, `PerkHelper`, NavalDLC
   models...).
2. Re-check `_rbm-bypass-map.md` against RBM's patches, then re-audit the perks whose call sites moved: update
   their `verdict`, `summary`, `conditions`, `sites` (file:line) and `rbm`, and the group's `.md` table, and bump
   `auditedAt`. New perks need an entry; the script warns about personal perks without one.
3. Keep `NoTroopEffectPerkIds` in `RBMConfig/Shared/TroopPerks.cs` equal to the set of hero-only,
   campaign-only, no-check-found and rbm-bypassed verdicts (the script warns on any difference), and the list in
   the header comment of `RBMXML/rbm_troop_perks.xml`.
4. Update `audit/README.md` and re-run the script.
