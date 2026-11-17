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
| Reload: tactical / fast | R / double-tap R | Tap X / hold X |
| Check magazine / inspect weapon | Tap T / hold T | Tap / hold D-pad right |
| Clear a jam: tap-rack / inspect & clear | Tap R / hold R | Tap X / hold X |
| Health screen (body diagram) | H | View / Back |
| Interact (pick up) | F | X (when looking at an item) |

If the gamepad crouch is set to *Hold*, tap/hold on one button no longer works, so
prone moves to D-pad down.

### 2. Gunplay (core done — attachments after inventory)

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
6. **Attachments** — moved to after inventory (step 6).

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

#### Piece 3: recoil (done)
- **Tarkov-style:** each shot kicks the gun (mostly up, random sideways within limits) and it
  returns **all the way** to the point of aim on its own. The player never pulls down.
- **Gun kicks more than the view:** the camera follows ≈30% of the kick, so you see the rifle jump;
  the barrel always ends up where the full kick puts it.
- **Full auto:** besides each shot's kick, the point of aim **climbs** a little per shot (≈0.13°)
  and levels off (≈0.75° × the recoil multiplier). The camera follows most of the climb, so
  you see the sights walk up. It settles all the way back after the burst. Vertical kick eases
  after the first few shots; sideways spread widens a little in long bursts.
- **Modifiers:** hip fire ×1.6, crouch ×0.85, prone ×0.6, weapon recoil stats (attachments will
  change these later), and the **Strength** skill (×1.25 at level 0 → ×0.7 at level 10).
- **Camera shake:** small per-shot shake, on by default, with a 0–100% setting (0 = off).
  The view kick itself is part of recoil and isn't affected by this setting.
- Measured (graybox rifle, standing, aimed): single shot kicks ≈0.9° (Strength 3) and is back in
  ≈0.38 s. 30-round full auto at 50 m: Strength 0 ≈105×117 cm, Strength 3 ≈78×103 cm (view climbs
  0.4° → 0.8°), Strength 10 ≈65×66 cm. Short bursts stay much tighter.
- Every trigger press fires, even a click shorter than one frame.

#### Piece 4: magazines & reloading (done)
- **Magazines are items** with their own round count (built as item data now; the temporary
  **magazine pouch**, 6 slots, becomes part of the inventory/rig in step 6). Start: 1 in the gun +
  4 full 30-round mags. Loose ammo and loading mags come with inventory.
- **No ammo counter.** Check the magazine (T / D-pad right): takes a moment and gives an estimate.
  Precision grows with the Reloading skill: rough at low levels ("feels about half"), finer steps
  mid-way, within a few rounds at high levels, exact at 10.
- **Chamber:** a reload before empty keeps the chambered round (30+1). Running dry means the
  reload also racks the charging handle (slower). Pulling the trigger on an empty gun just clicks.
- **Reloads** always take the **fullest** magazine from the pouch.
  - **Tactical** (tap R / tap X): old mag goes back into the pouch (dropped if the pouch is full).
  - **Fast** (double-tap R / hold X): old mag is dropped on the ground. Faster.
  - Starting tactical and then double-tapping/holding switches to fast if the old mag isn't out yet.
  - Walking is fine; **sprinting cancels** the reload. Cancelled after the old mag is out = no mag in
    the gun until you reload again. Can't aim or fire while reloading.
- **Dropped magazines** are physical objects. Look at one within reach and press **F** (keyboard)
  or **X** (gamepad: X is "interact" when looking at an item, otherwise reload) to put it back in
  the pouch. First use of the interaction system (doors, loot later).
- **Reloading skill** (new): reload times scale ×1.0 at level 0 (civilian: tactical 4.2 s, fast
  3.0 s, +1.3 s when empty) → ×0.5 at level 10. XP from reloads and magazine checks.
- **Interaction targeting is forgiving:** if the exact view center isn't on something usable, the
  nearest item within ≈10° of it (and within 2.2 m, with nothing in between) is targeted. Small
  items on the floor would otherwise be nearly impossible to point at without a crosshair.
- **Developer keys:** Ctrl+7/8 Reloading −/+, Ctrl+R refill ammo.

#### Piece 5: malfunctions (done)
- **Weapon condition** 0–100% per weapon. Each shot wears it a little (≈0.01% → ~10,000 rounds
  from new to ruined; the Maintenance skill halves wear at level 10). Repairs come later.
- **Jam chance per shot** from condition (tunable): ≈1/5000 at 100%, 1/2000 at 80%, 1/1200 at 70%,
  1/100 at 30%, 1/25 at 0%. **Heat** from sustained fire raises it (up to ≈×2 after long full auto)
  and wears the gun up to ×1.5 faster. Heat is capped and cools off when you stop.
- **Malfunctions:** misfire (dud: click, no shot), failure to eject (stuck casing), failure to feed,
  double feed (rarest; more likely in poor condition). No on-screen warning: the gun clicks or stops.
- **Jams start unknown.** Ways to deal with one:
  1. **Inspect** (hold T / hold D-pad right, ≈1.2 s): tells you the jam type and the weapon's
     condition ("good condition", "badly worn"...). Then reload does the right fix.
  2. **Tap-rack** (tap R / tap X, ≈1.2 s): clears misfires, failures to eject and failures to feed,
     but **not** a double feed ("Still jammed" — now you know to inspect).
  3. **Inspect & clear combo** (hold R / hold X while jammed): inspects, then does the correct fix
     automatically. One button, slower than a correct tap-rack. The controller-friendly path.
  - Double-feed fix ≈4 s (magazine out, rack several times, magazine back in); loses a round.
  - Tap T still checks the magazine; holding it inspects the weapon instead.
  - While the gun isn't jammed, R/X work exactly as before (tap = tactical, double-tap/hold = fast).
- **Maintenance skill** (new): clearing and inspection ×1.0 at level 0 → ×0.5 at level 10, weapon wear
  ×1.0 → ×0.5; repairs later. XP from clearing jams and inspecting.
- Developer keys: Ctrl+9/0 Maintenance −/+, Ctrl+K cycle condition (100/70/40/15/0%), Ctrl+J jam next shot.

### 3. Health & damage (piece 1 done; healing with inventory)
Shared by player, zombies and NPCs (one Health component).

#### Piece 1: body, damage, feedback (done)
- **Body parts** (max HP): head 35, chest 85, stomach 70, arms 60 each, legs 65 each.
  - Head or chest at 0 = death. Other parts at 0 are **destroyed**: further damage to them spreads
    to the rest of the body (×0.7).
- **Hits land on body-part hitboxes.** Bullets do the ammo's damage (5.56 ≈ 55, less as the bullet
  slows down): ≈2 chest hits or 1 head hit kills.
- **Bleeding:** hits can cause light (≈0.5 HP/s) or heavy (≈2 HP/s) bleeding on that part.
- **Fractures** (bullets to limbs, falls): broken leg = slow limp (55% speed per broken leg), no
  sprint; broken arm = much more sway. Destroyed leg: 40% speed; destroyed arms: huge sway.
- **Pain** builds with damage, fractures and destroyed parts, fades over time: shakier aim, screen effects.
- **Fall damage** from drops over ≈3 m (legs first, fracture chance grows with height; ≈10 m+ is
  usually lethal).
- **Wound types** (design now): scratch, laceration, bullet wound, bite. Bites and infection are
  switched on with zombies (step 4).
- **Armor** (helmets, plates) is inventory (step 6); the damage pipeline already has a place for it.
- **Health screen:** a key (H / gamepad View) toggles a **body diagram** any time. It shows bleeding and
  fractures, and an **estimate** of each part's health based on the **Medical** skill:
  low = rough words ("hurt", "badly hurt"), mid = finer words, high = approximate HP, 10 = exact.
- **No HUD health bar.** Instead: hit flash, red vignette that grows as health drops, pulsing edges
  while bleeding, blur/desaturation with pain, heartbeat and hurt sounds (placeholder until real audio).
- **Medical skill** (new): estimate precision now; healing speed/effectiveness with items (piece 2).
  XP from taking damage now, from using medical items later.
- **Death:** "You died" screen and restart (permadeath: a new character).
- Verified: 2 chest / 1 head hit kills at 15 m; own bullets never hit you; heavy bleed ≈2 HP/s;
  falls: 3.5 m scrape, 7 m legs nearly destroyed (often broken), 11 m+ usually fatal.
- Training dummies on the range (respawn after 4 s) and a fall-test tower (3.5 / 7 / 11 m).
- Developer keys: Ctrl+H hurt self, Ctrl+N break a leg, Ctrl+G heal all (also revives),
  Ctrl+− / Ctrl+= Medical −/+.

#### Piece 2: healing & medical items (with inventory, step 6)

### 4. Zombies & AI foundation (done)

#### Vision for 1.0
- Zombies start as **shamblers**. Over the first few in-game months the virus **mutates**, producing
  tougher, faster **runners**. At 1.0 this is a sandbox setting players can turn off or customize.
- **Infection is punishing and scary:** any wound from the infected (scratch, laceration, bite) or blood
  transfer has an **85–95%** infection chance. Infection is hidden at first, then symptoms, then death
  over roughly 2–3 game days.
- **Game clock:** 1 game day = 3 real hours (2 h daylight, 1 h night). Seasons with varying daylight and
  weather come later.

#### Piece A: clock, senses, navigation, zombie (done)
- **World clock** (autoload): game time flows at 1 game day / 3 real hours; daylight 06:00–22:00
  (2 real hours), night 22:00–06:00 (1 real hour). Sun moves; nights are dark. Debug: speed up time.
- **Noise system** (shared): every sound is an event with a loudness radius. Rifle shot ≈250 m,
  sprinting ≈12 m, walking ≈5 m, crouch-walking ≈2 m, jumping/landing, reloading and dropped mags
  in between. Anything in range hears it (closer = more certain of where).
- **Sight** (shared): ≈25 m in daylight (much less at night), within a field of view, needs line of
  sight. Crouching, prone and standing still make you harder to spot; spotting takes a moment unless
  you're close.
- **Navigation:** zombies path around obstacles (Godot navigation; per-chunk nav data later).
- **Zombie behavior:** wander → investigate sounds → chase what they see → lose you → search the last
  known position → **educated guess:** continue along the direction and speed you were moving for as
  long as you've been gone → sweep ahead in that direction → wander. New sounds are fresh leads;
  sounds heard while it could see you are old news. (`PredictsMovement` on the zombie type, so
  dumber types can skip the guess.) Shamblers ≈1.0–1.6 m/s with variety (stats from a zombie type resource,
  so runners can be added later). Graybox body, Meshy model later.
- **Killing zombies:** destroying the **head** (brain) kills instantly. Zombie chest/stomach have lots
  of HP; a destroyed chest means it staggers and collapses from **blood loss** (≈3 rifle hits).
  Destroyed legs make it a **crawler**. Arms damage does little.
- Test range: 3 zombies wandering behind the spawn; navigation baked from level geometry on load.
- Developer keys: Ctrl+Z spawn a zombie 20 m ahead, Ctrl+P cycle time speed (×1/×60/×600),
  Ctrl+O skip 3 game hours. Zombies show their state/awareness above their heads (dev builds).

#### Piece B: attacks, wounds, infection, shove (done)
- Zombies grab and attack: scratches, lacerations, bites to a body part (bites mostly when grabbed).
  Being grabbed slows you hard.
- Wounds bleed and carry the 85–95% infection chance. Infection: hidden → symptoms (shown on the health
  screen, screen effects) → health drains → death.
- **Shove / rifle-butt strike** (G / gamepad RB): short-range push that staggers, sometimes knocks
  down, breaks a grab. Costs stamina, short cooldown, small damage.
- Details as built: attacks have a ≈0.6 s lunge wind-up (back off or shove to avoid it); 35% of landed
  attacks become a grab; while grabbed you move at ≈0.7 m/s and it bites every ≈1.3 s until shoved off
  or killed. Passing swipes are mostly scratches; grabs are mostly bites. Shove: ≈1.9 m reach, 70°
  in front (or any zombie holding you), 8 stamina (weaker when tired), 0.9 s cooldown, knockdown ≈30%
  on a healthy zombie (more with damaged legs), 2.5 s on the ground.
- Infection is never labeled: the health screen shows wounds and symptoms only ("Feverish, aching",
  "Very sick, nauseous"). Fever ≈6–12 game h after infection, sickness drains the body, death after
  ≈48–72 game h ("You succumbed to the infection"). Fever also adds sway, pain and lowers stamina.
- Developer keys: Ctrl+U infect self, Ctrl+G heal also cures; Ctrl+P speeds time up to watch it.

### Skills
- Levels 0–10, new characters start at 3. Skills level up **by doing**, and each level
  needs more XP than the last. Exact curves live in a tunable resource.
- **Fitness** (implemented in step 1): raises max leg stamina (≈42 at level 0 → 150 at level 10: about 4.4 s of sprinting at level 1, 6 s at the starting level 3, 12.5 s at level 10), stamina regen
  (8/s at level 0 → 14/s at level 10), and slightly raises sprint speed (≈ +8%) and acceleration (≈ +10%).
  Earns XP from sprinting and jumping, with more XP when stamina is low.
- **Strength**: reduces weapon recoil (gunplay piece 3, ×1.25 at level 0 → ×0.7 at level 10); carry capacity comes
  with inventory (step 6).
- **Medical** (step 3): health estimates on the body diagram; healing with items later.
- **Maintenance** (step 2): jam clearing and inspection speed, slower weapon wear; repairs later.
- **Reloading** (step 2): reload and magazine-check speed, magazine-check precision.
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
2. ✅ Gunplay core (weapons usable by any character). Includes, because they need aiming down sights:
   - **Gamepad auto-lean:** aiming down sights beside a corner/cover edge leans around it (CoD-style).
   - **Peek over cover:** aiming while crouched behind waist-high cover raises you to aim over it (gamepad and keyboard).
3. ✅ Health & damage — body parts, bleeding, fractures (shared by everyone)
4. ✅ AI foundation — perception (sight/hearing) + navigation, zombie as first user
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
- Virus mutation / runners (planned for 1.0, see step 4)
- Seasons and weather
