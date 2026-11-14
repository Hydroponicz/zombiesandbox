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

### 1. Player controller (done)
- **Feel:** snappier than Tarkov, but with momentum and weight. Acceleration and
  deceleration are not instant; changing direction at a sprint is slower than when walking.
- **Stances:** stand, crouch, prone. No fine stance height.
- **Lean** (first and third person):
  - Keyboard: hold Q / E (Hold/Toggle setting). Head and camera shift ≈0.35 m sideways;
    first-person camera also rolls ≈12°.
  - Stand or crouch only, never prone. Leaning limits you to walking speed; sprinting cancels it.
  - Walls limit the lean: your head stops short of anything in the way.
  - Third person: leaning moves the camera to the shoulder you lean toward.
  - Gamepad: **no manual lean**. It auto-leans when aiming next to cover (built in step 2, see below).
- **Speeds** (starting values, all tunable):
  - Walk ≈ 2.0 m/s · Run (default) ≈ 3.5 m/s · Sprint ≈ 5.5 m/s
  - Crouch ≈ 1.5 m/s · Prone ≈ 0.6 m/s
  - Sprint is forward-only; sprinting turn rate is limited.
- **Jump:** small jump (≈0.55 m) that **always** happens when pressed on the ground (presses just
  before landing are buffered). Costs stamina; with too little stamina the jump is just lower.
  - **Spam penalty, not a block:** every landing costs some speed and briefly reduces acceleration.
    Quick repeated jumps build *jump fatigue* (fades after ~2.5 s on the ground), which makes jumps
    lower and landings slow you down much more.
  Characters use 1.6× real gravity so jumps and falls feel heavy (≈0.5 s in the air).
  Jump from crouch/prone stands you up instead. (Vaulting is in the Parking Lot.)
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
| Lean left / right | Q / E | Automatic when aiming at cover (step 2) |
| Swap shoulder (3rd person) | Z | R3 (right stick click) |
| Aim down sights | Right mouse (toggle) | Left trigger (hold) |
| Hold breath (while aiming) | Hold Shift | Click L3 |
| Fire | Left mouse | Right trigger |
| Fire mode (semi / auto) | B | D-pad left |

If the gamepad crouch is set to *Hold*, tap/hold on one button no longer works, so
prone moves to D-pad down.

### 2. Gunplay (in design)

#### Locked decisions
- **Ballistics:** simulated projectiles. Bullets have travel time and drop with distance (no hitscan).
- **No crosshair** when not aiming. Hip fire goes where the barrel points.
- **Recoil:** Tarkov-style. The gun kicks and settles back on its own; weapon handling and skill
  reduce it. The player does not need to pull down to fight it.
- **First weapon:** a graybox, fictional AR-style rifle (semi + full auto).
- **ADS in third person:** player setting.
  - *Switch to first person* (looks through the real sights), or
  - *Over-the-shoulder zoom* (camera tightens over the shoulder and narrows FOV).
- **Shared by every character:** weapons are driven by an aim direction, so zombies-with-guns
  never happen but NPCs use exactly the same weapon code as the player.

#### Pieces (each finished before the next)
1. **Holding & aiming** — carry the rifle, ADS (both modes), sway, weapon collision with walls,
   gamepad auto-lean at corners and peeking over low cover.
2. **Firing & ballistics** — projectiles, drop, fire rate, semi/full-auto.
3. **Recoil.**
4. **Magazines & reloading.**
5. **Malfunctions.**
6. **Attachments** (probably after inventory exists).

#### Piece 1: holding & aiming (done)
- Rifle is always held ready; it lowers while sprinting and comes back up ≈0.25 s after sprint ends
  before it can fire.
- **ADS:** right mouse / left trigger. Hold/Toggle setting per device (defaults: keyboard Toggle, gamepad Hold).
  - ≈0.3 s to aim (slower while moving fast). Walking speed while aiming; aiming stops a sprint.
  - Separate look sensitivity while aiming (mouse and gamepad settings).
  - First person: look through the iron sights (FOV 75° → 60°).
  - Third person: setting **Switch to first person** (default) or **Over the shoulder**
    (camera closer, FOV 55°, a small **aim dot** shows where the barrel really points — the only
    on-screen aiming aid in the game).
- **Third-person aim:** the weapon converges on what the camera center looks at; if the barrel is
  blocked (camera sees over cover the gun can't), the barrel's line wins.
- **Sway:** gentle idle sway, larger while moving, much larger when out of breath (low leg stamina).
  Crouch and prone reduce it. Higher Aiming skill reduces it (to 60% at level 10).
- **Hold breath:** sprint button while aiming (keyboard: hold Shift; gamepad: click L3, click again to
  release). Duration is a flat timer from the **Aiming** skill (4 s at level 0 → 10 s at level 10).
  Running out = 2 s gasp with heavy sway; breath must refill to 50% before holding again.
- **Weapon collision:** near walls the rifle pulls back toward the body; pushed far enough it lowers
  and can't fire.
- **Gamepad auto-lean:** aiming with cover right in front leans toward the open side (right first).
- **Peek over cover:** aiming while crouched behind low cover rises you up (to 1.55 m) to aim over it.
- **Aim assist (gamepad only, built in piece 2 once there are targets):** grippy, Apex/modern-CoD
  style: slowdown over targets + rotational tracking of moving targets. Strength setting (incl. off).
  Never through walls; only hostile targets.

#### Piece 2: firing & ballistics (done)
- **Fire modes:** semi and full-auto (≈750 rpm). Switch: B (keyboard) / D-pad left (gamepad).
  Fire: left mouse / right trigger. Pressing fire stops a sprint. Unlimited ammo until piece 4.
- **Projectiles:** every bullet is simulated (no hitscan): ≈900 m/s muzzle velocity, real gravity
  (9.81 m/s², not the character multiplier), air drag (≈70% speed left at 300 m).
- **Zeroing:** sights zeroed at 100 m: the bullet crosses the sight line there, hitting slightly
  high/low at other ranges.
- **Hit feedback:** bullet holes, a dust puff, a short impact sound. **No hit markers.**
- **Penetration:** designed for later (materials, buildings). For now everything stops bullets.
- **Sound:** code-generated placeholder shot and impact sounds until real audio exists.
- **Targets:** plain targets in the test range plus a long-range lane (25–300 m), one moving target.
- **Aiming XP:** hitting a target earns Aiming XP (more at longer range).
- **Aim assist** (gamepad, see piece 1 spec) lands with this piece; targets stand in for enemies.
- **No recoil yet** (piece 3): full-auto is a laser until then.
- Verified: drop vs sight line −3.6 cm @25 m, 0 @100 m, −8.7 cm @200 m, −36 cm @300 m (≈645 m/s,
  0.4 s flight). At 300 m you must hold over and hold your breath, like the real thing.
- Pulling the trigger ends a sprint; press sprint again to resume.

### Skills
- Levels 0–10, new characters start at 3. Skills level up **by doing**, and each level
  needs more XP than the last. Exact curves live in a tunable resource.
- **Fitness** (implemented in step 1): raises max leg stamina (≈42 at level 0 → 150 at level 10: about 4.4 s of sprinting at level 1, 6 s at the starting level 3, 12.5 s at level 10), stamina regen
  (8/s at level 0 → 14/s at level 10), and slightly raises sprint speed (≈ +8%) and acceleration (≈ +10%).
  Earns XP from sprinting and jumping, with more XP when stamina is low.
- **Strength** (defined now, implemented with inventory in step 6): carry capacity.
- **Aiming** (step 2): hold-breath duration (4 s → 10 s) and less weapon sway. Earns XP from hits (piece 2).
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
1. ✅ Player controller + first/third person camera + stats component + leg stamina
2. Gunplay core (weapons usable by any character). Includes, because they need aiming down sights:
   - **Gamepad auto-lean:** aiming down sights beside a corner/cover edge leans around it (CoD-style).
   - **Peek over cover:** aiming while crouched behind waist-high cover raises you to aim over it (gamepad and keyboard).
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
- Fine stance height
- Manual lean on gamepad (decided: no)
- Arm stamina
- Free-look
- Skill decay over time
- Aim assist for gamepad (decide during gunplay step)
- AI chat-model NPCs (dropped)
- Multiplayer / co-op
