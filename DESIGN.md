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
| Language | GDScript with static typing (hot paths may move to C++ GDExtension later if profiling demands it) |
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

### 1. Player controller (spec — under review, not implemented)
- **Feel:** snappier than Tarkov, but with momentum and weight. Acceleration and
  deceleration are not instant; changing direction at a sprint is slower than when walking.
- **Stances:** stand, crouch, prone. No lean, no fine stance height.
- **Speeds** (starting values, all tunable):
  - Walk ≈ 2.0 m/s · Run (default) ≈ 3.5 m/s · Sprint ≈ 5.5 m/s
  - Crouch ≈ 1.5 m/s · Prone ≈ 0.6 m/s
  - Sprint is forward-only; sprinting turn rate is limited.
- **Jump:** small jump, costs stamina. (Vaulting is in the Parking Lot.)
- **Stamina:** a single **leg stamina** pool.
  - Drained by sprinting and jumping, regenerates after a short delay.
  - Hitting zero = exhausted: no sprinting until it recovers to a threshold.
- **Progression:** movement improves with character skills/fitness (Zomboid/Tarkov style).
  - Skills level up by doing (sprinting raises fitness, etc.).
  - Fitness improves stamina max, regen rate, and slightly improves sprint speed and acceleration.
  - The controller reads all values from a **stats component**, never hard-coded
    constants, so skills, injuries and (later) carry weight can modify movement.
- **Camera:**
  - Third person: over-the-shoulder, with a shoulder-swap key.
  - First person: camera attached to the head bone of the full-body model.
  - Toggle key swaps between them instantly at any time.
  - Bullets always originate from the weapon, never the camera, in both modes.
- **Gamepad:** analog stick tilt scales speed (light tilt = walk, full tilt = run).
  Look sensitivity, deadzones and response curve are adjustable settings.

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
1. Player controller + first/third person camera + stats component + leg stamina
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
- Aim assist for gamepad (decide during gunplay step)
- AI chat-model NPCs (dropped)
- Multiplayer / co-op
