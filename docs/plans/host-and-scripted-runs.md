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
- F1 switches the view to a debug fly camera and back. (User) WASD moves it, Q and E move it down and up, Shift moves it ten times faster, and the mouse turns it. It starts at the world's camera and moves in the axes of that camera's frame, relative to that camera. When the camera entity moves into another frame, as through an airlock, the fly camera starts again at the world's camera. The render thread sets it in the render snapshot, so it changes nothing in the world.
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

Step 1, the ship's orbit (ticket `ter-fe04`), is the Demo's scene `orbit` and the script `scripts/orbit.json`. The user chose each point marked (User).

- The planet has Earth's radius and gravity, and the ship orbits 400 km up, once in 92.4 minutes. (User) A full orbit runs headless in about 30 s. `--speed 60` plays it in the window in about 1.5 minutes.
- The ship faces the planet: its nose points along its orbit and its belly toward the planet, through the Kepler attitude `FacesParent`. (User)
- The scene has two views. (User) `chase` rides 70 m behind the ship and 12 m above it. `far` stands three Earth radii out and shows the whole planet, where the ship is too small to see. In free play, V switches between them. A script switches them with camera steps.
- The script captures both views and dumps the state at the start and after each quarter of the orbit.

The demo starts as C# code. The [content packages and mods](content-packages.md) work later moves the content of the demo into the first content package.

## Steps

1. Write the script format and the runner test-first, with a world that has no renderer.
2. Add the renderer and headless captures.
3. Build the vertical slice, one scripted test per step of the slice.
4. Add the windowed host with input.
