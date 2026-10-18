# Tertius

Tertius is a game engine in C# for space-scale scenes and dense immersive-sim interactions at once. It runs on Jolt Physics in double precision and draws with a deferred Vulkan renderer. It is an engine, not a game. It is unfinished, and it changes fast.

The two tables below say what is different about its physics and its entity-component system. They also say which other engines do something similar.

## Physics

Other engines do some of the things that Tertius does. I found no engine that does all of them.

| Feature | In Tertius | Who else |
|---|---|---|
| 1. Large worlds | Each object keeps its position relative to a frame near it. When a group of slow objects is 8,389 km or more from its frame, the engine automatically makes a new frame at its center. So every object is correct to 2 nanometers, at every place, at the same time. That number is the limit of the number format, not a measured error. Two levels of frames are enough for the whole observable universe. One frame holds the universe. One frame holds each region up to about a million light-years across, such as a galaxy. The engine makes all other frames itself. | **Star Citizen, in part.** Objects in its ships and on its planets keep positions relative to them. I found no source that it makes new frames by itself. If it does not, a loose object far from any ship or planet, such as a drifting crate, uses one center point: the center of its star system. Then that object is correct only as much as in the engines below. Unreal Engine 5, Godot (custom build), Flax and Stormworks use one center point. They are correct to 2 nanometers only within 16,777 km of it. They are correct to 1 mm at the distance of Neptune, and to 8 m at the nearest star. Unity is correct to 1 mm only within 16 km. Kerbal Space Program moves the center point with the player, so only objects near the player are correct. |
| 2. Ships in ships | Each planet, ship, station and room can be a real object with its own frame: an area that moves with the object. A frame is not a separate level. It stays in the larger world, so you can see into it from outside. It follows its object. A frame that the engine makes for a fast ship falls freely, and the ship falls with it. You can walk in a ship that flies inside a larger ship, and a crate can float out of the door into space. | **Star Citizen does a similar thing with ship and planet frames.** I found no source that its frames touch across an edge (item 3), or that it makes frames by itself (item 4). Unity, Godot, Flax and Jolt can run separate physics worlds, but they do not move objects between them, and the worlds do not follow anything. Void Crew lets you walk in a moving ship, but not in ships in ships. |
| 3. Objects across an edge | A crate can be half in a ship and half outside. It can touch objects on both sides at the same time. | **None that I found in a game or engine.** A 2021 research paper (Kaufmann and others) copies objects across the borders of fixed areas in a large world. Its areas do not move. |
| 4. Fast objects | A ship in orbit moves 8 km each second. Most physics engines store speeds in 32-bit numbers, such as PhysX (used by Unity), Jolt (used by Godot) and Box3D. At orbit speed, those numbers cause two problems in any engine. Small pushes have no effect. A person who uses a thruster for one minute can end about 10 m from the correct place. That number is an estimate, for the harder of two directions that the measurements tried. Tertius fixes this. Objects that move fast together, such as ships in a battle or a boarding party, share one moving frame. The engine makes it automatically, at every place at the same time. So their motion relative to each other stays correct. | **Kerbal Space Program, in part.** It fixes this only in the area around the player's ship. Ships farther than about 2.5 km stop using physics and move on fixed paths. Star Citizen probably fixes it inside its ships, because objects there move relative to the ship. I found no other engine that tries. So in other engines, a space battle, a spacewalk or a boarding party at orbit speed gets these errors. |
| 5. Thrust that you feel | When a ship increases speed, the crew feels a push toward the back wall. When the engines stop in orbit, the crew floats. The tests prove this with loose crates, not yet with a person. | **None that I found.** Star Citizen and Space Engineers use areas of fake gravity. Fake gravity is a setting of the ship, not a result of its motion. |
| 6. Orbits and turns | Planets and stations move on calculated orbits and turn at constant speeds. | **Kerbal Space Program, Kitten Space Agency, Orbiter.** In Kerbal Space Program, a ship on a fixed path stops using physics, so nothing on it can move. Orbiter has no ship insides to walk in. In Tertius, a station on a fixed path still has full physics inside. |
| 7. Spin gravity (not complete) | In a turning station, you will stand on the outer wall. A ball that you drop will move in a curve. Today, a turning station carries what rests on it, but it gives no gravity yet. | **Small test projects, and an experimental Unreal tool for hair and cloth only.** Outer Wilds gets a similar result, because its planets turn in one shared world. That world uses Unity, so the limits of item 1 apply. Space Engineers uses fake gravity, so a dropped ball falls straight down. |

The sources are developer posts, patch notes and forums. I did not make sure that all of them are correct.

## Entity-component system

Each entity is a handle into tables of engine data, plus a list of component classes that hold its behavior. Entities affect each other only through messages.

| Feature | In Tertius | Who else |
|---|---|---|
| Behavior in components | Behavior lives in component classes on each entity. Every message to the entity passes through all of them in priority order, and each one can change, stop or add a message. | **Caves of Qud works this way.** Bevy and Unity DOTS put behavior in systems, and a system runs only on the components that its query names. So there, each interaction between two components of one entity needs its own system. In Tertius, components that never knew of each other still interact, so behavior can emerge. |
| Entities change only themselves | A component writes only its own entity. It changes other entities only by sending them messages. | **None that I found enforces this.** Caves of Qud follows it by convention, and Godot allows direct calls on other nodes. Where any object can write any other, game code needs locks or one thread. |
| Everyone reads the last tick | During a tick, every component reads the world as it was at the end of the last tick. It writes only the rows of its own entity. | **None that I found.** In Unity and Godot, an update can see neighbors that already updated in this frame. There, the update order changes the result, so gameplay usually stays on one thread. |
| Deterministic parallel gameplay | Gameplay runs on many threads. One thread and eight threads give the same world, bit for bit. | **None that I found promises this.** Unity DOTS fixes the order of recorded commands only, and Bevy sets no order between parallel systems. There, a bug that depends on thread timing can vanish on the next run. |
| Messages and hot paths that allocate nothing | A message is a small struct with its data inline, sent to an entity, a topic or everyone. After its buffers grow, a tick allocates nothing, and tests count the bytes. | **Friflo ECS also allocates nothing in C#.** Unreal Mass signals go to entities but carry only a name, so game code must find and pass the data itself. A C# engine whose game code allocates in every frame pauses for the garbage collector. |
| Handles that catch stale references | Every call that takes an entity checks that it still lives, in every build. | **None that I found checks every call.** Bevy and EnTT also number reused slots. But Bevy warns that a reused id can alias, and EnTT leaves most calls on a stale id unspecified. There, a call can act on a new entity in the old slot, far from the bug. |
| Frames are entities | A frame is an ordinary entity, so a ship's frame gets messages and components, and it dies with the ship. Every read of a position names its frame. | **None that I found.** Bevy's big_space makes its grids entities, but with no velocities and no physics for each grid. Unity DOTS and Unreal read a position only relative to the parent or the world, so game code converts it to other frames itself. |
| Blueprints | A blueprint lists components with their field values, and it can inherit from a parent. | **Caves of Qud does more.** Its blueprints also inherit, and it compiles the C# source of mods at run time. Tertius adds only factories that it compiles ahead, so a spawn uses no reflection. |

The sources are engine manuals, developer interviews and wikis. I did not make sure that all of them are correct.

To make sure that these claims are real, read [GUIDE.md](GUIDE.md). It names the code and the tests behind each one.

## Build and run

Every command runs in the Nix dev shell from [flake.nix](flake.nix), in the repository root. You need Nix with flakes turned on. The first run needs network access, because the dev shell builds the double-precision Jolt library. The renderer and the Rendering and Host tests need a Vulkan device. On macOS, the dev shell supplies MoltenVK.

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

[tools/README.md](tools/README.md) gives the commands for coverage, mutation testing and JetBrains Rider.

## Repository layout

- [src](src) holds the modules. Tertius.Core holds the math, handles, messages and the frame tree, and it depends on nothing. Tertius.Simulation, Tertius.Physics and Tertius.Rendering each depend only on Core. Tertius.Host holds the window, the threads and the script runner, and Tertius.Demo holds the demo scenes.
- [tests](tests) holds one xUnit project per module.
- [ARCHITECTURE.md](ARCHITECTURE.md) is the binding design, and [docs/plans](docs/plans) holds the design docs.
- [scripts](scripts) holds the JSON scripts for the demo scenes.
- [tools](tools) holds the helpers for coverage and mutation testing, and [native](native) builds the Jolt library.

## License

Tertius is under the 0BSD license. See [LICENSE](LICENSE) for the full text.
