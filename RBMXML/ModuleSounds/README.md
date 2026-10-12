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

## Horns

Played by `FormationShoutsLogic` (Formation shouts setting) from the formation's man nearest the camera, carried
further than the voices (in full up to 20 m, pulled to at most 90 m away, silent past 400 m):

- `rbm/horn/charge/<culture>`: a cavalry or horse archer formation of 5 or more going over to Charge (player's or AI's
  order) blows it 0.1–0.4 s after the order, before its yells; once per formation per 20 s, a team's horns 2.5 s apart.
  Empire/Vlandia the battle horn and the horn in D, Sturgia/Nord/Battania/generic the viking horn, Khuzait/Aserai
  the shofar; pitched per culture in `module_sounds.xml`.
- `rbm/horn/order/<culture>`: Aserai (short shofar blasts) and Nord (viking horn) formations sound it with every order,
  0.1–0.3 s after the order line starts; once per formation per 6 s, not when the charge horn sounds.

Sound category `mission_siege_loud` (see the comment in `module_sounds.xml`).

All mono, peak -3 dBFS, single blasts cut from Freesound's HQ previews, all **CC0**: 30 ms pre-roll, 15 ms fade-in,
about 0.5 s of natural tail kept, 0.35 s fade-out (0.8 s where a long blast was trimmed: `horn_d_2`, `horn_shofar_5`).
Levelled by perceived loudness (ITU-R BS.1770 LUFS), gain under a peak limiter (5 ms look-ahead, 80 ms release,
-3 dBFS ceiling): the shofar reads loudest to the ear, the deep viking horn quietest (and is masked by battle
noise), so the shofar clips sit at -12 LUFS, the battle horn and horn in D at -10, the viking horn about -8.5 to
-10.5 (aimed at -8, as far as the limiter allows) after a +5 dB high shelf at 1.2 kHz so its overtones carry.

- `horn_viking_1-12.ogg` (2.4–3.4 s, deep, ~230 Hz): "medieval viking horn" by Artiom_Constantinov, a real horn
  recorded with a Sennheiser MKE 600 + Zoom H6 (https://freesound.org/people/Artiom_Constantinov/sounds/856540/)
- `horn_d_1.ogg` (7.6 s long blast) and `horn_d_2.ogg` (the same blast cut to 4 s with a fade): "Viking Horn in D" by
  roemergruft (https://freesound.org/people/roemergruft/sounds/704440/). CC0; the author asks for credit, so: thank
  you, roemergruft.
- `horn_battle_1.ogg` (4.5 s, ~300 Hz, with echo): "Battle_horn_1" by kirmm
  (https://freesound.org/people/kirmm/sounds/392180/)
- `horn_shofar_1-5.ogg` (1: 5.9 s, 2: 2.4 s, 3: 2.6 s, 4: 1.5 s, 5: 5.5 s, a long blast cut with a fade): "Small
  Shofar" (18-inch ram's horn) by Sauron974 (https://freesound.org/people/Sauron974/sounds/223368/)
- `horn_shofar_6.ogg` (2.7 s, two-tone): "shofar blast medium length two tone" by Sonicsheep
  (https://freesound.org/people/Sonicsheep/sounds/479310/)

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
- `horn_*.ogg`: the horns, see "Horns" above.
- `source/horns/`: the downloaded horn recordings (HQ previews, all CC0) the `horn_*` clips were cut from:
  `856540_*` viking horn (Artiom_Constantinov), `704440_*` horn in D (roemergruft), `392180_*` battle horn (kirmm),
  `223368_*` small shofar (Sauron974), `479310_*` two-tone shofar (Sonicsheep)
