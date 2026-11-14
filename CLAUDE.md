# CLAUDE.md — instructions for AI coding sessions

Read `DESIGN.md` first. It is the source of truth for scope. **Do not build anything
that isn't in DESIGN.md.** Suggest new ideas for the Parking Lot instead.

## Working style
- The user wants to go slowly, piece by piece: discuss a system, agree on the spec,
  write it into DESIGN.md, *then* code it.
- Build one build-order step at a time and refine it before moving on.
- The user tests by pulling with GitHub Desktop and opening the project in Godot 4.7.x.
  After each change, tell them exactly what to test and which keys to press.

## Branches
- `main`: tested, working builds only.
- `preview`: new, untested work. All new code goes here first.
- The user merges `preview` → `main` once they've tested it.

## Code conventions
- **C#** (Godot .NET build). Namespace root: `ZombieSandbox` (e.g. `ZombieSandbox.Player`).
- C# script files are `PascalCase.cs` and **must match the class name** (Godot requirement).
  Classes extending Godot types must be `partial`.
- Scenes, resources and folders: `snake_case`.
- Use `[Export]` for inspector-tunable fields; `[GlobalClass]` on custom `Resource` types.
- Enable nullable reference types; avoid allocations in per-frame code.
- After pulling C# changes the user must build (hammer icon or F5) before running.
- Organize by feature: `game/<feature>/` holds that feature's scenes **and** scripts together.
  - `game/core/`: shared systems (IDs, save, events)
  - `game/player/`, `game/combat/`, `game/ai/`, `game/world/`, ...
- `levels/`: playable maps and graybox test levels.
- `assets/`: imported art and audio (`models/`, `textures/`, `materials/`, `audio/`).
- Tunable numbers live in `Resource` files (`.tres`), not hard-coded constants.
- All input via InputMap actions with both keyboard/mouse and gamepad bindings; never read raw keys.
- Prefer composition (child component nodes) over deep inheritance.
- Player, zombie and NPC must share the same health, weapon and perception components.
- Anything persistent needs a stable unique ID and save/restore methods.
- Hand-written `.tscn` files must stay valid Godot 4 text-scene format.
