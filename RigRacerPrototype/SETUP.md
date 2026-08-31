# RigRacer — Level 0 deterministic simracer prototype

A Unity (2021 LTS+) prototype built on top of your `CarVisualRig`. It adds a
deterministic, frame-rate-independent vehicle simulation: ICE engine, slip-based
tyres, double-wishbone suspension with ARB + heave spring + 4-way damping + bump
stops, anti-ackermann front steering, camber/toe reflection, and a telemetry HUD.

> **Scope of Level 0:** flat, collidable terrain only. One raycast per corner is
> used for the suspension contact (stable and deterministic). The cylindrical /
> mesh-cast tyre contact (so the *depth* of the tyre follows obstacles) is a later
> stage — see *Roadmap* at the bottom.

---

## 1. Project setup

1. Create a new 3D Unity project (URP or Built-in, either works).
2. Copy the `Scripts/` folder from this prototype into `Assets/Scripts/`.
   Resulting structure:
   ```
   Assets/Scripts/Core/VehicleCore.cs
   Assets/Scripts/CarVisualRig.cs
   Assets/Scripts/CarController.cs
   Assets/Scripts/CarInputManager.cs
   Assets/Scripts/TelemetryHUD.cs
   ```
3. No external packages or assets are required.

## 2. Build the collidable flat terrain

1. In the Hierarchy: **GameObject → 3D Object → Plane** (this is your ground).
2. Set its Transform:
   - Position `(0, 0, 0)`, Rotation `(0,0,0)`, Scale `(10, 1, 10)` (a 100 m x 100 m pad).
3. Ensure it has a **Mesh Collider** (a Plane comes with one by default). This is
   what the suspension raycasts hit. *Do not* remove the collider.
4. Create a Layer called **Ground** and assign the Plane to it
   (Layers dropdown → Add Layer → "Ground"; then set the Plane's layer).
5. Leave the car's `groundMask` set to include the **Ground** layer
   (`CarController.groundMask` → enable "Ground").

> Tip: a Plane's surface is at local y = 0, so the car spawns 2.2 m above it and
> drops onto the suspension, letting you watch it settle to its true ride height.

## 3. Create the car

1. In the Hierarchy: **GameObject → Create Empty**, name it **Car**.
2. Add the components (in order so `[RequireComponent]` is satisfied):
   - **CarVisualRig** (auto-creates 4 transparent wheel cylinders as children)
   - **CarController** (auto-added dependency; references the rig)
   - **CarInputManager**
   - **TelemetryHUD**
3. Wire references in the Inspector:
   - `CarController.rig` → the Car (it self-fills, but verify)
   - `CarController.input` → the CarInputManager (same object or another)
   - `CarController.hud` → the TelemetryHUD
   - `TelemetryHUD.car` → the CarController
4. Set `CarController.spawnPosition` to something like `(0, 2.2, 0)` (in the air,
   above the terrain — this is where **R** resets the car).
5. Press **Play**. The car drops, settles, and the HUD appears bottom-left.

## 4. Controls

| Action            | QWERTY | AZERTY | Reverse behaviour                              |
|-------------------|--------|--------|------------------------------------------------|
| Accelerate        | W      | Z      | In Reverse: acts as **brake**                  |
| Brake             | S      | S      | In Reverse: acts as **throttle** (moves back)  |
| Steer left        | A      | Q      | —                                              |
| Steer right       | D      | D      | —                                              |
| Reset to spawn    | R      | R      | Drops the car back in the air, zeroed state    |

**Reverse engagement:** while nearly stopped, hold **S** (~0.2 s) to engage Reverse
(gear shows `R`); **S** then drives backward and **W** brakes. Hold **W** while
stopped in Reverse to return to forward drive.

## 5b. Track width is respected at runtime

`CarController` places each wheel at `±trackWidth/2` (front/rear configurable), so changing
`frontTrackWidth`/`rearTrackWidth` on the rig spreads the wheels correctly during play
(previously the wheels were pinned to the fixed mount X). Editable live in the Inspector.

## 5. Editor rig visualization

Select the Car and look in the Scene view:

- **Blue** box = chassis, **cyan** wire = chassis bounds.
- **Magenta** sphere = center of mass.
- **Red** spheres = the **16 inner wishbone mounts** (4 per corner: upper-front,
  upper-rear, lower-front, lower-rear).
- **Green** lines = the double-wishbone arms (inner mounts → outer ball joint).
- **Yellow** spheres/line = the **8 outer mount points** (2 per corner: upper &
  lower ball joints, linked by the upright).
- **Orange arc** = the arc each wishbone outer joint traces about its inner pivot
  axis across the suspension travel (~10 cm). This is the *double-wishbone arc
  motion* preview. It follows the live compression while playing.

Use `CarVisualRig.suspensionPreview` (0..1) to scrub the arc in Edit mode, and
`symmetricEditing` to keep left/right mounts mirrored.

## 6. Determinism notes (important for later mobile testing)

- Physics only advances in fixed increments of `CarController.fixedStep`
  (default `1/200`s) using an accumulator, so the outcome is identical regardless
  of render frame rate. The same input list → same trajectory.
- `CarController.recordInputs` / `playbackInputs` capture and re-play the exact
  input stream for reproducible tests (e.g. "car must not jump high at 300 kph
  over a kerb"). Record a run on PC, play it back on device — identical result if
  the fixed step and initial state match.
- Bump stops (`bumpStopStart`/`bumpStopK`) plus 4-way damping keep the car planted;
  with `maxTravel = 0.10` m the body cannot launch off the suspension.

## 7. Tuning starting points (Inspector)

- Engine: `maxPowerHp`, `torqueCurve`, `gearRatios`, `finalDrive`. Top speed and
  horsepower are shown in the HUD and derived automatically.
- Suspension per axle (`frontSusp`/`rearSusp`): `cornerSpringK`, `bumpLow/High`,
  `reboundLow/High`, `bumpStopStart`, `bumpStopK`, `arbStiffness`, `heaveK`,
  `heaveDamping`, `restLength`, `maxTravel`.
- Tyres per axle: `muX`, `muY`, Magic-Formula `B/C/E`, `camberThrust`, `wheelInertia`.
- Steering: `maxSteerAngle`, `ackermann` (negative = **anti**-ackermann),
  `camberGainPerMeter`.

## 8. Roadmap (not in Level 0)

1. **Tyre depth contact:** replace the single suspension raycast with a
   cylindrical sweep / mesh cast per wheel so the contact patch follows the
   *shape* of the tyre against obstacles and kerbs (depth-reactive).
2. **Collidable body:** add a rigidbody/collider shell on the chassis so the body
   itself interacts with terrain and other objects.
3. **Kerb / obstacle testing** on PC with recorded inputs, then port the exact
   fixed-step loop to mobile (same `fixedStep`, same initial state).
4. Mobile input layer (touch steering/throttle) feeding the same `CarInput`.

## 9. Camera (follow the car)

The prototype has no built-in camera, so add a chase camera:

1. Select the scene's **Main Camera** (or create one: GameObject → Camera).
2. Add the **CarChaseCamera** component to it.
3. Set `CarChaseCamera.target` to the **Car** (it also auto-finds the
   CarController if left empty).
4. Tune in the Inspector: `distance` (behind), `height` (above),
   `lookAhead`, and `positionLerp`/`lookLerp` (smoothing). The camera rises
   slightly with speed and stays above the ground plane.

It runs in `LateUpdate` so it sits behind the car's physics each frame.
