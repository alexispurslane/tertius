# Blue Danube demo

## Goal

A long demo video of the engine, recorded from one unbroken scripted run with the real renderer. The camera follows one character capsule from a launch terminal on Earth's surface to the outer wheel of a spinning station. The centerpiece recreates the docking sequence from "2001: A Space Odyssey". The graphics are gray-box procedural meshes without textures. The demo shows the physics only, so it adds no sky and no anti-aliasing: the engine is unfinished, and viewers know it (user, 2026-10-18). Title cards and music are added after the run.

## The sequence

1. Earth's surface. Buildings form a launch pad and a boarding terminal. The character walks through the terminal and boards a shuttle.
2. The shuttle launches up a ramp, like a rocket sled, and flies to orbit. In orbit the character becomes weightless by itself, because the ship's frame falls freely. The character leaves its seat and grabs a pen.
3. The shuttle burns again and approaches a large spinning station, seen through its windows. The station has a docking slot on its axis.
4. The shuttle spins up to match the station's rotation, enters the slot, and lands on one side of the docking barrel. Weak spin gravity returns as it moves out to the barrel wall.
5. The character leaves the ship and takes a chute down a spoke to the outer wheel, where it feels full gravity.

## Rules

- The engine runs at its fixed 60 ticks per second for the whole run. The project has no time warp. A time-lapse comes from the capture: the script captures every Nth tick, and the video plays those frames at the normal rate.
- Everything outside the engine is demo work: the scene, demo components, mesh generators, scripts, capture, editing, title cards and music. Every engine change goes to the user first (user, 2026-10-18).
- The music is Johann Strauss II, "The Blue Danube" (1866). The composition is in the public domain. The recording must have a free license, for example a US military band recording or a Musopen recording. Each download needs a yes with its file name, source and size.

## What exists, and what the demo adds

From a survey of the code on 2026-10-18.

| Need | State | Demo work |
|---|---|---|
| Scripts of 100,000 ticks and more | Exists (`scripts/orbit.json` runs about 332,700 ticks) | A generator that writes the long script |
| One image per frame | Partial: only a `capture` step writes a PNG, synchronously, about 0.1 to 0.15 s per 720p frame (estimate) | The generator alternates `run` and `capture`. Measure 600 frames first. |
| Video | Missing: no ffmpeg in the dev shell | Add `pkgs.ffmpeg` to the flake. Encode, cut, add title cards and music with ffmpeg. |
| A camera that follows the character | Partial: the camera is an entity, a camera step is a hard cut, `AttachTo` follows a parent | A chase camera component on an entity attached to the character |
| Character walking, floating, grabbing | Exists (`MoveCharacter`, `Jump`, grab within 3 m) | A path-following walker component. A capsule hides that the rigid form never turns. A pen as a small grab item. |
| Seat | Missing | A static box in the hull's interior |
| Ground at the pad | Earth's sphere sags about 480 m between vertices | A large static slab under the pad, as in the surface yard |
| Cylinders, rings, tori | Missing: only box, sphere and plane meshes | Mesh generators and matching compound or convex shapes for the wheel, spokes, barrel and chute |
| Ship thrust and steering | Partial: thrusters exist in the test project only | Autopilot components: the climb to orbit, the rendezvous, the spin match and the docking |
| Spin gravity, Coriolis, a ship in a turning frame, characters in a turning frame | Planned: the spin gravity epic `ter-s9m0` | Waits for the epic |

## Physics notes

- Weak gravity in the barrel does not return by itself while the ship coasts. A ship that matches the spin and moves out from the axis must keep pushing toward the axis to stay on its rotating path. Its contents then feel that push as weight, which grows with the radius: ω² r. The docking autopilot holds the ship on that path.
- A character that slides down a spoke feels the Coriolis force press it against one wall of the chute, about 2 ω v: 12 m/s² at 20 m/s with ω = 0.313 rad/s. The chute's friction needs tuning, and the fall speed comes near the threshold of a numeric frame for a thrusting group, 50 m/s.
- The station's bubble must be at least twice the size of the ship's bubble so that it can hold it. A sphere suits the wheel better than a flat ellipsoid.
- The launch needs a surface frame that does not turn, or the spin gravity ticket for numeric frames inside turning frames (`ter-vja8`).

## Engine questions for the user

These come up during the demo work and need the user's decision:

1. Capture speed: an asynchronous PNG encode for headless runs (ticket `ter-viyi` covers the window only), or raw frames piped to ffmpeg.
2. A script step that captures every N ticks, instead of a generated list of steps.

Two questions belong to the spin gravity epic, and its tickets carry notes about them:

- The proper acceleration of a ship inside a turning parent frame (`ter-1bnq`).
- Whether the choice between the walking and the weightless form of a character counts the centrifugal term near the hub (`ter-1b21`).

## Order of the work

1. The video pipeline: ffmpeg, the script generator, the chase camera, a measured frame time.
2. The surface: the pad, the terminal, the walker, the boarding.
3. The launch and the climb to orbit: the ramp, the shuttle's autopilot, the weightless cabin, the seat and the pen.
4. The station and the rendezvous: the mesh generators, the wheel, the spokes, the barrel with its slot, the station on its orbit, the rendezvous autopilot.
5. The spin match and the docking.
6. The exit and the chute.
7. The edit: the time-lapse cuts, the title cards, the music and the final render.

Steps 1 to 3 need the rails epic (`ter-ozm8`). Steps 4 to 6 need the spin gravity epic (`ter-s9m0`).
