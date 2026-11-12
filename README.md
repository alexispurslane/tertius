# Tertius

Tertius is a game engine in C# for space-scale scenes and dense immersive-sim interactions at once. It runs on Jolt Physics in double precision and draws with a deferred Vulkan renderer. It is an engine, not a game. It is unfinished, and it changes fast.

<video src="docs/media/blue-danube-demo.mp4" controls width="100%"></video>

The demo video, 6 minutes 50 seconds at 960 × 540: [docs/media/blue-danube-demo.mp4](docs/media/blue-danube-demo.mp4). It is one unbroken run of the engine. A person boards a shuttle at the North Pole of a turning Earth. The shuttle launches up a ramp, docks with a spinning station in orbit, and the person walks out onto the floor of the station's wheel and throws boxes in its spin gravity. The readouts on screen come from the run, and the check panels set them beside the textbook's numbers.

The two tables below say what is different about its physics and its entity-component system. They also say which other engines do something similar.

## Physics

Tertius keeps every position relative to a frame. A frame is an area that moves with an object, such as a planet, a ship or a room. Frames nest. For example, a room is in a ship, the ship orbits in the frame of a planet, and the planet orbits its star.

Other engines do some of the things that Tertius does. I found no engine that does all of them.

| Feature | In Tertius | Who else |
|---|---|---|
| Ships in ships | A planet, ship, station or room can carry its own frame, which moves with it inside the larger world. A frame is not a separate level. A ship can fly inside a larger ship, and a crate can float out of the door into space. | **Star Citizen does something similar.** Unity, Godot and Jolt can run separate physics worlds, but they move nothing between them, and the worlds follow nothing. So a game must move each object between worlds by hand, and keep each world in step with its ship. |
| Large worlds | Each object keeps its position relative to a frame near it, so it is correct to 2 nanometers, everywhere at once. The engine makes a new frame for any slow group 8,389 km or more from its frame. Two levels of frames that a game places cover the observable universe. | **Star Citizen, in part.** Its ships and planets carry frames, but I found no source that it makes frames for loose objects. Unreal Engine 5 and Godot use one center point, so they reach 1 mm at Neptune and 8 m at the nearest star. Unity reaches 1 mm only within 16 km. So far from the center, objects shake and contacts fail, and a game must keep the action near one point. |
| Objects across an edge | A crate that is half in a ship and half outside touches objects on both sides at once. | **None that I found.** A 2021 research paper copies objects across the borders of fixed areas in a large world, but its areas do not move. Without this, an object in a doorway collides with one side only, so the other side passes through it. |
| Fast objects | Objects that move fast together, such as ships in a battle, share a moving frame that the engine makes by itself. So small pushes still count at orbit speed. | **Kerbal Space Program, in part.** It moves its frame with the player's ship only, and ships farther than about 2.5 km stop using physics. Most engines keep speeds in 32-bit numbers. At orbit speed, a person who thrusts for a minute there can end about 10 m off, and small pushes do nothing. So a battle, a spacewalk or a boarding party at orbit speed goes wrong. |
| Thrust that you feel | A ship's acceleration acts on everything inside it. The crew feels thrust as weight. When the engines stop in orbit, the crew floats. The tests show this with loose crates so far. | **None that I found.** Star Citizen and Space Engineers use areas of fake gravity, which is a setting of the ship. So unless a designer scripts it, what the crew feels does not follow the ship's motion. |
| Orbits and turns | Planets and stations move on calculated orbits and spins, with full physics inside them. | **Kerbal Space Program and Kitten Space Agency also move planets on orbits.** In Kerbal Space Program, a ship on a fixed path stops using physics. So nothing can happen on board a ship while it follows its orbit. |
| Spin gravity | In a turning station, you stand on the outer wall, and a dropped ball moves in a curve. The scene `spin-station` shows a person who stands, walks, jumps and throws a crate on the floor of a turning ring. | **Only small test projects, and an Unreal tool for hair and cloth.** Space Engineers fakes spin gravity with a fixed direction, so a dropped ball falls straight down. Outer Wilds gets a similar result, because its planets turn in the simulation, but its Unity world has the limits of one center point. |

The sources are developer posts, patch notes and forums. I did not make sure that all of them are correct.

## Entity-component system

Each entity is a handle into tables of engine data, plus a list of component classes that hold its behavior. Entities affect each other only through messages.

| Feature | In Tertius | Who else |
|---|---|---|
| Behavior in components | Behavior lives in component classes on each entity. Every message to the entity passes through all of them in priority order, and each one can change, stop or add a message. | **Caves of Qud works this way.** Bevy and Unity DOTS put behavior in systems, and a system runs only on the components that its query names. So there, each interaction between two components of one entity needs its own system. In Tertius, components that never knew of each other still interact, so behavior can emerge. |
| Entities change only themselves | A component writes only its own entity. It changes other entities only by sending them messages. | **None that I found enforces this.** Caves of Qud follows it by convention, and Godot allows direct calls on other nodes. Where any object can write any other, game code needs locks or one thread. |
| Everyone reads the last tick | During a tick, every component reads the world as it was at the end of the last tick. It writes only the rows of its own entity. | **None that I found.** In Unity and Godot, an update can see neighbors that already updated in this frame. There, the update order changes the result, so gameplay usually stays on one thread. |
| Deterministic parallel gameplay | Gameplay runs on many threads. One thread and eight threads give the same world, bit for bit. | **None that I found promises this.** Unity DOTS fixes the order of recorded commands only, and Bevy sets no order between parallel systems. There, a bug that depends on thread timing can vanish on the next run. |
| Hot paths that allocate nothing | Tertius is written in C#, which has a garbage collector. After its buffers grow, a tick allocates nothing on the heap, and tests count the bytes. | **Friflo ECS also allocates nothing in C#.** In Unity, every API that returns an array creates a new copy each time game code uses it, for example `Physics.RaycastAll`. If game code calls such an API in every frame, the game pauses when the garbage collector runs. |
| Handles that catch stale references | Every call that takes an entity checks that it still lives, in every build. | **None that I found checks every call.** Bevy and EnTT also number reused slots. But Bevy warns that a reused id can alias, and EnTT leaves most calls on a stale id unspecified. There, a call can act on a new entity in the old slot, far from the bug. |
| Frames are entities | A frame is an ordinary entity, so a ship's frame gets messages and components, and it dies with the ship. Every read of a position names its frame. | **None that I found.** Bevy's big_space makes its grids entities, but with no velocities and no physics for each grid. Unity DOTS and Unreal read a position only relative to the parent or the world, so game code converts it to other frames itself. |
| Blueprints | A blueprint lists components with their field values, and it can inherit from a parent. | **Caves of Qud does more.** Its blueprints also inherit, and it compiles the C# source of mods at run time. Tertius adds only factories that it compiles ahead, so a spawn uses no reflection. |

The sources are engine manuals, developer interviews and wikis. I did not make sure that all of them are correct.

To make sure that these claims are real, read [GUIDE.md](GUIDE.md). It names the code and the tests behind each one.

## Build and run

Every command runs in the Nix dev shell from [flake.nix](flake.nix), in the repository root. You need Nix with flakes turned on. The first run needs network access, because the dev shell builds the double-precision Jolt library. The renderer and the Rendering and Host tests need a Vulkan device. On macOS, the dev shell supplies MoltenVK. It builds MoltenVK with two patches on the first run ([tools/README.md](tools/README.md), "The patched MoltenVK").

```sh
# Build the solution.
nix develop --command bash -c 'dotnet build Tertius.slnx'

# Run the tests of one module: Core, Simulation, Physics, Rendering or Host.
nix develop --command bash -c 'dotnet test --project tests/Tertius.Core.Tests'

# Run a demo script with no window. It writes PNG images and JSON state dumps.
nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -- --script scripts/drop-box.json --headless --out captures/drop-box'

# Run a scene in a window. W, A, S and D move, V changes the view, F1 starts a fly camera, and Esc quits.
nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -- --scene station'
```

[tools/README.md](tools/README.md) gives the commands for coverage, mutation testing, duplicate code, the docs site and JetBrains Rider.

## Repository layout

- [src](src) holds the modules. Tertius.Core holds the math, handles, messages and the frame tree, and it depends on nothing. Tertius.Simulation, Tertius.Physics and Tertius.Rendering each depend only on Core. Tertius.Host holds the window, the threads and the script runner, and Tertius.Demo holds the demo scenes.
- [tests](tests) holds one xUnit project per module.
- [ARCHITECTURE.md](ARCHITECTURE.md) is the binding design, and [docs/plans](docs/plans/README.md) holds the design docs.
- [scripts](scripts) holds the JSON scripts for the demo scenes.
- [tools](tools/README.md) holds the helpers for coverage, mutation testing, duplicate code and the docs site, and [native](native) builds the Jolt library.

## License

Tertius is under the 0BSD license. See [LICENSE](LICENSE) for the full text.
