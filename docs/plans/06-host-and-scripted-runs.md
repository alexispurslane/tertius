# 06 Host and scripted runs

Status: planned. The user approved this design.

## Goal

Wire all modules into a running program, and let tests and agents drive it with scripts. `src/Tertius.Host` and `src/Tertius.Demo` are empty projects today.

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

## The vertical slice

The demo shows the engine's main idea working end to end:

1. A ship orbits a planet.
2. A person walks inside the ship while it orbits.
3. The person goes through an airlock to the outside.
4. The person throws an object from one frame into another.

The demo starts as C# code. Plan 08 later moves its content into the first content package.

## Steps

1. Write the script format and the runner test-first, with a world that has no renderer.
2. Add the renderer and headless captures.
3. Build the vertical slice, one scripted test per step of the slice.
4. Add the windowed host with input.
