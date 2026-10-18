# Content packages and mods

Ticket: `ter-ynqi`. It needs the [content formats](content-formats.md) work. The [asset pipeline](asset-pipeline.md) and the [editor](editor.md) build on it.

## Goal

Separate the engine from game content, and load game content and mods through one mechanism. A package is a folder with a rigid layout. It holds blueprint, scene and patch files (content formats), assets (asset pipeline), and optionally one C# project that compiles into one DLL of components. The engine loads the DLLs at runtime and links the component names in the JSON files to the component types once, by reflection.

A mod works blindly: copy its built folder into a mods folder, and it loads. Every id comes from a folder or file name, so a mod needs no registration step and no load order list. A built package is the form of a package that players get: it holds the DLL and the cooked assets, so a player needs no build tools (see "Built packages").

The user asked that blueprints link component names to "the loaded DLL components or built-in components". This plan reads "built-in components" as the components of the package `tertius.base`, which every game depends on. The user confirmed this reading.

Today no package or mod loading exists. ARCHITECTURE.md lists mods as out of scope, and the user brought them into scope. No class in `src` implements `IComponent` today, so all components can live in packages from the start.

## Decisions

1. Game content and mods use exactly the same mechanism. (User)
2. A package is a folder, and the folder name is the package id. The layout is rigid, and every id comes from a path. The manifest holds only what the folder cannot say. (User)
3. Each package with code has one C# project, compiled against the public engine API into one DLL. The engine loads the DLL at runtime and maps component names to types once, by reflection. The `Blueprints` registry then compiles the factories. (User)
4. Built-in components live in the DLL of the package `tertius.base`, not in the engine assemblies. (User) There are three reasons:
   - The base components use only the mod API. So every build proves that the API is enough for real components.
   - The engine assemblies hold no game rules. A test makes sure that no engine type implements `IComponent`.
   - The base components reload in the editor like the code of any other package.

   The cost is that base components cannot use engine internals. If one needs more, the mod API grows on purpose.
5. Each package gets its own collectible `AssemblyLoadContext` (a package context). Engine assemblies always come from the default context, so a mod and the engine share one `IComponent` type (research probe). Separate contexts keep conflicting private libraries apart and name the failing package in errors.
6. One load gives a code set and a content set. A code set is the loaded package code: the package contexts, the component types and the caches over those types. A content set is the data over it: the packages, the frozen blueprint registry and the prepared scenes. A reload of data files makes a new content set over the same code set.
7. A reference without a package prefix means the package of the file that wrote it. The loader never searches other packages for a path. A qualified reference must name the own package or a declared dependency.
8. A short component name resolves in the own package first, then in the direct dependencies. Two matches in the dependencies are an error, never a silent choice.
9. The load order comes from the dependencies. Ties break by the ordinal order of the package ids. The order then does not depend on the file system or the operating system.
10. A package changes the content of another package only through patch files that merge (the merge rule of content formats). There is no whole replacement of a blueprint. Players of Bethesda games and of Caves of Qud dislike whole replacement, because only the last mod wins.
11. All file and folder names in a package are lowercase. A reference that works on macOS then also works on case-sensitive Linux file systems.
12. The loader reports every error with the file, the JSON Pointer and, where it can, the line and column. Errors in packages from content roots stop the load. Errors in mods can skip the mod (see "Errors and policy"). A player's launcher skips a broken mod and every mod that depends on it, then starts and shows the errors. (User)
13. Mods run with full trust. See "Trust".
14. The mod API is an explicit list of types. `[EngineOnly]` marks the public engine types that mods must not use. An analyzer in the mod SDK makes each use a build error, not a warning. A mod that compiles then keeps working within one API version.
15. The editor never replaces code inside a live World. After a DLL changes, it discards its worlds, unloads the package contexts and loads again. Data changes reload without unloading code.
16. There is no runtime compilation of C# source in this plan. The user asked for compiled DLLs, and `dotnet build` is one command for people and agents.
17. The manifest is `manifest.json`, the name that Caves of Qud uses.
18. A package exists in two forms. The source form is in git, with `code/` and `assets/`. The built form has the DLL and the cooked assets, and players get it. A copied source folder cannot load for a player, because the launcher never builds code or imports assets.

## Design

### Repository layout

```
src/                       the engine; no game rules and no components
  Tertius.Core
  Tertius.Simulation
  Tertius.Physics
  Tertius.Rendering
  Tertius.Content          new: content formats (the content formats design), packages and loading (this design)
  Tertius.Assets           new with the asset pipeline: the cooked format and the asset store
  Tertius.Host
  Tertius.Launcher         renamed from Tertius.Demo: runs a game package
content/                   packages that ship in this repository
  Directory.Build.props    imports the root props and the mod SDK props
  Directory.Build.targets  imports the mod SDK targets
  tertius.base/            built-in components and shared content
  tertius.demo/            the vertical slice of host and scripted runs
tools/
  Tertius.Cli/             the `tertius` command: content and package commands here, asset and editor commands from the asset pipeline and the editor
  Tertius.Mod.Sdk/         the MSBuild SDK for package code, with the mod API analyzer
  Tertius.Import/          the asset importer, from the asset pipeline
  Tertius.Editor/          the editor, from the editor design
tests/
  Tertius.Content.Tests/   unit tests, plus fixture packages in Packages/ and files in Corpus/
  Tertius.Packages.Tests/  tests of tertius.base and tertius.demo through scripted runs
mods/                      ignored by git: local mods during development
dist/                      ignored by git: built packages from `tertius package build`
.tertius/                  ignored by git: downloads of the asset pipeline and state of the editor
```

Git ignores `mods/`, `dist/` and `.tertius/`. In every package, it ignores `bin/`, `.schemas/` and `code/obj/`. It also ignores the backup and temporary files of Blender (see "Package layout"). The solution file `Tertius.slnx` includes the code projects of the packages in `content/`. One `dotnet build Tertius.slnx` then builds the engine and the shipped packages. The flake dev shell adds a small `tertius` wrapper script, so an agent types `tertius content validate` instead of a long `dotnet run` line.

### Package layout

```
content/tertius.demo/
  manifest.json            required
  README.md                optional
  LICENSE, LICENSE.md      optional
  CREDITS.md               optional; the asset pipeline generates it from the asset records
  preview.png              optional: the picture for a mod list
  blueprints/              *.blueprint.json, in any folder depth
  scenes/                  *.scene.json; start.scene.json is the start scene of a game
  patches/                 *.patch.json
  assets/                  source assets and their *.import.json files (asset pipeline)
  code/
    tertius.demo.csproj    the one C# project
    Ships/Thruster.cs      C# sources, with any names and folders
  bin/                     build output; ignored by git
    tertius.demo.dll       the package DLL and its PDB
    assets/                cooked assets (asset pipeline)
    previews/              preview images (asset pipeline)
  .schemas/                generated schemas; ignored by git
```

| Rule | Error | Reason |
|---|---|---|
| Only the entries above can be at the top of a package. | CT1003 | A typo such as `blueprint/` fails at once and does not silently load nothing. |
| Names start with a lowercase letter or a digit. They hold only lowercase letters, digits, `-` and `_`. | CT1002 | The same reference works on every file system. |
| Outside `assets/`, a dot can only start a kind suffix or a file extension. | CT1002 | The kind suffix and the extension stay clear. |
| In `assets/`, a name has at most one extension, and then optionally the suffix `.import.json`. `defaults.import.json` is a fixed name. | CT1002 | An import file names its source: `corvette.blend.import.json` belongs to `corvette.blend`. |
| A file in `blueprints/` ends in `.blueprint.json`, in `scenes/` in `.scene.json`, and in `patches/` in `.patch.json`. | CT1003 | The folder and the suffix both give the kind, and they must agree. A file opened alone still shows its kind. |
| The loader ignores every name that starts with a dot. | None | `.DS_Store`, `.gitignore` and `.schemas` cause no errors. |
| The loader ignores the backup files `*.blend1` to `*.blend32` and the save files `*.blend@` of Blender. | None | Blender writes them next to a `.blend` file on every save (see the asset pipeline design). |
| `code/` holds exactly one project, named `<package id>.csproj`. | CT5009 | The assembly name is the package id. |
| The name rules do not apply inside `code/` and `bin/`, or to the fixed names `README.md`, `LICENSE`, `LICENSE.md` and `CREDITS.md`. | None | C# files follow C# naming. |

A game package is an ordinary package that has `scenes/start.scene.json`. The launcher starts that scene unless its `--scene` option names another one. The launcher's `--game` option names the game package. A shipped game's launcher has its game id built in, so a player starts the game with no options.

### Built packages

A source package is a package as git holds it: with `code/` and `assets/`, and with no `bin/`. It loads only where the mod SDK and the asset tools exist, which is during development. A built package is the form that players get. `tertius package build <id> [--out <folder>]` makes it in these steps:

1. Build the code project in Release.
2. Import every asset of the package (see the asset pipeline design).
3. Copy `manifest.json`, the optional files of the root, `blueprints/`, `scenes/` and `patches/` into the output folder.
4. Copy `bin/<id>.dll`, `bin/<id>.pdb` and `bin/assets/` into the output folder.

The default output folder is `dist/<id>/`. A built package leaves out `code/`, `assets/`, `bin/previews/` and `.schemas/`. To install a mod, a player copies its built folder into a mod root. Archives, a workshop and updates are later work.

The loader reads both forms with these rules:

- A package with `code/` or with `bin/<id>.dll` has code. A package with `code/` and no built DLL is error CT5004.
- A package without `assets/` takes its asset ids from the cooked folders in `bin/assets/`. Each cooked folder has the name of its source file, so the ids of both forms are equal.
- `PackageInfo.Form` says which form the loader found.

A source package in a mod root of a player gives CT5004 or AS8001 (see the asset pipeline design). The hint says: "This folder is a source package. Copy its built package from `tertius package build`."

### Ids and references

| Thing | Where it is | Id |
|---|---|---|
| Package | `content/tertius.demo/` | `tertius.demo` |
| Blueprint | `blueprints/ships/corvette.blueprint.json` | `tertius.demo:ships/corvette` |
| Scene | `scenes/start.scene.json` | `tertius.demo:start` |
| Patch | `patches/corvette-shields.patch.json` in `alice.shields` | `alice.shields:corvette-shields`. Only errors and `explain` show it. |
| Asset | `assets/ships/corvette.blend` | `tertius.demo:ships/corvette.blend` |
| Sub-asset | The piece `Hull` in that model | `tertius.demo:ships/corvette.blend#Hull` |
| Component | `class Thruster` in `tertius.demo.dll` | `tertius.demo:Thruster` |
| Topic | A name in any file | `input`. Topics have no package. |

```
package-id   = segment 1*("." segment)                ; at least two segments, at most 64 characters
segment      = lower *(lower / digit / "-")
name         = (lower / digit) *(lower / digit / "-" / "_")
path         = name *("/" name)
asset-path   = path "." extension                      ; extension = 1*(lower / digit)
reference    = [package-id ":"] (path / asset-path) ["#" fragment]
fragment     = 1*(any character except "#")            ; the asset pipeline design defines the fragments of each asset kind
component    = [package-id ":"] upper *(alpha / digit)
```

- The id of a blueprint or scene leaves out the kind folder and the kind suffix. An asset id keeps its extension, because `rock.png` and `rock.glb` can sit in one folder.
- Only source assets have ids. Import files, and the `.bin` and `.mtl` files that a source names, have no id.
- A component name starts with an uppercase letter and a path starts with a lowercase letter or digit. So a key is never ambiguous between the two.
- The first segment `tertius` is reserved for packages in this repository. Mods use their author name, as in `alice.shields`.
- The kind of a reference comes from where it is written: a `parent` key holds a blueprint, and a `MeshId` field holds an asset. So a blueprint and a scene can share a path.

Resolution rules:

1. A reference without a package resolves in the package of the file that wrote it. This holds inside merged values too, because phase 6 qualifies each reference in its own file before any merge (see the content formats design).
2. A qualified reference must name the own package, a hard dependency or an optional dependency (CT4004). The dependency then loads first, and a package cannot reach content that it did not declare.
3. A missing target is error CT4001. The message lists up to three ids with a small edit distance, so an agent can fix a typo from the error alone.
4. An optional dependency that is absent makes its references errors, except inside patches that require it (see "Patches").

### The manifest

| Key | Form | Required | Meaning |
|---|---|---|---|
| `$schema` | A string | No | The schema path |
| `version` | A semantic version | Yes | The version of this package |
| `engine` | A version range | Yes | The engine API versions that the package works with |
| `title` | A string | Yes | The display name |
| `description` | A string | No | A short description |
| `authors` | An array of strings | Yes | The authors |
| `license` | An SPDX expression | Yes | The license of the package. The asset pipeline records the license of each asset. |
| `dependencies` | An object: package id to version range | No | Packages that must load first |
| `optionalDependencies` | An object: package id to version range | No | Packages that load first. They can be absent. |
| `loadAfter` | An array of package ids | No | Packages that load first. They can be absent. This gives order only, with no version and no access to their names. |
| `incompatible` | An object: package id to version range | No | Packages that must not load together with this one |

An example from a mod, `mods/alice.shields/manifest.json`:

```json
{
  "$schema": ".schemas/manifest.schema.json",
  "version": "1.2.0",
  "engine": ">=0.1.0 <0.2.0",
  "title": "Corvette shields",
  "description": "Adds an energy shield to the corvette of the Tertius demo.",
  "authors": ["Alice Example"],
  "license": "MIT",
  "dependencies": {
    "tertius.base": ">=0.1.0 <0.2.0",
    "tertius.demo": ">=0.1.0 <0.2.0"
  },
  "optionalDependencies": {
    "bob.ship-sounds": ">=2.0.0"
  }
}
```

Versions follow Semantic Versioning 2.0.0. A range is `*`, or a list of comparators that all must hold:

```
range        = "*" / comparator *(" " comparator)
comparator   = [">=" / ">" / "<=" / "<" / "="] version
version      = major "." minor "." patch ["-" prerelease]
```

The engine API version is `EngineInfo.ApiVersion` in Core. The assemblies `Tertius.Core` and `Tertius.Simulation` get the assembly version `major.minor.0.0`. Before 1.0.0, every change of the minor version can break mods. From 1.0.0, only a change of the major version can.

### Finding packages

The loader takes two kinds of root folders. A root folder holds packages as subfolders.

| Root | Examples | Which packages load |
|---|---|---|
| Content root | `content/` in the repository, or the content folder of a shipped game | The game package and its dependencies only |
| Mod root | `mods/` in the repository, or the mods folder of the player | Every package, unless the request disables it |

The load set comes from these steps:

1. Index every subfolder of every root as a package. A folder without `manifest.json` is error CT1001. One id in two roots is error CT1004, which names both folders.
2. Start with the game package and every package in the mod roots that the request does not disable.
3. Add the hard dependencies, again and again, until the set does not grow. A missing one is error CT1101.
4. Make sure that every dependency version is in its range (CT1102). Make sure that `ApiVersion` is in the `engine` range of each package (CT1105). Make sure that no two loaded packages are incompatible (CT1104).

Optional dependencies and `loadAfter` never add a package. They only add order.

`ContentLoader.FindPackages` runs only these steps and the load order, and it loads no code. The importer of the asset pipeline and the packages panel of the editor use it. It returns every package that it found, also after an error, so a panel can show a broken package.

### Load order

The loader sorts the load set with Kahn's algorithm. An edge goes from each hard dependency, each present optional dependency and each present `loadAfter` entry to the package that names it. When several packages are ready, the package with the smallest id in ordinal order goes first. A cycle is error CT1103, and the message lists the packages of the cycle.

Factorio breaks ties by the length of the dependency chain first. That rule adds nothing here, so the plan leaves it out.

### The load pipeline

| Phase | Work | Error codes |
|---|---|---|
| 1. Find | Index the roots, apply the layout rules, read the manifests | CT1001 to CT1004 |
| 2. Resolve | Build the load set and the order. Then call `IAssetResolver.BeginLoad` with the load set. | CT1101 to CT1105 |
| 3. Code | Read the DLL references, create the package contexts, find components, union cases and event payload types | CT5xxx |
| 4. Events | Register the event payload types in the fixed order | CT5007, CT5008 |
| 5. Read | Read every blueprint, scene and patch file, in parallel | CT2xxx |
| 6. Qualify | Make component keys, parents, patch targets and every reference qualified, each in the file that wrote it | CT4xxx |
| 7. Patch | Apply the patches | CT6xxx |
| 8. Inherit | Resolve inheritance with the merge rule | CT4006 |
| 9. Bind | Register the topic names of all read files. Declare every blueprint id in ordinal order. Then bind and register the blueprints on one thread, and freeze the registry. | CT3xxx, CT4xxx |
| 10. Scenes | Prepare every scene | CT3xxx, CT4xxx, CT7xxx |

Each phase reports every error that it finds. Phases 1 to 3 end the load after an error, because the later phases need a complete set of packages and types. Phases 5 to 10 skip only the items that depend on a failed item, such as the children of a broken blueprint. So one run reports every content error at once.

Phases 9 and 10 call the asset resolver in the fixed order of the content formats design: package load order, file path, then document order.

### Code: the project, the SDK and the package context

The mod SDK `Tertius.Mod.Sdk` is an MSBuild project SDK. It sets these values for a package project:

- `net11.0`, `LangVersion` `preview`, nullable, and `EnableDynamicLoading`.
- The assembly name is the package id, from the folder name.
- The output goes to `<package>/bin/`, with no framework or runtime subfolder.
- The documentation file is on, with warning CS1591 off. The schemas of the content formats work take their descriptions from it.
- A reference to the engine API with `Private=false`, so the package never ships its own copy of an engine assembly. The API is `Tertius.Core` and `Tertius.Simulation`.
- A small MSBuild task reads `manifest.json` and adds a reference with `Private=false` to the DLL of each dependency. In this repository it adds a project reference instead, so the build order is right.
- The analyzer that reports uses of `[EngineOnly]` types and members as error TRT0001.

In this repository, `content/Directory.Build.props` imports the root `Directory.Build.props` and `tools/Tertius.Mod.Sdk/Sdk/Sdk.props`. A package project is then one line:

```xml
<Project Sdk="Microsoft.NET.Sdk" />
```

A mod outside the repository references the SDK from NuGet. Its project is also one line, and the research probe built and loaded such a mod:

```xml
<Project Sdk="Tertius.Mod.Sdk/0.1.0" />
```

An engine assembly is an assembly whose name starts with `Tertius.` in an ordinal comparison. Package ids are lowercase, so no package DLL matches.

The loader loads a package DLL in these steps:

1. If `code/` exists and `bin/<package id>.dll` does not, that is error CT5004. Before the CLI loads content, it builds the package code, unless `--no-build` is set.
2. Read the assembly references with `System.Reflection.Metadata`. No mod code runs. A reference to `Tertius.Core` or `Tertius.Simulation` must fit the host (CT5001). Before 1.0.0, its major and minor versions equal those of the host. From 1.0.0, its major version is equal, and its minor version is not greater. A reference to any other engine assembly is error CT5005.
3. Create the package context and load the DLL and its PDB from memory streams. A rebuild can then replace the files while the editor runs.
4. Call `GetTypes()` and sort the types by `FullName` in ordinal order. A `ReflectionTypeLoadException` is error CT5002, with the loader exceptions in the message.

The `Load` override of a package context resolves an assembly name in this order:

1. An engine assembly, or any assembly that the default context provides: return null, so that the default context loads it.
2. An assembly of a dependency package: return it from the context of that package. A type in one package can then derive from a type in its dependency (research probe).
3. A private library of the package: resolve it with an `AssemblyDependencyResolver` on the package DLL.
4. Anything else: return null.

### Components and their names

A component type is a public, concrete, non-generic class that implements `IComponent` and has a public parameterless constructor. A class that implements `IComponent` and breaks one of the other rules gets warning CT5006.

- The component name is the value of `[ComponentName("...")]`, or else the simple C# type name. It matches `^[A-Z][A-Za-z0-9]*$`. The attribute keeps a name stable after a C# rename, and it separates two classes with one simple name in different namespaces.
- Two component types with one name in one package are error CT5003. The message names both C# types.
- The qualified name is `<package id>:<component name>`.

A short name in a file of package P resolves like this:

1. If P has a component with that name, it is that component. If a direct dependency has one too, warning CT4005 says that the own component hides it.
2. If P has no such component and exactly one direct dependency of P has it, it is that component.
3. If two or more direct dependencies have it, that is error CT4003, which lists the qualified names.
4. If no package has it, that is error CT4002, with up to three names at a small edit distance. If an undeclared package has the name, the hint says which dependency to add.

Only direct dependencies count, not dependencies of dependencies. A package can then only use names that its own manifest declares.

Phase 4 registers the event payload types of each package DLL. The loader takes every `unmanaged` struct that implements `IEventPayload`, public or not, in `FullName` order. A payload over 48 bytes is error CT5008. More than 512 event types in total is error CT5007. Registration reads only the type and its size, so no mod code runs.

### The mod API

The mod API is the set of types that a mod compiles against and that the engine keeps stable within one API version.

The mod API in `Tertius.Core`:

- Math: `Double3`, `DQuaternion`, `Pose`, `DPose` and `BoundingBox`.
- Handles: `Entity`, `FrameHandle`, `Topic`, `Topics`, `Broadcast` and `Target`.
- Events: `Event`, `IEventPayload`, `EventType<T>`, `EventMask` and `PayloadBytes`.
- Engine payloads: `Tick`, `Spawned`, `Destroy`, `FrameChanged`, `Collision` and `InputEvent`, with its case types `KeyChanged`, `MouseButtonChanged`, `MouseMoved` and `Scrolled`.
- Render values: `MeshId`, `ModelId` (from the asset pipeline), `MaterialId`, `TextureId`, `TextureSlot` (from content formats), `RenderFlags` and `LightKind`.
- Physics values: `BodyDesc`, the shape records, `CompoundChild`, `BodyMotion`, `BodyLayer`, `PhysicsIntent` and `IntentFlags`.
- Frame values: `FrameBounds`, `MotionDriver`, `GravityField`, its case types `UniformGravity` and `PointGravity`, and `KeplerOrbit`.

The mod API in `Tertius.Simulation`:

- The component surface: `IComponent`, `Self`, `BodyHandle`, `Outbox`, `EventList`, `EventsOf<T>` and `RefPredicate<T>`.
- Reads of other entities: `Snapshot` and `FrameView`.
- Spawning: `SpawnDesc`, `FrameSpec`, `RenderData`, `LightData`, `BlueprintId` and `Self.Spawn(BlueprintId, ...)`.
- Attributes: `ComponentNameAttribute`, `TextureSlotAttribute` and `UnionCaseAttribute` (from content formats), and `ColorAttribute` (from the editor).
- Other types: `StaleEntityException`.
- Queries and sensors (from [queries-and-sensors.md](queries-and-sensors.md)): `EntityQuery`, `EventQuery<T>` and their runs, the physics query requests and their answer events, `CollisionEnded`, `Entered`, `Left`, the `Sensor` component and the collision group names.

Engine only, in `Tertius.Core`:

- The physics seam types and the render seam types.
- `FrameTree`, `TripleBuffer<T>`, `PoseTree<TPose>` and `PoseNode`.
- `EngineEvents.Register` and `EventTypeRegistry.Register`.
- The constructors of `MeshId`, `ModelId`, `MaterialId`, `TextureId`, `Entity` and `FrameHandle`, and `Entity.FromRaw`.

Engine only, in `Tertius.Simulation`:

- `World`, `WorldOptions` and `WorldStats`.
- `Blueprints`, `Blueprint`, `ComponentSpec`, `ComponentFactories`, `ComponentRecipe` and `ComponentMembers`.
- `EventBuffer`.
- `Self.Spawn(string, ...)`. A mod spawns only through a `BlueprintId` field, because the loader resolves that field at load time.

Every other engine assembly is engine only as a whole, and the SDK does not reference it.

- `[EngineOnly]` in Core marks each engine-only type or member in Core and Simulation. The analyzer reads the attribute, so the list lives next to the code and cannot drift from it.
- The research found a real hole: `Self.Snapshot.Frames` returns the mutable `FrameTree`. A component can call `SetLocalPose` or `Reparent` during a parallel tick, which breaks thread safety and determinism. This plan adds `FrameView`, a read-only view, and makes `Snapshot.Frames` return it. If the [design review](design-review.md) fixes this first, the step is already done.
- The boundary is about stability, not security. A mod can still reach any engine type through reflection. See "Trust".
- `Self`, `Outbox` and `EventList` keep their internal constructors. Tests of a package load it through `ContentLoader` and drive a `World`, as the scripted runs from [host and scripted runs](host-and-scripted-runs.md) do. Test projects are not mods, so they can reference the whole engine.

### Patches

A patch file changes one blueprint or one scene of another package with the merge rule of content formats.

| Key | Form | Meaning |
|---|---|---|
| `$schema` | A string | The schema path |
| `blueprint` or `scene` | A qualified reference | The target. A patch has exactly one of the two keys. |
| `requires` | An array of package ids | The packages that must be loaded. Without them, the loader skips the patch. |
| `merge` | An object | The merge patch, in the form of a blueprint file or a scene file |

Rules:

- The target package must be a hard dependency or an optional dependency of the patching package (CT4004). If it is an optional dependency that is absent, the loader skips the patch and reports info CT6003.
- A patch that targets its own package is error CT6004. Edit the file instead.
- A missing target in a loaded package is error CT6001.
- The loader applies patches in package load order, and within one package in the ordinal order of the file paths. Then it resolves inheritance. A patch to a parent then reaches every child that does not override the value.
- Names in a patch resolve in the patching package. So `Shield` below comes from the DLL of `alice.shields`.
- If two patches set a leaf value at the same JSON Pointer, the later one wins. Warning CT6002 names both packages and the winner. A patch that removes a component that another patch changed also gets CT6002.
- `tertius content explain <id>` prints the resolved blueprint, with the file and package of every value. For one entity of a scene, it prints the entry merged over its blueprint, with patches.

An error belongs to the package of the file that wrote the failing value. For a missing key, it belongs to the package of the patch whose `null` removed the key (the source map of content formats records removals). So a patch that breaks a blueprint of the game counts against the mod, not against the game.

The research on other games shaped these rules:

| Game | Problem that players report | Rule here |
|---|---|---|
| Bethesda games | Only the last plugin that edits a record wins. | Patches merge field by field. |
| Caves of Qud | Without `Load="Merge"`, a mod replaces the whole object. Deletes are partly broken. | Only merge exists, and `null` removes. |
| RimWorld | XPath patches fail silently and are brittle. | A missing target is an error with a position. Targets are ids, not queries. |
| Factorio | A "final fixes" stage invites races between mods. | One patch phase, in a fixed order, with conflict warnings. |

Two patches from the mod `alice.shields`. The first, `patches/corvette-shields.patch.json`, adds a component from its own DLL and changes a field of a base component. `Shield` is `alice.shields:Shield`, and `Health` is `tertius.base:Health` of a declared dependency.

```json
{
  "$schema": "../.schemas/patch.schema.json",
  "blueprint": "tertius.demo:ships/corvette",
  "merge": {
    "components": {
      "Shield": { "capacity": 400, "rechargeRate": 25 },
      "Health": { "max": 1500 }
    }
  }
}
```

The second, `patches/start-hum.patch.json`, adds an entity to the start scene of the demo and needs an optional dependency. The pose is in m in ship axes, in the engine room.

```json
{
  "$schema": "../.schemas/patch.schema.json",
  "scene": "tertius.demo:start",
  "requires": ["bob.ship-sounds"],
  "merge": {
    "entities": {
      "shield-hum": {
        "blueprint": "bob.ship-sounds:loops/low-hum",
        "frame": "corvette",
        "pose": { "position": [0, 1, 25] }
      }
    }
  }
}
```

### How mods change behavior

A mod never patches the C# code of another package. The engine has no IL patching, such as Harmony, and no runtime compilation. A mod runs with full trust, so it can bundle such a library itself, but the engine neither supports nor tests that.

A mod changes behavior through components, as in Caves of Qud. It has three ways:

1. It adds its own component to a blueprint with a patch. Components run in `Priority` order on one event list, lowest first. So a mod component with a lower `Priority` changes or removes an event before the base component sees it. One with a higher `Priority` sees what the earlier components did.
2. A patch removes a base component with `null` and adds the mod's replacement in the same merge.
3. A mod component derives from a public component class of a dependency and overrides what the base author made `virtual`. Then the mod replaces the base component as in way 2. The package contexts allow this (research probe).

The `Shield` of the patch example above uses way 1. It runs before `Health` and soaks up damage from its charge:

```csharp
// mods/alice.shields/code/Shield.cs
namespace Alice.Shields;

public sealed class Shield : IComponent
{
    /// <summary>The most damage that the shield holds, in hit points.</summary>
    [Range(0, 1e6)]
    public float Capacity { get; init; } = 100;

    /// <summary>The charge that comes back per second, in hit points.</summary>
    [Range(0, 1e6)]
    public float RechargeRate { get; init; } = 10;

    private float charge = -1;

    // Before tertius.base:Health, which runs at 0.
    public int Priority => -20;
    public EventMask Wants => EventMask.Of<Tick>() | EventMask.Of<Damage>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (charge < 0)
            charge = Capacity;
        if (events.Of<Tick>().Any())
            charge = MathF.Min(Capacity, charge + RechargeRate * self.DeltaTime);
        foreach (ref Damage damage in events.Of<Damage>())
        {
            float soaked = MathF.Min(damage.Amount, charge);
            damage.Amount -= soaked;
            charge -= soaked;
        }
        events.Of<Damage>().Where(static (ref Damage damage) => damage.Amount <= 0).Remove();
    }
}
```

In this example:

- `Capacity` and `RechargeRate` are the fields that the patch sets as `capacity` and `rechargeRate`. They are `init` properties, because every spawn of a blueprint shares the bound values (see the content formats design).
- `Damage` is the event payload of `tertius.base`, a declared dependency of the mod.
- `events.Of<Damage>()` gives each `Damage` payload by reference, so the shield lowers its amount in place. `Health` then sees only what the shield did not soak up.
- `Where(...).Remove()` takes out every `Damage` that nothing is left of, so `Health` never runs for it.

Authors of base packages make mods easier with two habits:

- Keep components small and driven by events, so a mod can come before or after one behavior.
- Mark the methods that mods need to change as `virtual`.

### Determinism

| What | Order | Reason |
|---|---|---|
| Packages | Topological, ties by ordinal id | Independent of the file system and the operating system |
| Files in a package | Ordinal order of the relative path | The order of a directory listing differs between operating systems. |
| Types in a DLL | Ordinal order of `FullName` | Metadata order is stable, but nothing promises it. |
| Event payload ids | Engine types, then packages in load order, then `FullName` | `EventType<T>.Id` is the order of first registration. |
| Topic ids | `input`, then names in package load order, file path order and document order | `Topics.Get` gives ids in the order of first use. |
| Patches | Package load order, then file path | The load order already exists and is deterministic. A package that must win names the other package in `loadAfter`. |
| `BlueprintId` values | Ordinal order of the qualified blueprint id. Phase 9 declares every id before it binds. | A field can then name any blueprint, also one that registers later, or its own blueprint. |
| Asset resolver calls | Package load order, file path, document order | The resolver hands out renderer ids in the order of the requests. |
| Parallel file reads | Results stored by file index, not by finish order | Thread timing must not change a result. |
| Diagnostics | The order of `DiagnosticBag.Sorted` (from content formats) | One input gives one report. |

Topic id values do not change the order of delivery, because the router orders deliveries by recipient (ARCHITECTURE.md, Messages). So an editor that keeps old topic names after a reload behaves like a fresh process.

### Errors and policy

The loader collects every diagnostic in a `DiagnosticBag` (from content formats) and never throws for a content error. Errors use the compiler format of the content formats design, and `--json` gives the same data as JSON.

| Policy | Used by | Behavior |
|---|---|---|
| `Strict` | The CLI, the editor, tests, and the launcher during development | Any error fails the load. Nothing is registered, and every package context unloads. |
| `SkipBrokenMods` | The launcher for players | If every error belongs to packages in mod roots, the loader drops those packages and every package that depends on them, and it loads again. An error that belongs to a package from a content root fails the load, as in `Strict`. |

The rule of "Patches" decides the package that an error belongs to. A failed attempt must leave no trace, because the first attempt already registered the event payload types of every package, the broken mods too. So the loader does these steps before the second attempt:

1. Drop the content set of the first attempt.
2. Unload the code set of the first attempt with `CodeSet.Unload`. This truncates the event type registry to the engine types.
3. Start again from phase 1, with new package contexts.

The ids of the second attempt are then equal to those of a clean load without the dropped mods. This holds for event payload ids, topic ids and `BlueprintId` values. If a context of the first attempt does not unload, the launcher reports warning CT5010 and goes on. That context costs only memory: the registry no longer holds its types, and no world runs its code.

The second load attempt keeps the diagnostics of the first one in the result, so the player sees why a mod is missing. Warnings never fail a load. The CLI option `--warnings-as-errors` changes that for CI.

This design owns these codes. The content formats design owns CT2xxx, CT3xxx and CT7xxx, and it also reports CT4001, CT4006, CT4007 and CT4008.

| Code | Meaning |
|---|---|
| CT1001 | A folder in a root has no `manifest.json`. |
| CT1002 | A file or folder name breaks the naming rules. |
| CT1003 | An entry is not allowed at that place, or its suffix does not match its folder. |
| CT1004 | Two roots hold the same package id. |
| CT1101 | A hard dependency is missing. |
| CT1102 | A dependency version is outside its range. |
| CT1103 | The packages form a dependency cycle. |
| CT1104 | Two incompatible packages are in the load set. |
| CT1105 | The engine API version is outside the `engine` range of a package. |
| CT4001 | Unknown blueprint, scene or asset |
| CT4002 | Unknown component name |
| CT4003 | Ambiguous component name |
| CT4004 | The reference names a package that is not a dependency. |
| CT4005 | Warning: an own component hides a component of a dependency. |
| CT4006 | The blueprints form an inheritance cycle. |
| CT4007 | A scene or a `BlueprintId` field names an abstract blueprint. |
| CT4008 | The asset resolver of the asset pipeline reports an error. The message starts with its AS code. |
| CT5001 | The DLL was built against another engine API version. |
| CT5002 | The DLL does not load, or `GetTypes()` fails. |
| CT5003 | Two component types in one package have one name. |
| CT5004 | `code/` exists, but the DLL is not built. |
| CT5005 | The DLL references an engine assembly other than `Tertius.Core` and `Tertius.Simulation`. |
| CT5006 | Warning: a class implements `IComponent` but cannot be a component. |
| CT5007 | More than 512 event types |
| CT5008 | An event payload is larger than 48 bytes. |
| CT5009 | `code/` does not hold exactly one project with the right name. |
| CT5010 | Warning: a package context did not unload. |
| CT6001 | The target of a patch does not exist. |
| CT6002 | Warning: two patches set the same value. |
| CT6003 | Info: a patch was skipped, because an optional package is absent. |
| CT6004 | A patch targets its own package. |

### Unloading and hot reload

The editor reloads content in two ways.

| Change | What reloads | Cost |
|---|---|---|
| Blueprint, scene or patch files | `ContentLoader.ReloadData` runs phases 5 to 10 again and reuses the code set. The editor rebuilds its worlds from the scene document. | Milliseconds for the demo |
| A package DLL or a manifest | The editor disposes every world and content set, unloads the code set and loads everything again. | About a second (an estimate) |

The editor never moves live component state from an old type to a new type. Such a transfer is complex and fragile. The scene document is the source of truth in the editor design, so a rebuilt world loses nothing that the document holds.

A collectible context unloads only after nothing outside it refers to its types, objects or code (Microsoft documentation, and the research probe). This table lists every holder of mod types and how it lets go.

| Holder | What it holds | How it lets go |
|---|---|---|
| `World` | Component instances | `World.Dispose()`, and the caller drops the world |
| `ComponentFactories` | Compiled delegates over mod types | The code set owns it, and every content set over the code set shares it. A static cache kept the context alive in the probe, so the content formats work makes it an instance. |
| `ContentContracts` | The `JsonSerializerOptions` of the code set, with contracts and converters over mod types | The code set owns it and drops it at unload. Content sets borrow it. No mod type reaches `JsonSerializerOptions.Default`. |
| `Blueprints` | `ComponentSpec.Type` | The content set owns it. `ContentSet.Dispose` drops it. |
| `EventTypeRegistry` | The `Type` of each mod payload | `EventTypeRegistry.TruncateTo(engineCount)` at unload. `EngineEvents.Register()` runs before the first load, and the loader records the count before it registers package types. A contract asserts that every type above that count comes from a package context. |
| Editor caches | Inspector data | `InspectorCache` belongs to the code set (see the editor design). |
| `AssetStore` (from the asset pipeline) | Nothing | It holds renderer ids and engine shapes, never a mod type. |
| Mod threads, timers and static event handlers | Anything | Mods must not create them. The unload timeout reports them. |

Generic statics over mod types, such as `EventType<T>.Id`, do not keep a context alive (research probe).

A risk remains: a static cache of System.Text.Json can keep a mod type. A research probe for the earlier design of content formats saw the caches of `JsonSerializer` keep a context alive. The unload tests of step 2 of content formats and of step 11 here bind first, so they find it. If a context stays alive on .NET 11, the editor restarts its process to reload code (see the editor design). A player gets CT5010 at the second attempt of `SkipBrokenMods`, which costs only memory.

Two always-on guards stop an unload while mod code still runs. A failed guard throws `InvalidOperationException` in every build, because a caller of a public API broke a rule:

- `ContentSet.Dispose` throws while a live World uses its registry. `Blueprints` counts the worlds that use it: the World constructor adds one, and `World.Dispose` removes one.
- `CodeSet.Unload` throws while a content set over the code set is not disposed.

`CodeSet.Unload` does these steps:

1. Apply the guard above.
2. Drop the factories, the contracts and the component list. Truncate the event type registry.
3. Call `Unload()` on every package context.
4. Run `GC.Collect()` and `GC.WaitForPendingFinalizers()` in a loop until every context is gone or the timeout ends.
5. For each context that is still alive, report warning CT5010 with the package id. The editor then asks the user to restart it. `dotnet-dump` is not in the pinned nixpkgs, so a dotnet local tool manifest provides it for `gcroot` searches.

The game loads its content once per process and never unloads it, except for the second attempt of `SkipBrokenMods`.

### Trust

Mods run with full trust. A mod is .NET code in the engine process. It can read and write every file that the player can, open network connections and start programs. .NET cannot isolate code inside one process, and Microsoft states that untrusted code cannot be loaded safely into a trusted .NET process. Tertius does not try. Install only mods that you trust. A package without code runs no code of its own, but its data still controls trusted components.

### The CLI

The `tertius` command lives in `tools/Tertius.Cli`. Every command takes `--json` for output that agents read.

| Command | What it does |
|---|---|
| `tertius content validate [--game id] [--no-build]` | Builds package code and imports the assets of the load set (see the asset pipeline design), unless `--no-build` is set. Then it loads the set with `Strict`, prepares every scene and reports every diagnostic. |
| `tertius content schema` | Writes `.schemas/` in each package, and the VS Code entries that map each file kind to its schema |
| `tertius content format [paths] [--dry-run]` | Writes the canonical layout of the content formats design and corrects every `$schema` path |
| `tertius content new <kind> <id>` | Creates a blueprint, scene or patch file at the right path, with the right suffix and `$schema` |
| `tertius content ids [--kind k]` | Lists every id of the content set |
| `tertius content explain <id> [--entity scene-id]` | Prints a resolved blueprint, a scene or one scene entity, with the file and package of every value |
| `tertius content rename <old id> <new id>` | Moves a file and rewrites every reference to it in the loaded packages |
| `tertius package new <id>` | Creates a package folder with a manifest, the folders and, with `--code`, the project |
| `tertius package build <id> [--out folder]` | Writes the built package (see "Built packages") |

The asset pipeline adds the `tertius asset` commands, and the editor work adds the `tertius editor` commands. Every subcommand of `tertius` uses these exit codes:

| Exit code | Meaning | Commands | What an agent does next |
|---|---|---|---|
| 0 | No errors | All | Nothing |
| 1 | The command line is wrong. | All | Correct the command. |
| 2 | A file or the network cannot be read or written. | All | Report the error and stop. |
| 3 | The content or the assets have errors. | All | Read the diagnostics and fix them. |
| 4 | An internal error, which is a bug | All | Report the error and stop. |
| 5 | A required tool is missing, such as Blender outside the assets shell (see the asset pipeline design). | `content validate`, `package build`, `asset` | Follow the runbook of the asset pipeline design: ask the user before the first build of the assets shell. |
| 6 | An `expect` step of an editor script failed (see the editor design). | `editor run` | Read the result of the step. |
| 7 | Another process held the asset lock of a package for longer than the wait (see the asset pipeline design). | `content validate`, `package build`, `asset` | Wait, then run the command again. |

Each code has one meaning, so an agent can act on the code alone. The JSON output is `{"diagnostics": [...], "summary": {...}}`, with the diagnostic keys of the content formats design.

### Code

New types in `Tertius.Content`:

```csharp
namespace Tertius.Content;

// The content formats work adds the type. This plan uses its parser everywhere.
public readonly record struct PackageId
{
    public static bool TryParse(string text, out PackageId id, out string error);
    public override string ToString();
}

public enum ContentKind { Blueprint, Scene, Patch, Asset }

/// <summary>A package and a path in one kind folder. Blueprints and scenes drop the kind suffix. Assets keep their extension.</summary>
public readonly record struct ContentId(PackageId Package, ContentKind Kind, string Path)
{
    public override string ToString() => $"{Package}:{Path}";
}

public readonly record struct SemVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemVersion>
{
    public static bool TryParse(string text, out SemVersion version);
}

public sealed class VersionRange
{
    public static bool TryParse(string text, out VersionRange range, out string error);
    public bool Contains(SemVersion version);
}

public sealed record PackageManifest(
    SemVersion Version,
    VersionRange Engine,
    string Title,
    string? Description,
    IReadOnlyList<string> Authors,
    string License,
    IReadOnlyDictionary<PackageId, VersionRange> Dependencies,
    IReadOnlyDictionary<PackageId, VersionRange> OptionalDependencies,
    IReadOnlyList<PackageId> LoadAfter,
    IReadOnlyDictionary<PackageId, VersionRange> Incompatible);

public enum PackageOrigin { Content, Mod }

/// <summary>Source: the form in git, with code/ and assets/. Built: the form of tertius package build.</summary>
public enum PackageForm { Source, Built }

/// <summary>LoadIndex is -1 for a package that is not in the load set.</summary>
public sealed record PackageInfo(PackageId Id, string Folder, PackageOrigin Origin, PackageForm Form, PackageManifest Manifest, int LoadIndex);

/// <summary>The result of phases 1 and 2. Found holds every package of the roots, also after an error.</summary>
public sealed record PackageIndex(
    IReadOnlyList<PackageInfo> Found,        // ordinal id order
    IReadOnlyList<PackageInfo> LoadSet,      // load order; empty if phase 2 failed
    IReadOnlyList<ContentDiagnostic> Diagnostics);

public enum LoadPolicy { Strict, SkipBrokenMods }

public sealed record ContentRequest
{
    public required PackageId Game { get; init; }
    public IReadOnlyList<string> ContentRoots { get; init; } = [];
    public IReadOnlyList<string> ModRoots { get; init; } = [];
    public IReadOnlyList<PackageId> Disabled { get; init; } = [];
    public LoadPolicy Policy { get; init; } = LoadPolicy.Strict;

    /// <summary>File text in memory, by file path, that phase 5 parses instead of the files on disk. The editor work adds it in step B2.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Overrides { get; init; } = ImmutableDictionary<string, ReadOnlyMemory<byte>>.Empty;
}

public sealed record LoadResult(ContentSet? Set, IReadOnlyList<ContentDiagnostic> Diagnostics, IReadOnlyList<PackageId> SkippedMods)
{
    public bool Succeeded => Set is not null;
}

public static class ContentLoader
{
    /// <summary>Runs phases 1 and 2 only. It loads no code. The importer and the packages panel use it.</summary>
    public static PackageIndex FindPackages(ContentRequest request);

    /// <summary>Loads code and data. A content error never throws. It is in the result.</summary>
    public static LoadResult Load(ContentRequest request, IAssetResolver assets);

    /// <summary>Reads the data files again and reuses the code set of the previous set. The caller then disposes the previous set.</summary>
    public static LoadResult ReloadData(ContentSet previous, IAssetResolver assets);
}

/// <summary>The loaded package code of one load: one collectible context per package, the component types and the caches over them.</summary>
public sealed class CodeSet
{
    public IReadOnlyList<ComponentInfo> Components { get; }   // package load order, then component name
    public ComponentFactories Factories { get; }
    public ContentContracts Contracts { get; }

    /// <summary>Unloads every package context. Throws InvalidOperationException while a content set over this code is not disposed.</summary>
    public UnloadReport Unload(TimeSpan timeout);
}

public sealed record UnloadReport(bool Unloaded, IReadOnlyList<PackageId> StillAlive);

/// <summary>
/// Everything that one game needs from its packages. Worlds built from it share its frozen registry.
/// Dispose drops the data only. It never unloads the code set, which other sets from ReloadData can share.
/// </summary>
public sealed class ContentSet : IDisposable
{
    public IReadOnlyList<PackageInfo> Packages { get; }      // load order
    public CodeSet Code { get; }
    public Blueprints Blueprints { get; }                    // frozen
    public PreparedScene Scene(string qualifiedId);
    public IReadOnlyList<ContentId> Ids(ContentKind kind);   // assets: source assets only

    /// <summary>The resolved blueprint or scene, and the source map of content formats with the file of every value. A new tree per call.</summary>
    public (JsonObject Tree, SourceMap Sources) Explain(ContentId id);

    /// <summary>One entity entry of a scene, merged over its blueprint, with patches. As above.</summary>
    public (JsonObject Tree, SourceMap Sources) Explain(ContentId scene, string sceneId);

    /// <summary>Asserts every invariant below. Tests and the debug switch call it.</summary>
    public void AssertInvariants();

    /// <summary>Throws InvalidOperationException while a live World uses the registry.</summary>
    public void Dispose();
}
```

New engine types:

```csharp
namespace Tertius;

/// <summary>Marks a public type or member that mods must not use. The mod SDK reports a use as error TRT0001.</summary>
[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class EngineOnlyAttribute : Attribute;

public static class EngineInfo
{
    /// <summary>The version of the mod API. Mods name a range of it in their manifest.</summary>
    public const string ApiVersion = "0.1.0";
}

// Additions to EventTypeRegistry, both [EngineOnly]:
//   public static int Register(Type payload);        // the loader registers mod payloads in the fixed order
//   public static void TruncateTo(int count);         // unload: drops the mod payload types

namespace Tertius.Simulation;

/// <summary>Sets the name of a component in content files. Without it, the name is the simple C# type name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>A read-only view of the frame tree for components. Snapshot.Frames returns it.</summary>
public readonly struct FrameView
{
    public FrameHandle Parent(FrameHandle frame);
    public void GetChildren(FrameHandle frame, List<FrameHandle> results);
    public bool IsAlive(FrameHandle frame);
    public DPose LocalPose(FrameHandle frame);
    public DPose Relative(FrameHandle from, FrameHandle to);
    public FrameBounds Bounds(FrameHandle frame);
    public MotionDriver Driver(FrameHandle frame);
    public KeplerOrbit Orbit(FrameHandle frame);
    public GravityField Gravity(FrameHandle frame);
    public Double3 LinearVelocity(FrameHandle frame);
    public Vector3 AngularVelocity(FrameHandle frame);
    public Vector3 ProperAcceleration(FrameHandle frame);
}

// Additions to Blueprints, both [EngineOnly]:
//   public int WorldCount { get; }                    // the World constructor adds one, World.Dispose removes one
```

`ContentSet.AssertInvariants` states the invariants of the orchestration that this plan adds:

- The packages are in a topological order of their dependencies, with the tie-break of this plan.
- Every registered blueprint has exactly one source file, in a package of the set.
- Every component type belongs to exactly one package context of the code set.
- The registry is frozen, and the `BlueprintId` table and the registry agree in count and order.
- Every prepared scene names only blueprints of the set.
- The event type registry holds the engine types, then the payload types of the packages in load order.

### Performance budgets

These are targets, not measurements. The benchmarks of step 15 replace them with numbers.

| Work | Target | Basis |
|---|---|---|
| Find and order 200 packages | Under 50 ms | Manifests are small files. |
| Load and scan one package DLL | About 1 ms, plus 2 ms per 1,000 types | The research probe: a context loads in 0.6 to 1.0 ms, and 4,000 types scan in 7.5 ms. |
| Read the metadata of one DLL | Under 1 ms warm, about 7 ms cold | The research probe |
| Load `tertius.base` and `tertius.demo` in full | Under 500 ms cold | Asset import (from the asset pipeline) and GPU uploads not included |
| `ReloadData` for the demo | Under 100 ms | |

### ARCHITECTURE.md changes

1. Scope: remove "Mods" from the out-of-scope list. Add that mods run with full trust and that no sandbox exists.
2. Projects and dependency rules: add `src/Tertius.Content`, which depends on Core and Simulation. Add `content/`, `tools/` and the rename of `Tertius.Demo` to `Tertius.Launcher`. State that no engine type implements `IComponent`.
3. A new section, "Packages": the layout, the source and built forms, the ids, the load order, the patches, the policies and the mod API table.
4. Messages: event payload ids follow the fixed registration order, and engine and mods share the cap of 512 types.
5. Snapshots: `Snapshot.Frames` returns a `FrameView`.
6. Rules for references: replace the deferred sentence about mod unloading with the table of holders, the guards and the unload steps.
7. Blueprints, and the new section "Content files": the changes that the content formats design lists.
8. A new section, "Tools": the `tertius` command and its exit codes.

## Steps

Each step writes its tests first and sees them fail for the right reason. The engine builds and every test passes after each step.

1. Write `ContentId`, `SemVersion` and `VersionRange`. The content formats work already added `PackageId` and `AssetRef`. Write one test per grammar rule.
2. Write package discovery and the layout rules. Use fixture folders in `tests/Tertius.Content.Tests/Packages/`. Write one test per code from CT1001 to CT1004. Test the import file names and the ignored Blender files.
3. Read manifests with `JsonSerializer` and a `ContentContracts` with no package types (from content formats), and build the load set and the order. First write tests for the closure, the ranges and the engine range. Also test incompatibilities, optional dependencies, `loadAfter`, cycles and the tie-break. Done: `ContentLoader.FindPackages` returns the index, also for a broken root.
4. Add `FrameView`, and make `Snapshot.Frames` return it. If the design review did this, skip the step.
5. Add the engine types. First write these tests:
   - `EventTypeRegistry.Register(typeof(T))` returns `EventType<T>.Id`, in both call orders.
   - `TruncateTo` removes every id above the count, and a new registration then reuses the first free id.
   - The contract fires for a type from the default context above the engine count.
   - The `WorldCount` of `Blueprints` follows the World constructor and `World.Dispose`.

   Then add `EngineOnlyAttribute`, `EngineInfo`, `ComponentNameAttribute`, `Register(Type)`, `TruncateTo` and `WorldCount`. Mark the engine-only API, `Self.Spawn(string, ...)` included.
6. Load code. The test project builds the fixture packages first. Test the shared engine types, the metadata gate and private libraries. Test a base class from a dependency, the type order and each CT5xxx code. Test a built package without `code/`.
7. Register event payload types in the fixed order. Run two child processes and compare their ids.
8. Implement `IContentScope` from packages. Write one test per name rule, and one per hint.
9. Write the patch phase. Test each patch rule, the conflict warnings and the package that an error belongs to. Test `Explain` of a blueprint, a scene and one scene entity.
10. Write `ContentLoader` from end to end. Test both policies and the order of diagnostics. Test the second attempt: its event payload ids, topic ids and `BlueprintId` values equal those of a clean load without the dropped mod. Test that a patch that removes a required key drops its mod, not the game.
11. Write `ReloadData` and `CodeSet.Unload`. First write the tests of both guards. Then write the lock-in test: load a fixture package with its bind and schemas, spawn and tick, dispose, unload, and see that a `WeakReference` to each context dies. If one stays alive on .NET 11, apply the fallback of decision 18 of content formats.
12. Write the mod SDK and its analyzer. First write the analyzer tests: a fixture mod that uses `World` fails with TRT0001, and a mod that uses only the API builds. Then write the analyzer. Build a mod outside the repository from a local feed.
13. Create `tertius.base` and `tertius.demo`. Move the components and the scene of the host and scripted runs work into them. Rename `Tertius.Demo` to `Tertius.Launcher`, with a `--game` option. Done: the scripted runs give the same state dumps and captures as before the move.
14. Write the CLI commands, `package build` included. Test the command classes directly, and test every exit code. Run the process once as a smoke test. Done: a built `tertius.demo` in a fresh mod root loads with no SDK and no asset tools.
15. Write the benchmarks and report the numbers. Move the decisions of content formats and of this design into ARCHITECTURE.md, and close the epic tickets of both designs.

## Testing

- Fixture packages, good and bad, in `tests/Tertius.Content.Tests/Packages/`. Each bad fixture has an `.expected.json` file with its diagnostics.
- Determinism across processes: two child processes load one content set and print the event payload ids, topic ids, `BlueprintId` values and spawned handles. The outputs must be equal.
- The unload lock-in test of step 11.
- An architecture test: no type in the engine assemblies implements `IComponent`.
- The scripted runs from host and scripted runs, unchanged, as the proof that the move into packages kept the behavior.
- Analyzer tests in the style of Roslyn analyzer tests: source in, diagnostics out.
- `ContentSet.AssertInvariants` after every load in the tests.
- Lock-in tests for the decisions:
  - The ordinal tie-break, own names first, the patch order, and only direct dependencies for names.
  - `Strict` failure for errors in content roots, and the package that an error belongs to.
  - Equal ids after a second attempt and after a clean load.
  - Both unload guards throw in a Release build.
  - A built package loads with no `code/` and no `assets/`.

## Decided by the user

The user accepted every recommendation of this section on 2026-10-10.

1. A player who starts the game with a broken mod gets the policy `SkipBrokenMods`. The launcher skips the mod and every mod that depends on it, starts the game and shows the errors. Development, CI and the editor always use `Strict`. The other option was to refuse to start.
2. Built-in components live in the package `tertius.base`, which every game depends on. The base components then prove that the mod API is enough, and they reload in the editor like other code. The other option was an engine assembly.

## Out of scope and later

- Runtime compilation of loose `.cs` files with Roslyn. The probe measured about 0.5 s for a cold compile and 9 MB of extra libraries. It can come later for quick experiments.
- Mod distribution: archives, a workshop, signatures and automatic updates. The built package folder of this plan is the unit that such a system ships.
- A mod manager in the game, and per-mod configuration screens.
- Isolation of mods, which .NET cannot give inside one process. Mods in a separate process need a message protocol for every API.
- Replacement of a live component type inside a running world.
- Load-stage scripts in the style of the Factorio data stages.
- Replacement of an asset of another package by path. A mod patches the blueprint to use its own asset instead.
- Patch operations beyond the merge, such as an insert into an array.
- Mod versions recorded in save games, because saving is not planned yet.
