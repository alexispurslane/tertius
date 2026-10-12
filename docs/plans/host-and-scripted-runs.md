# Host and scripted runs

Ticket: `ter-dtw2`. The user approved this design.

## Goal

Wire all modules into a running program, and let tests and agents drive it with scripts. `src/Tertius.Host` and `src/Tertius.Demo` are empty projects today. The empty Demo project has no `Main` method, so `dotnet build Tertius.slnx` fails on it.

## Host

- The host opens the window, reads input, runs the simulation at its fixed tick rate and renders with interpolation between the last two ticks.
- The host turns input into events and posts them with `World.Post`.
- Headless mode renders to an offscreen image with no window.

## Scripted runs

- A script is a JSON file of timed steps: events to post before a given tick, image captures and state dumps.
- In a scripted run, ticks run in lock-step with the script, not with the wall clock. Rendering uses interpolation alpha 1, so every frame is deterministic.
- A run writes PNG images and JSON state dumps. A state dump holds poses, frames, recent events and errors.
- The command line is `dotnet run --project src/Tertius.Demo -- --script scripts/x.json --headless --out captures/x`.
- xUnit tests call the same code, so an integration test is a script plus assertions.

## Script format

Ticket `ter-hynj` decided this format. The user chose each point marked (User) between two designs.

A scene is a C# function that fills a new `World` and gives scene ids to some of its entities. The `SceneInstance` that it returns maps each scene id to its entity, as in the content formats design. A script names its scene, and the Demo finds the scene in its table of scenes.

```json
{
  "scene": "drop-box",
  "steps": [
    { "do": "post", "to": { "kind": "topic", "name": "input" }, "event": "InputEvent", "payload": { "kind": "keyDown", "code": 87 } },
    { "do": "run", "ticks": 60 },
    { "do": "dump", "name": "after-one-second" }
  ]
}
```

- The steps run in file order. Only a `run` step advances time. (User) This is the shape of the editor scripts in the editor design.
- A `run` step runs `ticks` ticks, at least 1.
- A `post` step calls `World.Post` at once. The event joins the routing at the end of the next tick, and its recipients handle it during the tick after that.
- A `dump` step writes the state of the world to `<out>/<name>.json`. A dump name follows the grammar of a scene id, and two dumps cannot have one name.
- The run ends after the last step.
- `to` is a union with `kind`: `{"kind": "entity", "id": "box"}`, `{"kind": "topic", "name": "input"}`, `{"kind": "frame", "id": "ship"}` (every entity in the frame that the entity `ship` owns), or `{"kind": "broadcast"}`.
- `event` is the C# name of a payload type, such as `InputEvent`. The runner finds the type through reflection over the loaded assemblies. (User) Two payload types with one name make that name an error.
- `payload` holds the fields of the payload by their camelCase names. A missing `payload` is the same as `{}`. A key for a read-only member is an unknown key, and a number out of the range of its field is an error.
- A target, or an entity field of a payload, must name a live entity.
- A payload type must fit an event: no field that holds a reference, and at most 48 bytes. The runner checks this, because a type that reflection finds passed no compiler check.
- The JSON follows the conventions of the content formats design: camelCase keys and enum values, `[x, y, z]` vectors, `[x, y, z, w]` quaternions, scene ids for entities, no comments, and an unknown key is an error.

The runner checks every step before the first tick. A script that it cannot run changes nothing.

## Captures and the camera

Ticket `ter-1i4z` added these. The user chose each point marked (User).

```json
{
  "scene": "drop-box",
  "window": { "width": 1280, "height": 720 },
  "steps": [
    { "do": "camera", "entity": "camera" },
    { "do": "capture", "name": "start" }
  ]
}
```

- A `capture` step renders the current state with the headless renderer and writes `<out>/<name>.png`. A capture name follows the grammar of a scene id, and two captures cannot have one name.
- `window` sets the image size in pixels, 1 to 16384 on each side. Without it, the size is 1280 by 720.
- A `camera` step makes a live entity the camera of the world, `World.Camera`. The camera is the entity's pose composed with the view offset of its render row. (User) The scene picks the camera at the start.
- A scripted run renders with interpolation alpha 1 and no impostor captures. So on one machine, the captures of a script are the same in every run.
- A capture needs a live camera, a renderer and an output folder. The runner checks all three before the first tick. If a component destroys the camera during the run, the run stops at the next capture or camera step. The error names the step and the tick.
- The command makes a headless renderer only for a script that has a capture step. So a script with dumps only runs with no GPU.
- A scene gets the resource half of the renderer, `IRenderResources`, to make its meshes and materials. A run with no renderer gives the scene `RenderResources.None`, whose meshes are `MeshId.None`. The scene makes the same entities either way.
- A dump names the camera in `camera`.
- The window comes with input in ticket `ter-mu7k`. (User) It plays a script at real time, 60 ticks a second, and `--speed` changes the rate. When the script ends, the window closes.

## The window

Ticket `ter-mu7k` added the window. The user chose each point marked (User).

```bash
dotnet run --project src/Tertius.Demo -- --script scripts/drop-box.json --out captures/drop-box --speed 0.5
dotnet run --project src/Tertius.Demo -- --scene drop-box
```

- Without `--headless`, the command opens a window. With `--script`, the window plays the script and closes after the last step. With `--scene`, it runs the scene live until you close the window. (User)
- Separate threads do the work, as in "Threads and the tick" of ARCHITECTURE.md. (User) The main thread runs the window and its input. The simulation thread runs the ticks and the script. The render thread draws the latest render snapshot. The gameplay workers and Jolt run as during any tick.
- The simulation thread runs 60 ticks a second of wall-clock time, times `--speed`. A tick late by less than one tick runs at once. A tick that starts later than that, after a stall such as a capture, moves the schedule by its delay. So the ticks after a stall keep their pace, instead of rushing to catch up, and no tick is ever skipped.
- The render thread interpolates between the last two ticks. The interpolation alpha is the wall-clock time since the newest snapshot, divided by the wall-clock length of one tick.
- A capture step works in the window. (User) The simulation thread waits while the render thread draws that tick with alpha 1 and saves it. The image has the size of the window's framebuffer. On a Retina display, that is twice the size in the `window` key. On a screen that is smaller than the `window` key, macOS makes the window smaller, and the images are smaller too.
- Keys and mouse movement become `InputEvent` messages to the `input` topic in free play. During a script, live input reaches no entity, so a script runs the same with or without a person at the keyboard. `InputRouter` holds these rules. The game never sees Esc or F1, and every key or button down that it sees gets its release: switching the fly camera on releases the keys and buttons that the game holds.
- In free play the window captures the cursor, so that the mouse can turn the game's view. (User) During a script it captures the cursor only while the fly camera is on. To reach other apps, use Cmd-Tab.
- F1 switches the view to a debug fly camera and back. (User) WASD moves it, Q and E move it down and up, Shift moves it ten times faster, and the mouse turns it. It starts at the world's camera and moves in the axes of that camera's frame, relative to that camera. When the camera entity moves into another frame, as when it leaves a ship's bubble, the fly camera starts again at the world's camera. The render thread sets it in the render snapshot, so it changes nothing in the world.
- Esc closes the window. If you close the window during a script, the run stops, and the dumps and captures until then stay on disk.

## State dumps

```json
{
  "name": "after-one-second",
  "tick": 60,
  "time": 1,
  "camera": "camera",
  "entities": [
    {
      "id": "box",
      "pose": { "frame": "ground", "position": [0, 0.5, 0], "rotation": [0, 0, 0, 1] },
      "rootPose": { "frame": "ground", "position": [0, 0.5, 0], "rotation": [0, 0, 0, 1] },
      "velocity": [0, 0, 0],
      "angularVelocity": [0, 0, 0]
    }
  ],
  "frames": [ { "id": "ground", "parent": null, "driver": "static" } ],
  "events": [ { "tick": 31, "event": "Collision", "to": { "kind": "entity", "id": "box" }, "from": null, "payload": { "other": "floor", "point": [0, 0, 0], "normal": [0, 1, 0], "impulse": 12.5 } } ],
  "errors": []
}
```

- `tick` is `World.TickNumber`, the number of completed ticks.
- `entities` lists every live entity in slot order. An entity without a scene id appears as `#index.generation`.
- `pose` is the pose in the entity's own frame. `rootPose` is the pose in the root frame of its frame tree. (User) The owner of a root frame has no frame, so its `pose.frame` is null.
- `frames` lists every frame in the dense order of the frame tree, which puts each parent before its children.
- `events` lists every event that the sync points routed since the previous dump, or since the start of the run. (User) Each event carries the tick count after the sync point that routed it. The order is by tick, then routing order.
- `errors` is `World.Errors` at the time of the dump, oldest first.
- `World.RoutedEvents` is the source of `events`: the events that the last sync point routed, in routing order. `Tick` and `Spawned` go straight into each entity's own event list, so they are never routed and never in a dump.
- A number that JSON cannot hold, such as NaN, appears as a string, such as `"NaN"`. So a dump of a broken state still writes.

## Command line

```
dotnet run --project src/Tertius.Demo -- --script scripts/drop-box.json --headless --out captures/drop-box
```

- The command builds the scene that the script names, in a new `World` on the Jolt backend, and runs the script.
- It writes the path of each dump and each captured image to standard output. It writes each problem and the world's log to standard error.
- With `--headless`, the ticks run back to back and no window opens. Without it, a window opens (see "The window").
- The exit codes are the codes of the `tertius` command line in the content packages design:
  - 0: the run finished, or the user closed its window.
  - 1: the command line is wrong.
  - 2: a file cannot be read or written.
  - 3: the script cannot run, or it names an unknown scene.
  - 4: an internal error, which is a bug.
  - 5: the renderer or the window cannot start, for example on a machine with no Vulkan driver.

## The vertical slice

The demo shows the engine's main idea working end to end:

1. A ship orbits a planet.
2. A person walks inside the ship while it orbits.
3. The person goes through an airlock to the outside.
4. The person throws an object from one frame into another.

Step 1, the ship's orbit (tickets `ter-fe04` and `ter-uucg`), is the Demo's scene `orbit` and the script `scripts/orbit.json`. The user chose each point marked (User).

- The frame tree follows the real sky. (User) The sun is the root frame, with the sun's gravity and real radius, and it is the light. The frame `earth` goes round it on Earth's real orbit and does not turn. It leans by Earth's 23.44 degree obliquity, so its axes follow the equator. The frame `earth-surface` sits at Earth's center and turns once per sidereal day with the Spin driver (User). It draws the planet and casts its shadow.
- The ship falls freely round Earth: Jolt integrates it in Earth's gravity, with no damping. (User) It starts on a circle 400 km up, tilted 51.64 degrees to the equator as the ISS flies, once in 92.4 minutes. Its spin keeps its belly toward Earth and its nose along its motion. Over one orbit it falls about 350 m behind the exact circle, and the lag grows by about 0.4 km per orbit, about 7 km per day, because Jolt keeps velocities as floats. Its height swings by about 64 m and its place along the track by about 128 m, because Jolt adds each tick's pull to the velocity before it moves the ship, and the start velocity is the exact one, not one offset by half a tick of gravity. Compare a dump with the circle at the dump's own `time`: the script's quarters each end 2.9 ms late. The ship holds its attitude only because Jolt leaves out the gyroscopic torque and the scene leaves out the gravity-gradient torque. Its spin axis is its middle axis of inertia, so with the gyroscopic torque on, it would tumble within a few orbits.
- The ship orbits `earth`, not `earth-surface`, because an orbit in a turning frame turns with it.
- The scene starts at the March equinox, with the sun straight above the ship. The ship is in Earth's shadow from about 110 to 250 degrees along its orbit. The script captures the chase view inside the shadow at 120 degrees, where the face that the camera sees points at the sun.
- Space has no sky and no ground, so the scene's ambient light is the same dim color from every direction.
- A full orbit runs headless in about 40 s, in a Release build. `--speed 60` plays it in the window in about 1.5 minutes.
- The scene has two views. (User) `chase` rides 70 m behind the ship and 12 m above it. `far` stands in `earth`, three Earth radii out, and shows the whole planet turn, where the ship is too small to see. In free play, V switches between them. A script switches them with camera steps.
- The script captures both views and dumps the state at the start and after each quarter of the orbit. A quarter is 83,172.83 ticks, and the script runs 83,173.
- Dumps keep `rootPose` relative to the root (User), which is now the sun. So a `rootPose` is about 1.5e11 m, good to 30 µm. A ship's `pose` is relative to `earth`.
- The ship's bubble has a radius of 100 m, so it holds the hull and the chase camera. The sun has the real sun's radius, so the light has the real sun's angular size at Earth's distance.
- The tests of the scene run 20 s across the far side of the ship's first circle, where the mean anomaly wraps from π to -π. The full orbit is the manual run of `scripts/orbit.json`.

Step 2, the walk (ticket `ter-ai0t`), adds a person to the scene `orbit`. The script `scripts/walk.json` and its test `WalkSliceTests` drive it.

- The hull keeps its solid box in Earth's frame, for its mass and its collisions there. Inside the ship's own frame, its interior shape is hollow: walls 0.5 m thick around a cabin 19 by 7 by 59 m. The drawn hull is the outer box plus the cabin's box turned inside out. Each box is closed and has no T-junctions.
- The ship falls freely, so its proper acceleration is zero, and only the ship's own gravity field acts inside it. The field is 9.81 m/s² toward the belly, and only inside the cabin's box. A person outside the hull floats, as the airlock step needs.
- The person is a capsule character, 1.8 m tall, of 70 kg. It stands on the cabin floor 10 m aft of the ship's center and faces the nose. Nothing that the person does pushes the ship, because the cabin floor is a static body in the ship's own frame.
- The component `PersonControl` reads the `input` topic. W, A, S and D walk at 3 m/s, Space jumps at 4 m/s, and the mouse turns the view. The person takes input only while its view is on, and it lets go of its keys when its view goes off. (User) A component reads the world's camera through `Snapshot.Camera`. A script drives the person with the same `InputEvent` posts that the window makes in free play. Only a teleport turns a character, and a teleport skips the sweep that keeps it out of walls. So the body does not turn: the turn lives in the component and in the view offset.
- Outside the cabin, gravity is below the zero-gravity threshold of physics, so the person is weightless there. Step 3 decides how a weightless person moves.
- The person's eye, 1.65 m above the floor, is the third view: V goes from `chase` to `far` to `person`.
- The hull keeps the sunlight out of the cabin, so five lamps under the roof light it. They are point lights, which cast no shadows yet. So a lamp also lights faces that a wall hides from it, such as the inside of the airlock's chamber and the top of the hatch's sill.
- The script stands the person on the floor, walks it 6 m toward the nose, turns the view a quarter turn left, walks it 6 m toward the port wall and jumps. Then it walks the person into the port wall, which stops it. The test checks each move in the ship's frame, and that the ship carries the person 49 km round Earth meanwhile. A jump rises straight off the deck and lands where it left, because the person and the deck fall together.
- The person costs about 40 µs a tick. The full orbit took 24 to 27 s before the person, and 37 to 39 s with it, in a Release build on the development Mac. A character never sleeps, so the ship's own physics system now steps on every tick. Ticket `ter-jtyn` tracks it.

Step 3, the airlock (ticket `ter-f8sh`), adds an airlock to the scene `orbit`. The script `scripts/airlock.json` and its test `AirlockSliceTests` drive it. The user chose each point marked (User).

- The airlock's chamber stands inside the cabin, against the port wall, level with the person's start: 3 by 3 m, as tall as the cabin. So the chamber has the cabin's gravity. The door `airlock-inner` leads from the cabin into the chamber, and the door `airlock-outer` closes a hatch through the hull from the chamber to the outside. Both doorways are 1.2 m wide and 2.1 m tall, with a sill 0.2 m high that the person steps over.
- A body's shape never changes after spawn, so the hatch is a hole in the hull's interior shape from the start, and each door is its own kinematic body. The component `Door` slides a door up into the wall above its doorway in 1.5 s, on the messages `OpenDoor` and `CloseDoor`. The drawn hull comes from `BoxMesh`, which cuts the doorways through the faces as four trapezoids each. So the hollow hull with its hatch is closed and has no T-junctions, and so is each wall of the chamber. The chamber's walls stand against the cabin's faces, so their corners lie on the cabin's edges, and the chamber's inner wall stands against its side walls, so its corners lie on their edges. But those faces go on behind the walls, so no crack can show there.
- E cycles the airlock. (User) The person sends `Use` to every entity within 3 m of its position. The component `Airlock` handles it: the open door closes, and 3 s after the close order the other door opens. A use during a cycle does nothing. The inner door spawns shut, and the airlock opens it when the scene starts, which takes 1.5 s. Physics has no sensors and no ray casts, so the reach test is `Snapshot.QueryRadius` on the entities' positions. The airlock's position is over the middle of the chamber's floor, at the height of a standing person's center.
- A weightless person thrusts and drifts. (User) Step 3 made a character weightless below `FrameTree.ZeroGravityThreshold`. Step 3b widened that: a character is weightless whenever physics does not stand it on walkable ground. Physics keeps a weightless character's momentum, and gravity and forces change it. A walk or a jump does not move it. After each step in which it did not stand, the character loses the part of its velocity, relative to each static or kinematic surface that it hit, that goes into that surface. On a face it uses the face's own normal, because Jolt's contact normal leaned 6 degrees on the hatch's sill and turned a press onto the sill into a drift upward. At an edge it uses Jolt's contact normal. Against a dynamic body it keeps its momentum.
- `PersonControl` reads whether physics stood the person in its last step, as `self.Body.Standing`. Step 3 read the threshold at the person's position instead, and step 3b changed that. While the person does not stand, W, A, S and D fire thrusters of 105 N, 1.5 m/s² on the person, along the view, and Space and C along the person's own up. With no key held, the person drifts on. The report comes a tick late, so the thrusters start and stop a tick late. The walk and the jump do not wait for it: on every tick the person asks physics for the walk of the keys held now, and for a jump on a press of Space, and physics applies them only in the steps in which it stands the person. So a person that holds W through a jump walks on in the first step in which it stands again, and a press of Space in the two ticks before a landing still jumps.
- "Outside" is the ship's frame, inside its 100 m bubble. Past the bubble, the person passes into `earth`. Step 3 lost the person's control there, and step 3b keeps it. The throw step crosses frames.
- The script turns the person toward the airlock and walks it into the chamber. It presses E, and when the hatch is open, it walks the person out. The person drifts away from the hull at 3.125 m/s: its walk of 3 m/s, and five ticks of thrust before W goes up. Then it turns round, and four seconds of thrust bring it back through the hatch, where the cabin's gravity takes it onto the chamber floor. A second E cycles the airlock back, and the person walks into the cabin. The test checks each of these moves, and four captures: the shut hatch seen through the inner door, the shut and the open hatch, and the hull from outside.

Step 3b, control past the ship's bubble (ticket `ter-l6oo`), keeps the person's control when it drifts out of the ship's bubble. The script `scripts/spacewalk.json` and its test `SpacewalkSliceTests` drive it. The user chose each point marked (User).

- About 102.9 m from the ship's center, a hand-off moves the person into the frame `earth`, and the person comes back into the ship's frame 100.9 m from the ship's center. In `earth`, Earth's gravity is 8.7 m/s² at the ship's height, far above `FrameTree.ZeroGravityThreshold`. Under the rule of step 3, physics treated the person there as in the air under gravity and turned its up away from Earth, and `PersonControl` walked instead of thrusting. The person moved correctly, but it had no control.
- A character is weightless whenever physics does not stand it on walkable ground, in any frame and any gravity. (User) That is the physical definition: a body in free fall feels no weight. In `earth` the person falls freely with the ship, so it is weightless there, as in the ship's frame. The other designs were a rule for a free-fall path in a frame's field, a person that stays in the ship's frame, and a larger bubble.
- Physics reports for each character whether it stood in each step. Components read the report one tick later as `self.Body.Standing`, and a dump writes it as `standing` for each character.
- The thrusters work in the air under gravity too. So a jump in the cabin is a short use of the thrusters: in the air, W, A, S and D thrust along the view, and Space held after a jump thrusts the person higher.
- Up turns against the gravity only while the character touches anything under gravity, so a character that lands on its head still turns upright. A character in free fall keeps its last up. A hand-off keeps it too, turned into the axes of the new frame, and only a character that stood takes its up from the gravity of its new frame. So the person does not turn when it leaves the ship's frame or comes back. While the person is in `earth`, the ship turns under it, once per orbit. The person comes back tilted by that turn, about 0.01 rad, until the cabin's gravity stands it on the chamber floor.
- A thrust in `earth` turns a little sideways. Jolt keeps velocities as floats, and at the 7.7 km/s of the orbit a float has steps of 0.5 mm/s. While the person thrusts, each tick rounds the person's velocity and the ship's differently. A thrust of 24 ticks turned 0.003 m/s sideways. A thrust of 5 s turned 0.04 m/s sideways, and the person came back 1.7 m to the side of the hatch's center, past its edge. So the script thrusts only briefly in `earth`.
- The script goes out through the airlock as `scripts/airlock.json` does. Then the person thrusts away from the ship along the ship's X axis, at the height of the hatch's sill and 10 m aft. That axis is the normal of the orbit, where the motion relative to the ship has no sideways drift. Near the edge of the bubble the person brakes, and it drifts out at 0.3 m/s. In `earth` it turns round and captures the ship's port face from 92.6 m, where the hull is 404 by 54 pixels. Then it thrusts toward the ship for 0.4 s, which turns its drift to 0.3 m/s toward the ship. Back in the ship's frame, it thrusts to 3.8 m/s and drifts in through the hatch onto the chamber floor, where it stands. The test checks the frames ship, `earth` and ship, the thrust in `earth`, that the free fall does not turn the person, that each hand-off turns the person's up into the axes of its new frame, the return onto the chamber floor, that the run has no errors, and the size of the ship in the far capture. In this scene the ship's +Y points along the radius from Earth's center, so the slice test cannot tell an up that a hand-off keeps from an up that it takes from Earth's gravity. The physics test `CharacterOrbitHandOffTests` covers the up at a hand-off, with a ship whose +Y is not along the radius.

The demo starts as C# code. The [content packages and mods](content-packages.md) work later moves the content of the demo into the first content package.

## Steps

1. Write the script format and the runner test-first, with a world that has no renderer.
2. Add the renderer and headless captures.
3. Build the vertical slice, one scripted test per step of the slice.
4. Add the windowed host with input.
