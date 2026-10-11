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

## State dumps

```json
{
  "name": "after-one-second",
  "tick": 60,
  "time": 1,
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
- It writes the path of each dump to standard output, and each problem to standard error.
- `--headless` is required until the windowed host exists (ticket `ter-mu7k`).
- The exit codes are the codes of the `tertius` command line in the content packages design: 0 when the run finished, 1 for a wrong command line, 2 when a file cannot be read or written, and 3 for a script that cannot run or that names an unknown scene.

## The vertical slice

The demo shows the engine's main idea working end to end:

1. A ship orbits a planet.
2. A person walks inside the ship while it orbits.
3. The person goes through an airlock to the outside.
4. The person throws an object from one frame into another.

The demo starts as C# code. The [content packages and mods](content-packages.md) work later moves the content of the demo into the first content package.

## Steps

1. Write the script format and the runner test-first, with a world that has no renderer.
2. Add the renderer and headless captures.
3. Build the vertical slice, one scripted test per step of the slice.
4. Add the windowed host with input.
