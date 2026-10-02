# Getting Started

This page covers installing RBM, turning it on in the launcher, what it does about other mods, the module switches, and where to look when something goes wrong.

## Requirements

| | |
|---|---|
| Game version | Mount & Blade II: Bannerlord **v1.4.8** or newer |
| RBM version | v4.5.3 |
| Required modules | Native, SandBoxCore, Sandbox, StoryMode, CustomBattle |
| Optional modules RBM knows about | BirthAndDeath, RTS Camera, RTS Camera Command System |
| War Sails (Naval) DLC | Supported through a separate RBM submodule (see [War Sails](#war-sails-naval-dlc)) |

## Installing

RBM is one module folder. Put it in the game's `Modules` folder so the result looks like this:

```
Mount & Blade II Bannerlord\
  Modules\
    RBM\
      SubModule.xml
      bin\
      GUI\
      ModuleData\
```

If you own **War Sails**, also install the War Sails submodule next to it as `Modules\RBM_WS\`.

## Enabling in the launcher

1. Start the Bannerlord launcher and open the **Mods** tab.
2. Tick **(RBM) Realistic Battle Mod Bannerlord**.
3. With War Sails, also tick **(RBM WS) Realistic Battle Mod War Sails Submod**.
4. Load order: RBM goes **after** the official modules (Native, SandBoxCore, Sandbox, StoryMode, CustomBattle, and BirthAndDeath if you use it). If you use RTS Camera or RTS Camera Command System, put RBM after them as well. The War Sails submodule goes after both NavalDLC and RBM.

## Configuring RBM

Settings live on the **RBM Configuration** entry of the game's main menu. That is the only place to open them, so change settings before you start or load a game. RBM applies its changes every time a game starts or loads, so a changed setting takes effect from your next start or load. A few changes need a full game restart, as noted below.

Your settings are saved to:

```
Documents\Mount and Blade II Bannerlord\Configs\RBM\config.xml
```

Every option is listed in the [Settings Reference](settings.md).

## What's new

The title screen has an **RBM Changelog** badge in its top-right corner. Click it to see every RBM version and its notes. After an update, the newest version's notes open by themselves once, and the badge shows a dot until you have read them.

## The module switches

RBM is split into parts you can turn on and off separately. All of them are on by default.

| Switch | What it covers | Notes |
|---|---|---|
| **RBM Combat** | Damage, armor, weapon and missile physics, ranged reload, and RBM's reworked items, troops and siege engines. See [Combat & Armor](features/combat.md) and [Ranged Combat](features/ranged.md). | Off returns combat to the base game. RBM's spear and weapon animation parameters only change after a **game restart**. |
| **RBM AI** | Formation tactics and behaviors, siege AI, AI blocking and parrying, the frontline system, AI kicks and bashes, and the posture and stamina systems. See [Battle AI](features/ai.md). | Off returns battle AI to the base game. |
| **Posture System** | The posture meter, and the stamina system and posture bars that depend on it. See [Posture & Stamina](features/posture.md). | Needs **RBM AI** on. Turning it off also turns off the Stamina System and the Posture GUI. |
| **RBM Tournament** | Tiered tournaments: opponents, arena gear, prizes and renown follow your tier. See [Tournaments](features/tournament.md). | Off restores the base game's tournaments. |
| **RBM Campaign** | The troop spoils economy, wages and upkeep, settlement wealth, village and town production, caravans, equipment-aware auto-resolve and troop power. See [Campaign](features/campaign.md). | RBM's additions to the campaign screens only change after a **game restart**. |

When a switch is off, the options that belong to it stop working, even if they are still set to on.

When **RBM Combat** is off, RBM also leaves the game's items and troops alone and puts back the engine parameters it would otherwise change (walk speed, guard reset time and similar). If another mod loaded after RBM sets one of those parameters, that mod's value is kept.

## Compatibility

### War Sails (Naval) DLC

The War Sails submodule (**RBM WS**) adds RBM versions of the DLC's items, weapons, crafting pieces, troops and custom battle setups. It needs NavalDLC and RBM.

If War Sails and RBM are both active, RBM Combat is on and the submodule is missing, a message appears at the main menu. Without the submodule you can get problems such as Nords having no weapons. With RBM Combat off, RBM changes no items or troops, so the submodule isn't needed and no message appears.

### RTS Camera, Command System and BattleMiniMap

These mods share a common library. RBM checks whether that library is loaded and, if it is, plays it safe with formations. Its AI moves soldiers between formations only once instead of reshuffling them all battle, and it leaves some formation-movement handling to the base game. Those mods read formation layouts in a way that can freeze or crash the game when soldiers are reassigned mid-battle, so with one of them installed you may see slightly simpler formation handling.

RTS Camera is also **required** for RBM Campaign's *Spectate AI Battles* option, since its free camera is the only way to watch a battle with no player on the field.

### Other mods' saves

Whatever your switches are set to, RBM does two cleanups when a campaign loads:

- It repairs troop stacks that another mod left broken in the save. Such stacks would otherwise crash the game on the next daily tick.
- It removes clans that have no culture, which broken XML from other mods can leave behind.

## Troubleshooting

**My settings went back to defaults after an update.** RBM's config file has a version number. When an update changes the settings format, the old file is not read and all settings go back to their defaults. Open RBM Configuration and set them again.

**My settings don't save, or reset every launch.** If RBM can't read or write its config file, it falls back to the defaults instead of crashing. Common causes are a Documents folder redirected to OneDrive, Windows Controlled Folder Access blocking the game, or a file named `RBM` where the `Configs\RBM` folder should be. RBM writes a line starting with `[RBM]` to the game's log when this happens.

**A setting change did nothing.** Most changes apply the next time you start or load a game. RBM Combat's animation parameters and RBM Campaign's screen additions need a full restart of the game.

**Nords (or other War Sails troops) have no weapons.** Install and enable the RBM War Sails submodule (see above).

### RBM's log files

RBM can write detailed logs for bug reports or tuning. They go to subfolders of:

```
Documents\Mount and Blade II Bannerlord\Configs\RBM\logs\
```

| Folder | Contents | Turned on by |
|---|---|---|
| `battles` | Every blow of a fought battle and every blocked or parried melee blow, standings every 15 seconds, and a summary | *Field Battle Logging* (needs RBM Combat) |
| `ai` | Each team's tactic and each formation's behavior and orders, second by second | *AI Behavior Logging* (needs RBM AI) |
| `simulation` | Every auto-resolved battle | *Detailed Auto Resolve Logging* (needs RBM Campaign) |
| `powerCalculation` | Daily troop power breakdown of every party | *Troop Power Logging* |
| `campaign` | The spoils economy: purses, loot, upgrades, food | *Spoils Logging* (needs RBM Campaign) |
| `economy` | Village production, villager parties, town rations | *Economy Logging* |
| `caravans` | Supply caravans dispatched, sold, lost | *Caravan Logging* |
| `garrison` | Garrison refill activity | Always written while RBM Campaign is on |

All of these are off by default except the garrison log. The campaign-side folders keep only their 10 most recent files and delete older ones automatically. The *Armor Penetration Messages* option prints the blunt and penetrating damage of every blow you deal or take to the in-game message log instead of a file.
