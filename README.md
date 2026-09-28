# Dungeon Generator — Procedural Roguelike

A top-down 2D roguelike game. Every floor is made by the computer (procedural
generation), so each floor is different. This is my final project about
**Procedural Dungeon Generation in Roguelike Games**.

- **Engine:** Unity 6000.5.0f1 (2D, URP)
- **Language:** C#
- **Play in browser (WebGL):** [add your itch.io link here]
- **Video demo:** [add your video link here]

---

## How to play

Goal: find the **key**, open the **exit**, and go down to the next floor. Try to
go as deep as you can.

| Action | Keys |
| --- | --- |
| Move | WASD or Arrow keys |
| Attack | Space or Left mouse click (uses mana) |
| Go to next floor | Walk into the exit door (you need the key first) |
| Pause | Esc |
| Show run log | T |

When you clear a floor you get a medal (Bronze, Silver, Gold, or Diamond).
You get a better medal if you lose less health. If you clear a floor without
attacking, you get Diamond.

---

## Modes

- **Normal** — no fog of war (you can see the whole floor).
- **Hard** — fog of war (you only see near the player).
- **Practice** — all floors are unlocked, for testing. Nothing is saved.
- **Endless** — after you clear floor 100, you can keep going with no limit.

---

## Main features

- New dungeon every floor (rooms, corridors, key, exit).
- Difficulty goes up each floor (more/stronger enemies, fewer potions).
- Enemies that chase the player.
- Key and locked exit.
- Fog of war and minimap.
- Health potions and stat upgrades.
- Medals and progress saved per mode.
- 100 floors plus an Endless mode.

---

## How the generator works (short version)

Each floor is made in steps:

1. **BSP (Binary Space Partitioning):** split the map into smaller boxes and
   put one room in each box.
2. **Corridors:** connect the rooms with paths.
3. **Cellular automata:** smooth the shape so it looks more natural.
4. **Checks:** make sure every room can be reached, and make sure the floor is
   not just a straight line (it must have some side rooms). If a floor fails the
   checks, the game makes a new one.
5. **Placement:** the spawn, the exit (farthest room), and the key (a side room)
   are chosen using a room graph.

Enemies find the player using the **A\*** pathfinding algorithm.

---

## How to run

**Play online:** open the itch.io link above in a browser.

**Open in Unity:**
1. Install Unity **6000.5.0f1**.
2. Open this folder as a Unity project.
3. Open the scene `Assets/Scenes/DungeonGeneratorMain.unity`.
4. Press Play.

**Make a WebGL build:** in Unity, use the menu **Build → WebGL Playtest Build**.
The build is saved in `Builds/WebGL`.

---

## Project structure

```
Assets/
  Scenes/       DungeonGeneratorMain.unity   (the game scene)
  Scripts/
    Generation/ dungeon generator, room graph, A* pathfinding, decoration
    Player/     movement, health, mana, animation, camera
    Enemies/    enemy AI and spawner
    Items/      key, potions, upgrades, exit
    UI/         menus, HUD, minimap
    Systems/    difficulty, fog of war, progress, telemetry, floating text
    Debug/      testing tools
  Prefabs/      Enemy prefab
  Sprites/      art
Builds/WebGL/   the browser build
```

---

## Credits & third-party assets

| Asset | Creator | Licence | Source |
| --- | --- | --- | --- |
| **Cute Penguin Character Sprite** (idle, run, attack) — used as the player character. Extended with extra facing directions (up, down, and the four diagonals) built from the original frames. | CazBee | Creative Commons Zero v1.0 (CC0) | https://caz-bee.itch.io/cute-penguin-sprite-idle-run-attack |
| **Free Platformer Slimes**, © 2023–2026 — used as the enemy sprite. Blue slime; left/right facing frames (the right frame is a horizontal mirror of the left). | Pixelsnorf | CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/) | https://pixelsnorf.itch.io/platformer-slimes |
| **Wall tile (dark brick)** — drawn by the author for this project. | Author | Own work; free to reuse | Included in this repository (`Assets/Sprites/brick.png`) |

Fonts (Press Start 2P, VT323) are licensed under the SIL Open Font Licence; see the
`OFL.txt` files in each font folder under `Assets/Fonts/`.

---

## Notes for evaluation

- Generation quality is measured with a test tool (`DifficultyTestHarness`)
  that makes many floors and writes numbers to a CSV file (room count, floor
  size, generation time, and how many floors are fully connected).
- Player data is measured with `SessionTelemetry`, which writes one line per
  floor (explored %, time, damage, heals, outcome). Press **T** in game to see
  this log.
