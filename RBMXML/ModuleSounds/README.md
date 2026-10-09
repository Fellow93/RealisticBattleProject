# RBM module sounds

Audio files for `RBMXML/module_sounds.xml`. The RBM project's post-build step copies every `.ogg`/`.wav` here to
`RBM/ModuleSounds/`, where the engine looks for them. Never copy them into `RBM/` by hand.

## Massed march grunt

Played once per beat by every marching infantry formation (two or three times, spread across the line, for big
formations). It has to sound like a whole formation, so it must be one recording of many men, not one man:

- a short, unison crowd "HUH!" / "HA!": the voice part about 0.3–0.8 s, a short natural tail is fine
- mono, 44.1 or 48 kHz, `.ogg` (or `.wav`), normalised, no silence at the start (it is played on the beat)
- several takes per sound (3–5), listed as `<variation>`s, so beats do not repeat identically
- optional: one set per culture (`rbm/march/empire`, `rbm/march/sturgia`, ...), otherwise one `rbm/march/generic` set

Then add the `<module_sound>` entry to `RBMXML/module_sounds.xml` (template in its comment) and rebuild.
Only add an entry once its files are here.

Mind the licence of anything not recorded yourself: it ships with the mod.

## What is here

- `march_crowd_*.ogg`: grunts of "CRWDBatl_Crowd Grunting, Exerting, Metered, Rowing" by ShangusBurger / Shane
  Vincent, GameSoundCon 2024 walla session, **CC0**. Cut from Freesound's HQ previews: mono, 48 kHz, 1.0 s each,
  5 ms fade-in, 180 ms fade-out, peaks at -3 dBFS.
  - `1-8`: the eight grunts of the XY-AKG214 take (https://freesound.org/people/ShangusBurger/sounds/763822/)
  - `9-16`: the same eight grunts, spaced-omni take (https://freesound.org/people/ShangusBurger/sounds/764266/)
  - `17-22`: two different grunts of 1-8 layered 15-40 ms apart (second at 0.75-0.9x), for a bigger crowd:
    17 = 2+5, 18 = 3+7, 19 = 4+8, 20 = 6+1, 21 = 7+3, 22 = 8+4
- `source/`: the downloaded recordings the clips were cut from (not copied to the game by the build, which only takes
  `*.ogg`/`*.wav` from this folder's top level). All CC0, same author and session:
  - `763822_*` crowd grunting/rowing, XY, and `764266_*` the same performance, spaced omni (both used)
  - `764268_*` group marching "Hup 2 3 4" (continuous counting, not cut)
  - `763820_*` battle charge, no words (one 9 s roar, not used yet)
