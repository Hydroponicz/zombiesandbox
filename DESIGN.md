# Zombie Sandbox — Design Document

> Working title. This document is the single source of truth for what the game **is**.
> If a feature is not in here, it does not get built. New ideas go to the
> **Parking Lot** at the bottom until we deliberately promote them.

## Elevator pitch
A single-player, persistent open-world zombie survival game set in the United States
in 2032. Sandbox survival depth (Project Zomboid) meets lethal, weighty gunplay and
realism (Escape from Tarkov). Played in first or third person, switchable at any time.

## Core pillars
Every feature must serve at least one pillar.

1. **Lethal, weighty gunplay** — ballistics, armor, recoil and malfunctions matter. A few hits kill.
2. **Survive the sandbox** — hunger, thirst, injury and infection grind you down over time.
3. **Loot is everything** — grid inventory, weight, and a real sense of what you're carrying and what you'd lose.
4. **Zombies are a pressure system** — noise and sight draw them; hordes are deadly; gunfire has a cost.
5. **Survivors are unpredictable** — human NPCs and groups are hostile, neutral or friendly, and remember what you did.

## Locked decisions

| Topic | Decision |
|---|---|
| Engine | Godot 4.7.x, Forward+ renderer, Jolt physics |
| Language | **C#** (Godot .NET build). Chosen for performance at scale and maintainability of a large codebase. |
| Players | Single-player only (no multiplayer architecture) |
| Structure | Persistent open world. Death = new character. |
| Setting | United States, 2032 (near future) |
| Camera | First **and** third person, player can swap at any time |
| First person | **True full-body**: one character model and rig, camera at the head |
| Art | Between low-poly and mid-poly, assets made with Meshy.ai and cleaned up as needed |
| Weapons | Fictional / near-future names (avoids firearm trademark issues) — see Legal notes |
| NPC dialogue | Scripted (AI chat models are **dropped**) |
| Input | **Full controller support** — every feature playable on keyboard/mouse *and* gamepad. Steam Deck is a target. |

## System specs

### 1. Player controller (implemented on `preview` — in testing)
- **Feel:** snappier than Tarkov, but with momentum and weight. Acceleration and
  deceleration are not instant; changing direction at a sprint is slower than when walking.
- **Stances:** stand, crouch, prone. No lean, no fine stance height.
- **Speeds** (starting values, all tunable):
  - Walk ≈ 2.0 m/s · Run (default) ≈ 3.5 m/s · Sprint ≈ 5.5 m/s
  - Crouch ≈ 1.5 m/s · Prone ≈ 0.6 m/s
  - Sprint is forward-only; sprinting turn rate is limited.
- **Jump:** small jump (≈0.55 m), costs stamina. Jump from crouch/prone stands you up instead. (Vaulting is in the Parking Lot.)
- **Steps:** walks up curbs and stairs automatically (max ≈0.3 m) and sticks to the ground going down.
  Anything taller must be jumped.
- **Overhead clearance:** you can't stand or crouch up into a ceiling. If you asked to stand
  while under something low, you stand up automatically once there's room.
- **Collider:** flat-bottomed cylinder (reads stair edges as floor; works well with Jolt).
- **Stamina:** a single **leg stamina** pool.
  - Drained by sprinting and jumping, regenerates after a short delay.
  - Hitting zero = exhausted: no sprinting until it recovers to a threshold.
- **Progression:** movement improves with skills (see *Skills* below). The controller
  reads all values from a **stats component**, never hard-coded constants, so skills,
  injuries and (later) carry weight can modify movement.
- **Default pace:** keyboard defaults to *run*, hold a key to walk. Gamepad uses stick tilt.
- **Camera:**
  - Third person: over-the-shoulder, with a shoulder-swap key.
  - First person: camera attached to the head bone of the full-body model.
  - Toggle key swaps between them instantly at any time.
  - Bullets always originate from the weapon, never the camera, in both modes.
- **Gamepad:** analog stick tilt scales speed (light tilt = walk, full tilt = run).
  Look sensitivity, deadzones and response curve are adjustable settings.
- **Hold/Toggle settings:** sprint, crouch and prone each have a Hold/Toggle setting,
  stored separately for keyboard and gamepad.
  - Defaults: keyboard sprint = Hold; gamepad sprint = Toggle (CoD-style: click L3 once,
    sprint ends when you stop pushing forward or run out of stamina); crouch/prone = Toggle.

#### Default controls (all rebindable)
| Action | Keyboard / mouse | Gamepad (Xbox / PlayStation) |
|---|---|---|
| Move | WASD | Left stick |
| Look | Mouse | Right stick |
| Sprint | Left Shift | **L3 (left stick click)** |
| Walk | Left Alt (hold) | Light stick tilt |
| Jump | Space | A / Cross |
| Crouch | C | B / Circle (tap) |
| Prone | X | B / Circle (hold) |
| Switch first/third person | V | D-pad up (placeholder until D-pad is planned) |
| Swap shoulder (3rd person) | Q | R3 (right stick click) |

If the gamepad crouch is set to *Hold*, tap/hold on one button no longer works, so
prone moves to D-pad down.

### Skills
- Levels 0–10, new characters start at 3. Skills level up **by doing**, and each level
  needs more XP than the last. Exact curves live in a tunable resource.
- **Fitness** (implemented in step 1): raises max leg stamina (≈42 at level 0 → 150 at level 10: about 4.4 s of sprinting at level 1, 6 s at the starting level 3, 12.5 s at level 10), stamina regen
  (≈ +60% at 10), and slightly raises sprint speed (≈ +8%) and acceleration (≈ +10%).
  Earns XP from sprinting and jumping, with more XP when stamina is low.
- **Strength** (defined now, implemented with inventory in step 6): carry capacity.
- Weapon and other skills are added in the step that introduces them.
- A developer-only debug key sets skill levels for testing (removed from release builds).

## Architecture principles
- **Shared building blocks.** Player, zombies and human NPCs use the same health/damage,
  weapon, and perception components. NPCs play by the player's rules.
- **Data-driven stats.** Movement, weapons, items etc. read their numbers from resources,
  so tuning never requires code changes.
- **Persistence from day one.** Every persistent thing (container, item, zombie, NPC,
  door) has a stable unique ID and can save/restore its own state.
- **Chunked world from day one.** World positions are stored as *chunk coordinate +
  local offset*, so the map can grow far beyond floating-point-safe distances later.
- **Input goes through named actions, never raw keys.** Every action has both a
  keyboard/mouse and a gamepad binding, and both are rebindable.
- **Every UI is controller-navigable from the start** (especially the grid inventory).
  Button prompts swap icons to match the last-used device.
- **Off-screen simulation is abstract.** Distant zombies/NPCs are simulated as cheap
  data, not full scene nodes.

## Build order
Each step is refined until solid before moving on. All work happens in graybox test levels until step 8.

0. ✅ Project skeleton, conventions, this document
1. 🧪 Player controller + first/third person camera + stats component + leg stamina
2. Gunplay core (weapons usable by any character)
3. Health & damage — body parts, bleeding, fractures (shared by everyone)
4. AI foundation — perception (sight/hearing) + navigation, zombie as first user
5. Human NPCs — factions, disposition (hostile/neutral/friendly), combat with the same guns
6. Inventory & loot
7. Survival stats (hunger, thirst, fatigue)
8. World chunking/streaming + first real town generated from OpenStreetMap data
9. Full save/load of the persistent world
10. Scripted dialogue

## World vision (long term)
- Goal: generate real US geography from public data, growing **town → county → state →
  the 48 contiguous states** over time.
- Data sources (all usable commercially, with attribution where required):
  - **OpenStreetMap** — roads, building footprints, land use (ODbL: attribution + share-alike on the *map data*)
  - **USGS 3DEP** — elevation (public domain)
  - **USGS NAIP** — aerial imagery for reference / ground textures (public domain)
  - Google/Bing imagery is **not** allowed.
- Buildings get interiors from a library of templates; key locations are hand-polished.
- Real business names / brands from map data are stripped or replaced.
- Continent-scale distances will need either Godot's double-precision ("large world
  coordinates") build or origin shifting — decided at step 8.

## Legal notes (not legal advice — confirm with a lawyer before release)
- Steam requires disclosure of AI-generated content (Meshy assets).
- Confirm Meshy plan terms grant commercial rights for every asset used.
- Weapons use fictional names. Generic designations may be revisited later.

## Parking Lot (ideas NOT approved for building)
- Vaulting / climbing
- Leaning, fine stance height
- Arm stamina
- Free-look
- Skill decay over time
- Aim assist for gamepad (decide during gunplay step)
- AI chat-model NPCs (dropped)
- Multiplayer / co-op
