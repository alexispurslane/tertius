# Asset pipeline

Ticket: `ter-kge6`. It needs [content packages and mods](content-packages.md). It also uses the headless rendering of [host and scripted runs](host-and-scripted-runs.md) and the [renderer snapshot tests](renderer-snapshot-tests.md). The [editor](editor.md) builds on it.

## Goal

Turn source assets into runtime-ready data that blueprints and scenes can name. Source assets are models from Blender and other tools, and texture images. The user will tell Claude agents to "pull in this ship model" or to "grab a CC0 rock texture set". The agents then run the whole pipeline from the command line. So the pipeline must work with no person at the screen:

- A command line tool with JSON output and fixed exit codes.
- Error reports with stable codes and hints.
- Preview images that an agent opens with the Read tool.
- The same output bytes for the same inputs.
- A record of the source, authors and license of every third-party asset.

Today the renderer takes in-memory `MeshData`, `MaterialDesc` and `TextureData` through `IRenderer`, and no importer exists. The [content formats](content-formats.md) design defines asset references in content files and the `IAssetResolver` interface. The content packages and mods design defines the `assets/` folder of a package and the built form of a package. This design owns everything between a source file and a `MeshId`, `ModelId`, `MaterialId`, `TextureId` or `ShapeDesc`.

## Decisions

1. Claude plans the pipeline in detail, and it must be complete enough for agents to use alone. (User)
2. glTF 2.0 is the one scene format inside the importer. Blender exports it well, and its conventions match the engine: meters, radians, +Y up and counterclockwise front faces. (User: "GLTFs or something from Blender")
3. FBX, OBJ and `.blend` files go through headless Blender, which writes glTF. One converter covers three formats, and the glTF exporter of Blender is the reference exporter of the format.
4. Blender lives in a second dev shell, `nix develop .#assets`. Blender adds about 1.7 GiB to the Nix store, and only conversions need it. Every other tool is in the default shell. The cost: every machine that imports a `.blend` source needs the assets shell once, with a download of about 870 MiB (measured on 2026-10-26, see tools/README.md: Blender from cache.nixos.org, the small sources of `blender-mcp`, and the archives of MPFB and the MakeHuman asset pack, 44 MiB and 268 MiB, for decision 27). This includes the machine of the user, a fresh clone and CI, because git ignores the cooked output. Git keeps no downloaded file (decision 26), so the importer converts every third-party source at import, not intake. Every source that Blender converts then needs Blender on every machine: FBX, OBJ, `.blend`, TIFF, WebP and glTF with Draco or meshopt compression. A plain glTF needs no Blender, so the runbook prefers glTF downloads. (Changed on 2026-10-26: the project's own models are scripted models, see decision 22, and third-party sources are fetched, see decision 26.)
5. The importer uses C# libraries where a mature one exists: SharpGLTF, StbImageSharp, TinyEXR.NET and BCnEncoder.Net. It uses native code only where C# has no equal: meshoptimizer, V-HACD and Jolt.
6. Native bindings use `[DllImport]` with blittable arguments, not `[LibraryImport]`. The `LibraryImport` source generator does not load inside Stryker.NET 5.0.0 (project memory), so mutation tests of the importer stay possible.
7. Cooked output is the runtime-ready data that the importer writes. It goes into `bin/assets/` of the package, which git ignores (content packages and mods). A built package, as the content packages and mods design defines it, carries `bin/assets/`, so a player needs no importer.
8. The game never imports. The CLI, the editor and `tertius content validate` import, and the game loads only cooked output. The game then ships no native asset tools.
9. A model file is addressed by node names. A piece is a node that renders as one unit. Every root node is a piece, and an author can mark any other node as a piece. A part is one mesh with one material inside a piece. One entity renders one piece with all its parts through `render.model`.
10. The front of an object in the engine is -Z, like the camera and spot lights. glTF puts the front of an asset at +Z. So the importer turns every model by 180° about +Y.
11. A light's power is in W, as the engine's lights are in SI units ([local-light-shadows.md](local-light-shadows.md), "Units"). The importer turns glTF's candela into watts by the exporter's own factor of 683 lm/W: P = 4π · cd / 683 for a point light, and P = 2π (1 − cos θ_o) · cd / 683, the cone's solid angle times the intensity, for a spot light. So a Blender point light of 100 W imports as 100 W, and a scene from Blender keeps its brightness.
12. Materials import from glTF metallic-roughness as they are. Metallic, roughness and the base color pass through to `MaterialDesc`, and the reflectance is clamp(√(Y(F0d) / 0.16), 0, 1), where F0d is the dielectric reflectance of the glTF material. The default glTF dielectric, with an IOR of 1.5, gives the default reflectance of 0.5 exactly. (Changed on 2026-10-24: the renderer switched from Blinn-Phong to a PBR BRDF, so no conversion to gloss is needed. See [pbr-brdf.md](pbr-brdf.md).)
13. Metals look right. The BRDF takes the base color of a metal as its F0, and metals reflect the renderer's environment map of the scene around the camera ([environment-map.md](environment-map.md)). The specular part of the hemisphere ambient light lights them only in a renderer whose map is off. (Changed on 2026-10-24: the renderer no longer caps F0 at 0.16, so the importer no longer keeps half of the base color of a metal as diffuse color. (User))
14. Textures are block-compressed (BC4, BC5 and BC7), and the importer makes every mip level. All target GPUs support BC: Apple Silicon through MoltenVK (measured on this machine) and desktop GPUs on Linux.
15. LODs belong to a mesh. A LOD is a coarser index list over the same vertices. The renderer picks a LOD by its projected error in pixels.
16. Collision is stored as points and triangles. Jolt builds the shapes at content load, outside a tick. joltc exports no shape serialization (research), and the documentation of Jolt says that its cooked format changes between versions.
17. Skinning and morph targets are built by [skinning-and-morphs.md](skinning-and-morphs.md) (epic `ter-4vzc`): `MeshData.Rigging` carries a rig, a skin and morph targets, and the renderer deforms them by an entity's rig pose. The importer maps glTF skins and targets onto it (see "Meshes"). Animation clips are later work. See "Out of scope and later". (Changed on 2026-10-26.)
18. The cache key is a SHA-256 hash of the source bytes, the effective configuration, the importer version and the tools. The tools are the Nix store path of each native tool and the .NET runtime version. A rebuild with the same version number can change the output. Provenance is not part of the key, because it does not change the output. For a fetched file, the SHA-256 that its fetch record pins stands for its bytes, so the key needs no download (decision 26).
19. Every third-party asset records its source, authors, license and attribution text. The CLI rejects licenses that forbid adaptations or commercial use.
20. Every download needs the approval of the user. `tertius asset fetch` only prints a plan of the files, sizes and license. It downloads only with `--approved`, which an agent passes after the user said yes in chat. The approval is recorded with the SHA-256 of each file, and it covers every later download of exactly those bytes, on any machine (decision 26). A new or changed file needs a new approval.
21. No binary build output goes into git. The knot, the push mirror of the repository (tools/README.md, "The remotes: GitHub and the knot mirror"), has no Git LFS. So the project's own models are scripts, and their `.blend`, `.glb`, baked textures and views are build outputs in `bin/`. (User, 2026-10-26. This replaces the decision of 2026-10-10 that put binary source files into Git LFS. Third-party sources, such as texture images and `.glb` downloads, are fetched from their provenance records, decision 26.)
22. A model of the project has one source: a folder of Blender Python scripts, the scripted model (see "Scripted models"). Nobody edits a `.blend` file by hand. (User, 2026-10-26)
23. The importer builds a scripted model with the build tool `tools/blender/build.sh`. The outputs go into the cooked folder of the asset in `bin/assets/`. The build tool runs plain headless Blender with no MCP server, so an import never needs one. (User, 2026-10-26)
24. Agents model through the official Blender Lab MCP server, `blender-mcp` 1.0.3, which `.mcp.json` registers as the server `blender`. All modeling goes through its tool `execute_blender_code`, and agents look at their work through its render tools. (User, 2026-10-26)
25. The Nostromo tug moves in from an earlier project as the first scripted model, `tertius.demo:ships/nostromo.model`. It is also the worked example of the project skill `procedural-modeling`. (User, 2026-10-26)
26. Third-party sources are fetched from their provenance records. Git keeps the sidecar of each third-party asset, with its page, authors, license and fetch record: the URL, size and SHA-256 of each file. Git does not keep the files. The importer downloads a file on the first import where it is missing from the source cache `bin/sources/` of the package, checks its SHA-256, and keeps it there. The user approves the download once, at the fetch plan, and that approval covers the same pinned bytes on every machine (see "Fetched sources"). The cost: the first import on a new machine needs the network, and a source lasts only as long as its URL serves it. (User, 2026-10-26)
27. A model of a person makes the person with the code of MPFB, the MakeHuman add-on for Blender, inside the build. MPFB's sliders, skins, eyes, eyebrows, eyelashes, teeth, tongue, hair and rig then make a complete human, and a copy of its logic cannot drift from the original. The scripts make every garment and every piece of equipment. No clothes asset is used. (User, 2026-10-26: "I want the procedural model generation to actually be able to do a complete human.") See "Humans from MPFB".

## Design

### Data flow

```
assets/<path>               source file, own work             ┐
bin/sources/<path>          source file, fetched              │
assets/<path>.import.json   sidecar (optional)                ├─> importer (tools/Tertius.Import)
assets/**/defaults.import.json  folder defaults (optional)    ┘         │
                                                                        ├─> bin/sources/<path>    a missing fetched file, downloaded
                                                                        │                         from the fetch record of its sidecar
                                                                        ├─> bin/assets/<path>/    cooked folder
                                                                        └─> bin/previews/<path>/  preview images

assets/<path>.model/        scripted model ─> build tool (tools/blender/build.sh, headless Blender)
                                                 └─> bin/assets/<path>.model/build/model.glb ─> importer, as a glTF source

*.blueprint.json, *.scene.json ─> ContentLoader (content packages and mods) ─> AssetStore (src/Tertius.Assets)
                                                                                 ├─> IRenderer: meshes, textures, materials, models
                                                                                 └─> ShapeDesc objects ─> IPhysicsBackend.Prepare
```

### Projects

| Project | Kind | References | Contents |
|---|---|---|---|
| `src/Tertius.Assets` | Library | Core, Simulation, Content | The cooked format, `AssetStore` (the runtime resolver) |
| `tools/Tertius.Import` | Library | Core, Content, Assets, Physics, Rendering | The importer, intake (`tertius asset add`), fetch, previews and the file watcher |
| `tools/Tertius.Cli` | Executable (content packages and mods) | Adds Import | The `tertius asset` commands |
| `tests/Tertius.Assets.Tests` | Tests | Assets | Format round trips and the store |
| `tests/Tertius.Import.Tests` | Tests | Import | Importer tests, `Fixtures/` and `Golden/` |
| `native/vhacd.nix` | Nix derivation | | V-HACD 4.1.0 behind a small C API |

`Tertius.Assets` is engine only, like `Tertius.Content`, so mods never reference it. The game references `Tertius.Assets` but not `Tertius.Import`.

### Source formats

| Extension | Kind | Reader | Notes |
|---|---|---|---|
| `.glb`, `.gltf` | Model | SharpGLTF.Core 1.0.7 | The main format. A `.gltf` can also hold only materials (see "Texture sets"). A glTF with Draco or meshopt compression goes through a Blender round trip first, which needs the `assets` shell. SharpGLTF supports neither extension (research). |
| `.blend`, `.fbx`, `.obj` | Model | Blender 5.2.2, then SharpGLTF | Needs the `assets` shell. The cooked folder keeps the converted `.glb`. |
| `.model` (a folder) | Model | The build tool `tools/blender/build.sh` in Blender 5.2.2, then SharpGLTF | A scripted model: a folder of Blender Python scripts (see "Scripted models"). Needs the `assets` shell. The cooked folder keeps the build outputs in `build/`. |
| `.png`, `.jpg`, `.jpeg`, `.tga` | Texture | StbImageSharp 2.30.16 | 8 bits per channel. A 16-bit PNG becomes 8 bits, which is the precision of the BC formats. |
| `.tif`, `.tiff`, `.webp` | Texture | Blender 5.2.2, then StbImageSharp | StbImageSharp reads neither. Blender writes a PNG file. Needs the `assets` shell. The cooked folder keeps the converted `.png`. |
| `.hdr` | Texture | StbImageSharp `ImageResultFloat` | Float pixels |
| `.exr` | Texture | TinyEXR.NET 1.2.0 | DWAA and DWAB compression are not supported (research). |
| `.bin` | Buffer of a `.gltf` | | Allowed only next to the `.gltf` that names it. It has no id. |
| `.mtl` | Materials of an `.obj` | | Allowed only next to its `.obj`. It has no id. |

Intake is the work of `tertius asset add`: it records a download in a package, or it copies an own file into it (see "Fetching and intake"). Intake changes no bytes. The importer makes every conversion at import, and the cooked folder keeps its result:

- TIFF and WebP images become PNG files through Blender.
- A glTF with Draco or meshopt compression becomes a plain `.glb` through a Blender round trip.
- FBX, OBJ and `.blend` files become `.glb` files through Blender.
- A `.gltf` with separate files stays as it is. The importer reads its `.bin` and images by their paths.

The reason: git keeps no downloaded file (decision 26) and no binary build output (decision 21). A converted file is binary build output, and for a download it is a changed download too, so its only place is the cooked folder. Before decision 26, intake converted these inputs once and wrote the result into `assets/`.

The project's own models are scripted models, not `.blend` files (decision 22). A `.blend` source is a third-party source, so it is a fetched file (see "Fetched sources"). The importer converts it on every change.

### Assets in a package

A package in the layout of content packages and mods, with assets:

```
content/tertius.demo/
  assets/
    defaults.import.json            optional: configuration for this folder and every folder below it
    ships/
      nostromo.model/               a scripted model: the project's own model (see "Scripted models")
        model.json                  the scripts in order, the seed, the parameters, the bake and the views
        s00_scene.py                the model scripts
        s10_hull.py
        ...
      nostromo.model.import.json    optional sidecar: the configuration of this one asset
      corvette.blend.import.json    a third-party .blend model: provenance and the fetch record of corvette.blend
    props/
      rock-moss-set-01.gltf.import.json  a glTF of Poly Haven: provenance, the fetch record of its 5 files, and configuration
    materials/
      painted-metal.gltf            a material-only glTF that intake writes for a texture set; it names the fetched images
      painted-metal.gltf.import.json     provenance and the fetch record of the images
    planets/
      earth_albedo.jpg.import.json  provenance and fetch record: Solar System Scope, CC-BY-4.0
    ui/
      crosshair.png                 own work of the package
  bin/                              ignored by git (content packages and mods)
    sources/                        the source cache: each fetched file at its path in the source tree (see "Fetched sources")
      ships/corvette.blend
      props/rock-moss-set-01.gltf   the bytes of rock_moss_set_01_2k.gltf, renamed by intake
      props/rock_moss_set_01.bin    the dependencies keep the paths that the .gltf names
      props/textures/rock_moss_set_01_diff_2k.jpg
      ...
      materials/painted-metal/painted-metal_albedo.jpg
      materials/painted-metal/painted-metal_normal.jpg
      materials/painted-metal/painted-metal_roughness.jpg
      materials/painted-metal/painted-metal_metal.jpg
      planets/earth_albedo.jpg
      .archives/                    downloaded archives, named by their SHA-256
    assets/
      ships/corvette.blend/         one cooked folder per source asset
      ships/nostromo.model/
        build/                      the outputs of the build tool: model.glb, model.blend, textures/, build.json
    previews/
      ships/corvette.blend/views.png
```

A sidecar is the import file next to one source asset. Its name is `<source file name>.import.json`. For a scripted model, the source name is the folder name, so the sidecar of `nostromo.model/` is `nostromo.model.import.json` next to the folder. A folder default file is `defaults.import.json`, and it applies to every asset in its folder and in the folders below.

The source tree of a package is the set of its source files, with paths relative to `assets/`. An own file lies in `assets/`. A fetched file lies in the source cache `bin/sources/`, at the same path (see "Fetched sources"). The rules below that name the source tree hold for both places.

The name rules of content packages and mods apply to every file and folder in `assets/`. These rules apply too:

| Rule | Error | Reason |
|---|---|---|
| Every file in `assets/` is one of these: a source asset, a sidecar, a folder default file, a dependency that a source names, or a file of a scripted model folder. | AS1001 | An unknown file is a mistake. It must not load as nothing. |
| A scripted model folder holds `model.json`, the scripts that `model.json` names, and optionally `README.md`. It holds no other file and no folder. | AS1006 | A script that no list names never runs. It is a leftover or a mistake. |
| `model.json` binds to its schema, and every script that it names exists. | AS1007 | A build with a missing script fails late, in Blender. |
| A sidecar has its source file in the same folder: in `assets/`, or in the fetch record of the sidecar. | AS1002 | A sidecar with no source is a leftover from a rename. |
| A `.bin` or `.mtl` file has a `.gltf` or `.obj` in the same folder of the source tree that names it. | AS1003 | The same reason |
| A `.gltf` names only files of the source tree of its package. | AS1004 | A package must be complete without other folders. Its fetch records say where the fetched files come from. |
| A fetched file is not in `assets/`. | AS1008 | Git keeps only its fetch record (decision 26). A second copy in `assets/` would go into git, and it could differ from the pinned bytes. |
| A folder default file holds no `source` block. | AS1009 | A fetch record names the files of one asset, so provenance lives in the sidecar of that asset. |
| The scanner and the watcher ignore `*.blend1` to `*.blend32` and `*.blend@`. Git ignores them too. | None | Blender writes these backup and save files next to a `.blend` file (see below). |

By default, Blender keeps one backup, `corvette.blend1`, on each save, and it can keep up to 32. It also saves through a temporary file, `corvette.blend@`, and a rename. These are beliefs from the defaults of Blender, and step 15 tests them. Without the rule above, every save of the user makes `tertius content validate` fail with AS1001.

Names inside a glTF, such as node, material and image names, are free text. They become fragments, and the grammar in the content packages and mods design allows any character except `#` in a fragment.

### Ids and fragments

An asset id keeps its file extension (content packages and mods): `tertius.demo:ships/corvette.blend`. A scripted model keeps the extension of its folder: `tertius.demo:ships/nostromo.model`, and `tertius.demo:ships/nostromo.model#Nostromo` names its piece `Nostromo`. The fragment after `#` names a sub-asset: a piece, a material or a node inside the file. The kind of the field that holds the reference decides where the importer looks up the name. The content packages and mods design states the same rule for whole references.

| Field or key | Reference form | Looks up | Result |
|---|---|---|---|
| `render.model`, `ModelId` fields | `ships/corvette.blend#Hull` | Pieces | The piece with all its parts and lights. A part is one mesh with one material. |
| `render.model` | `ships/corvette.blend` | | Every root piece of the file |
| `render.mesh`, `MeshId` fields | `ships/corvette.blend#Hull` | Pieces | The mesh of a piece that has exactly one material (AS2011 otherwise) |
| `render.material`, `MaterialId` fields | `ships/corvette.blend#HullPaint` | Materials | The converted material |
| `{"kind": "asset"}` shapes | `ships/corvette.blend#Hull` | Pieces | The collision of the piece for the use that content formats defines: moving for a body that moves, static for an interior or a static body |
| `{"node": ...}` poses | `ships/corvette.blend#AirlockInner` | Nodes | The pose of the node in its piece (see "Node poses") |
| Texture keys of inline materials, `TextureId` fields | `planets/earth_albedo.jpg` | | A standalone image that fits the texture slot. Images inside a model have no texture id. Name the material instead. |

A reference without a fragment names the only piece or the only material of the file. If the file has several, the reference is error AS2010, and the message lists the names. A missing asset file is error CT4001 of content packages and mods. A missing sub-asset is an AS error inside CT4008.

LODs have no reference. They belong to their mesh, and the renderer picks them.

### Models: nodes and pieces

The importer reads the default scene of the glTF and applies these rules:

1. Every root node is a piece.
2. A node marked as a piece is a piece. Mark it in one of three ways: the name prefix `PIECE_` in Blender, the custom property `tertius_piece` with the value 1, or the sidecar key `"pieces": {"AirlockDoor": {}}`. The custom property survives a rename in Blender.
3. A piece holds the geometry and the lights of its node and of every descendant. It leaves out descendants that are pieces themselves, collision markers and LOD nodes.
4. The origin of a piece is the origin of its node, which is the object origin in Blender. Put the origin of a door on its hinge, and the door piece turns about the hinge.
5. Fragments leave out the prefixes of the name rules. The node `PIECE_AirlockDoor` is the piece `AirlockDoor`.
6. Two nodes with the same name keep the name in glTF index order, and the later ones get `~2`, `~3` and so on (AS2008). Object names are unique in one Blender file, so this is rare in exports from Blender.
7. A camera is ignored (AS2016).

An example in Blender, and the pieces that it gives:

```
Object tree in corvette.blend            Piece          What the piece holds
Hull                                     Hull           meshes of Hull and Interior, and the light Lamp
├── Interior
├── PIECE_AirlockDoor                    AirlockDoor    its own meshes; its origin is the hinge of the outer door
│   └── UCX_AirlockDoor                  (collision)    the moving collision of AirlockDoor
├── UCX_Hull_01 … UCX_Hull_12            (collision)    the moving collision of Hull: 12 convex hulls
├── UTM_Interior                         (collision)    the static collision of Hull: a triangle mesh
├── AirlockInner        (empty)          (node pose)    the place of the inner door
├── NozzleL, NozzleR    (empties)        (node poses)
└── Lamp                (point light)
```

The content formats design defines the key `render.model`, and a render section with `model` has no `mesh` and no `material` (CT3011 of content formats). A renderer model, named by a `ModelId`, holds the parts and lights of one piece. The entity draws every part of the piece with its own transform. A reference to the whole file draws every root piece at the pose of its root node.

#### Node poses

The content formats design defines the node pose `{"node": <asset reference>}` for `Pose` and `DPose` members. Its value is the pose of that node relative to the origin of a piece:

- For a piece node: the piece that contains its parent node.
- For any other node: the piece that contains the node.

So a door that is a piece under the hull gets its pose in hull axes. An entity in the frame of the ship needs exactly this pose. The origin of the frame is the origin of the ship entity. A node pose is rigid. The importer bakes every scale into the geometry (see "Axes, units and scale").

The corvette blueprint and the start scene of the content formats design use this model:

- The corvette draws `ships/corvette.blend#Hull` through `render.model`. Its body shape and its interior shape both name that piece. The body is a moving use and gets the 12 `UCX_` hulls. The interior is a static use and gets `UTM_Interior` (see "Collision").
- The blueprint `ship-parts/airlock-door` draws the piece `AirlockDoor`.
- The scene entity `airlock-outer` takes the pose `{"node": "ships/corvette.blend#AirlockDoor"}`, which is the pose of the piece node in hull axes. The entity `airlock-inner` takes the pose of the empty `AirlockInner`. One door piece then serves both doors.

### Scripted models

A scripted model is a model whose only source is a folder of Blender Python scripts. The user decided on 2026-10-26:

1. A model's only source is a folder of Blender Python scripts. Nobody edits a `.blend` file by hand. The `.blend` file, the `.glb` file, the baked textures and the views are build outputs.
2. The importer builds a scripted model, and the outputs go into the cooked folder of the asset in `bin/assets/`, which git ignores (decision 7). No build output goes into git, because the knot push mirror has no Git LFS. Third-party sources are fetched from their provenance records into `bin/sources/` (decision 26, see "Fetched sources").
3. Agents model through the official Blender Lab MCP server, `blender-mcp` 1.0.3. Blender Lab publishes its [source](https://projects.blender.org/lab/blender_mcp) and its [documentation](https://www.blender.org/lab/mcp-server/). The project's `.mcp.json` registers it, so its tools are native tools of an agent. All modeling goes through its tool `execute_blender_code`, and agents look at their work through its render tools.
4. An earlier agent built the Nostromo tug in its own project. The Nostromo moves in as the first scripted model. It is also the worked example of the project skill `procedural-modeling`.

Terms:

- The model folder is the folder of one scripted model. Its name ends in `.model`.
- A model script is one Python file in the model folder that `model.json` names.
- The model library is the Python package `tools/blender/tertius_blender/`. Model scripts import their helpers from it.
- The build tool is `tools/blender/build.sh`. It runs `tools/blender/build.py` inside headless Blender.
- The bridge is a Blender process that runs the MCP add-on and listens on TCP port 9876 of `localhost`.
- The MCP server is the `blender-mcp` program. Claude Code starts it over stdio, and it sends each tool call to the bridge.

#### The model folder

```
content/tertius.demo/assets/ships/nostromo.model/
  model.json          the scripts in order, the seed, the parameters, the bake and the views
  README.md           optional: notes for the next author
  s00_scene.py        the model scripts, in any names that the name rules allow
  s10_hull.py
  ...
```

The asset id is `tertius.demo:ships/nostromo.model`. Fragments name pieces, materials and nodes of the built `.glb`, as "Ids and fragments" says. The sidecar is `nostromo.model.import.json` next to the folder. The scanner does not look for assets, sidecars or folder default files inside a model folder.

`model.json`:

```json
{
  "$schema": "../../../.schemas/model.schema.json",
  "format": 1,
  "scripts": ["s00_scene.py", "s10_hull.py", "s30_greebles.py", "s70_materials.py", "s75_uv.py", "s80_assemble.py", "s90_checks.py"],
  "seed": 1979,
  "parameters": { "seeds": { "greebles": 1979, "windows": 7 } },
  "bake": { "objects": ["Hull_Fore", "Hull_Mid", "Hull_Aft", "Engines"], "size": 4096, "samples": 8, "margin": 16 },
  "views": {
    "resolution": [1920, 1080],
    "samples": 192,
    "shots": [
      { "name": "three-quarter", "camera": [-185, -235, 75], "target": [0, 2, -2], "lens": 50, "sun": [-0.55, -0.7, 0.6] }
    ]
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `format` | Required | The version of this format: 1 |
| `scripts` | Required | The model scripts, in the order of the build. At least one. Each is a file name in the model folder. |
| `seed` | Required | An integer. Every random choice of the model comes from it (see "Model scripts"). |
| `parameters` | `{}` | Free JSON values that the scripts read. If an author tunes a value, the value belongs here and not in a script. |
| `bake.objects` | `[]` | The mesh objects whose materials the bake turns into texture maps, in the order of the bake. Empty: no bake. |
| `bake.size` | 1024 | The side of every baked map in pixels, a power of two from 64 to 8192 |
| `bake.samples` | 8 | Cycles samples per pixel of the bake |
| `bake.margin` | 16 | Pixels that the bake extends each UV island, so mips do not bleed |
| `views.resolution` | `[1920, 1080]` | The size of each view in pixels |
| `views.samples` | 128 | Cycles samples per pixel of each view |
| `views.shots` | `[]` | The views. `name` gives the file `views/<name>.png`. `camera` and `target` are positions in Blender axes, in m. `lens` is the focal length in mm on a 36 mm sensor, default 50. `sun` is the direction toward the key light, in Blender axes. |

Positions in `model.json` and in the scripts are in Blender axes and meters, as "Axes, units and scale" says: +Z up, and the front of the model toward -Y. One exception: a script can build in a frame of its own, if a later script turns the whole model into these axes before the export. The Nostromo does this: its scripts `s10` to `s75` build it with the nose toward +Y, the frame of its earlier project, and `s80_assemble.py` turns it. The shots and the built model are always in the final axes. A new model builds toward -Y from the start.

#### Model scripts

The model library runs a model script with one function, `run.run_script(folder, script)`. The build tool and an agent's `execute_blender_code` call both use it, so a script runs the same way in both. The function:

1. Reads `model.json` again, so an edit applies at once.
2. Makes sure that the model library in use is the one of the checkout that holds the model folder. Python loads a package once per process, so a bridge that another checkout started runs that checkout's library. The function then raises an error. A model folder outside any checkout, such as a copy in a scratch folder, passes.
3. Reloads the other modules of the model library, so a library edit applies in a long session of the bridge too. A module cannot reload itself while it runs, so the code of the authoring loop reloads `run` before each call (see the skill `procedural-modeling`).
4. Seeds Python's `random` module with the string `<seed>/<script file name>`.
5. Runs the script with `exec` in a new namespace. The namespace holds one name, `model`.
6. Returns the dictionary that the script assigns to `result`, or `{}`.

`model` holds:

| Member | Meaning |
|---|---|
| `model.folder` | The absolute path of the model folder |
| `model.seed` | The `seed` of `model.json` |
| `model.params` | The `parameters` of `model.json` |
| `model.rng` | A `random.Random` seeded with `<seed>/<script file name>`. A new script in the list does not change the random choices of the others. |

Rules for a model script:

- Every random choice comes from `model.rng`, or from a `random.Random` seeded with a value of `model.json`. The seeding of the `random` module is a guard against a stray call, not an interface.
- A script iterates in a fixed order. It never depends on the order of a `set`, because Python orders a set of objects by memory address.
- A script changes only the scene. It writes no file. The bake, the save and the export belong to the build tool. MPFB writes log files while it works, into Blender's user resource folder, which the build tool and the bridge point at a temporary folder of their own (see "Humans from MPFB").
- A script builds only the model: meshes, materials, the lights of the model, empties for node poses, and collision markers. It adds no camera and no light for the views. The build tool adds the stage of the views after it saved `model.blend`.
- A script marks a piece that is not a root with the custom property `tertius_piece = 1`, and a collision marker with `tertius_collision`, as "Models: nodes and pieces" says. The custom property survives a rename.
- The origin of each piece node is the origin of the piece in the engine, as rule 4 of "Models: nodes and pieces" says. The front of the model faces -Y.

#### The bake

The glTF format holds no procedural shader, so the build bakes the material of each object in `bake.objects` into texture maps. The contract of a bake object:

- It is a mesh with one material and an active UV map. A model script makes the UV map.
- No other object of the scene uses its material. The maps belong to one object, so two objects with one material would show the maps of the last bake. The bake stops with an error.
- Its material has one Principled BSDF node, linked to the material output.
- The metallic input of that node holds a constant. The baked material takes the same constant.

The bake writes three maps per object into `textures/<object>_<pass>.png`:

| Pass | What the bake reads | Color space |
|---|---|---|
| `basecolor` | The value that feeds the base color input, baked as emission | sRGB |
| `roughness` | The value that feeds the roughness input, baked as emission | Non-color |
| `normal` | The tangent-space normal of the whole material, from its normal input and from the geometry | Non-color |

Baking as emission keeps metallic and lighting out of the color and roughness maps. After the bake, the object gets a new material `<material>_Baked`, made from the three maps. The procedural material stays in `model.blend` with a fake user, so an author can bake it again. Emission and transparency are not baked. Give an object with a glowing or transparent look a material that the glTF exporter writes as it is. A Principled BSDF with constant inputs is such a material.

With `--device auto`, the default of the build tool, the bake runs on a GPU if Cycles finds one: Metal on macOS, and OptiX, CUDA, HIP or oneAPI on Linux. Otherwise the bake runs on the CPU. `--device cpu` forces the CPU. The Blender of nixpkgs on Linux has no CUDA or OptiX, so its bakes run on the CPU. A bake on the CPU gives the same bytes on every run. A bake on Metal does not (see "Determinism" below).

#### The build tool

```
tools/blender/build.sh <model folder> --out <folder> [--views] [--device auto|cpu]
```

Outside the assets shell, the build tool runs itself again inside it. It then:

1. Estimates the free space that the build needs, from `bake.objects` and `bake.size` of `model.json` (`tools/blender/needed_mb.py`). If the volume of the output folder has less, it stops with exit code 6. The estimate is 1 GB, plus 32 bytes per pixel of one map for Blender's temporary files while it bakes, plus 24 bytes per pixel and bake object for the three maps in `textures/` and again in `model.glb`. The Nostromo, with four bake objects and maps of 4096 x 4096, needs 3 GB. A build with no bake needs 1 GB. The measured peak of the Nostromo was 344 MB of temporary files and 257 MB of outputs.
2. If the output folder exists, is not empty and holds no `build.json`, stops with exit code 2. A folder with `build.json` holds an earlier build, and the build tool replaces it. So a wrong path never loses files.
3. Runs Blender twice, with `TMPDIR` set to a new temporary folder, so Blender's temporary files land there, and `BLENDER_USER_RESOURCES` set to a folder inside it. Blender's user resource folder is where an add-on such as MPFB writes its logs and settings, so these files go away with the temporary folder, and the user's own Blender folder stays untouched. Blender's own output of both runs goes to `<out>/blender.log`.
   1. The build run: `$TERTIUS_BLENDER --background --factory-startup --disable-autoexec --python-exit-code 3 --python tools/blender/build.py -- <arguments>`.
   2. The export run, only after a good build run: the same command with `--threads 1 <out>/model.blend` and the arguments `--export <out>`. It opens `model.blend` and exports `model.glb`. With more than one thread, Blender computes the tangents of the export in an order that changes between runs, so their last bits differ (see "Determinism" below).
4. On every exit, also on an error or a signal, stops a Blender that still runs with SIGTERM. Then it deletes the temporary folder. Blender ignores Ctrl+C and SIGINT, and it leaves its temporary folder behind after SIGTERM. After SIGINT or SIGTERM, the build tool exits with code 130. SIGKILL runs no trap, so a caller that kills the build tool must also kill its Blender.
5. Prints `build.json` to standard output.

`build.py` runs inside Blender. The build run:

1. Reads `model.json` and checks it. A bad file or a missing script gives the status `badInput`.
2. Starts from an empty scene with metric units (`run.new_scene()`).
3. Runs every model script in order with `run.run_script`.
4. Bakes every object of `bake.objects`, as "The bake" says.
5. Saves `model.blend`. Its image paths are relative, `//textures/...`.
6. With `--views`, adds the stage and renders every shot into `views/<name>.png`. The stage holds a dark world with sparse stars, a key sun from the direction `sun` and one camera per shot. A weak fill sun shines from below, on the side of the camera. The stage renders on the device of `--device`. It comes after the save, so it is never saved and never exported.
7. Writes `build.json` with the status `failed` at the step `export`, until the export run replaces it. So a build whose export run never ends reports a failure.

The export run:

1. Exports `model.glb` with the export conventions below, with the camera check on.
2. Completes `build.json`: the root objects, the marked objects, the triangle count, the list of outputs and the status.

The outputs in the output folder:

| File | Contents |
|---|---|
| `model.glb` | The model: meshes, materials with the baked maps embedded, the lights of the model and the custom properties as `extras`. No cameras, and no lights of the stage. |
| `model.blend` | The built scene before the stage, with the procedural materials kept for a new bake |
| `textures/<object>_<pass>.png` | The baked maps, which `model.blend` loads |
| `views/<name>.png` | Only with `--views`: one image per shot |
| `build.json` | The report: status, Blender version, bake device, the seconds and `result` of each script, each baked map, the root objects (`roots`), the objects marked with `tertius_piece = 1` (`marked`), the triangle count and the list of outputs. The importer decides the pieces, because it also reads the `PIECE_` prefix, the sidecar and the collision markers. On a failure: the step of the build, the script, the message and the traceback. |
| `blender.log` | Blender's own output |

Exit codes:

| Code | Meaning |
|---|---|
| 0 | The build finished. |
| 2 | Bad input: the arguments, `model.json`, a missing script, or an output folder that holds other files |
| 3 | The build failed: a script raised an exception, the bake or the export failed, or Blender stopped. `build.json` and `blender.log` say where. |
| 5 | Blender is missing: the build tool is outside the assets shell, and `nix develop .#assets` failed. |
| 6 | The volume of the output folder has less free space than the build needs. The message names both sizes. |
| 130 | SIGINT or SIGTERM stopped the build. The build tool stopped Blender and deleted its temporary folder. The output folder holds no `build.json`, so the next build into it needs a new folder. |

The export conventions live in one function, `export.write_glb`. The converter of "Native tools and libraries" calls the same function. So a converted file and a scripted model leave Blender with one set of options:

- `export_format='GLB'`, `export_yup=True`, `export_apply=True`, `export_extras=True`, `export_tangents=True`, `export_lights=True`, `export_cameras=False`, `export_materials='EXPORT'`, `export_image_format='AUTO'`, `export_import_convert_lighting_mode='SPEC'`, and no Draco compression.
- With `refuse_cameras=True`, which only `build.py` passes, the function fails when the scene holds a camera. A camera in a scripted model means that the stage leaked into the model. A third-party file often holds a camera, so the converter leaves the check off, and the exporter leaves the camera out.
- The exporter computes tangents only for triangles and quads. So the function adds a Triangulate modifier at the end of the modifier stack of each mesh for the time of the export. glTF holds triangles in any case, and the scene keeps its polygons.
- Blender runs the export with one thread (`--threads 1`), for the tangents.

Determinism: the same model folder, model library and Blender build on the same machine give the same meshes, UV maps and nodes. Some BMesh operators, such as `create_uvsphere` and `spin`, order what they make by memory address. So `MB.finish` puts the vertices of each mesh in the order of their coordinates and its faces in the order of their vertices before the weld. `model.blend` and `build.json` hold times and paths, so their bytes differ.

The first builds of the Nostromo (2026-10-26) tested the belief that a build gives the same `.glb` bytes on every run. Two causes broke it, and the second is fixed:

- A bake on Metal differs between runs: a few pixels of the maps differ by one level out of 255. A bake on the CPU gave the same maps in every test. So the importer bakes on the CPU (see "The importer and a scripted model"). On this Mac, the Nostromo's twelve maps take about 170 s on Metal and about 14 minutes on the CPU (measured at 1024 x 1024: 54 s for the twelve maps, and the time grows with the pixels).
- Blender computes MikkTSpace tangents on several threads, and the order of the sums changes between runs. Thirty calls of `calc_tangents` on the Nostromo's `Engines` in one Blender gave thirty different results. With `--threads 1`, all thirty were the same. The exporter rounds each tangent to four decimals, so most differences vanish, but one to four values of `Engines` changed in each build. The export run of the build tool and the converter therefore run with one thread. The export of the Nostromo takes about 1 s.

So two builds of a model with `--device cpu` give the same `.glb` bytes. Two builds on Metal give `.glb` files that look the same and differ in a few pixels of their maps.

#### The importer and a scripted model

The importer treats a model folder as one source asset:

- It runs the build tool with `--out bin/assets/<path>.tmp-<random>/build`, inside the temporary folder of its atomic write (see "Cooked output"). It passes no `--views`, and it passes `--device cpu`, because only a bake on the CPU gives the same maps on every run (see "Determinism" above).
- With exit code 0, it reads `build/model.glb` as the glTF source of the asset. Everything after that is the path of every model. A scripted model has no `converted.glb`, because `build/model.glb` is that file.
- Exit code 3 gives AS6002 with the message of `build.json` and the last lines of `blender.log`. Exit code 2 gives AS1007. Exit code 5 gives AS6001. Exit code 6 gives AS6007.
- The time limit is the configuration key `blenderTimeout`. When it expires, the importer sends SIGTERM to the build tool and waits up to 30 s. The build tool then stops Blender, deletes its temporary folder and exits with code 130. Only if the build tool still runs after the wait, the importer kills the whole process tree (`Process.Kill(entireProcessTree: true)`), and Blender's temporary folder stays. A kill of the build tool alone would leave Blender running, because SIGKILL runs no trap. Either way, the importer reports AS6005.
- The `build/` folder stays in the cooked folder of the asset (decision 23). The game never reads it, and the byte rules of "Caching and determinism" do not cover it: `build.json` holds the path of the model folder and the seconds of each step, and `blender.log` holds the paths of temporary folders and Blender's timings.
- A built package leaves out the `build/` folder of each scripted model (content packages and mods). A player needs only the cooked data.

The cache key of a scripted model uses the key inputs of "Caching and determinism" with these contents:

| Key input | Contents for a scripted model |
|---|---|
| `source` | The SHA-256 of a canonical JSON object. The object maps the path of `model.json` and of each script that it names to the SHA-256 of its bytes. The paths are relative to the model folder. `README.md` is not in the key, because the build never reads it. |
| `dependencies` | The repository path and the SHA-256 of each file of the build tool and the model library: `tools/blender/build.sh`, `tools/blender/build.py` and every `.py` file in `tools/blender/tertius_blender/` |
| `tools` | The tools of a glTF source, and also `blender`: the Nix store path of Blender, which names its version. SharpGLTF, meshoptimizer, the .NET runtime and the others still process `build/model.glb`. Also `mpfb` and `makehuman`: the Nix store paths of MPFB (`TERTIUS_MPFB`) and of the MakeHuman system assets (`TERTIUS_MAKEHUMAN_ASSETS`), because a model of a person runs MPFB's code on those assets. |

The bake device is not a key input, because the importer always bakes on the CPU.

A scripted model is code. It runs with the rights of the person who imports it, like the `code/` folder of a package. A built package holds no scripts, so a player never runs one. Read a scripted model from a stranger before you import its source package.

#### Humans from MPFB

A model of a person makes the person with MPFB 2.0.18, the MakeHuman add-on for Blender, inside the build (decision 27). The model library module `tertius_blender.mpfb` holds the calls, and the astronaut and the crew member are the worked examples:

```python
from tertius_blender import mpfb

body = mpfb.human(model.params["human"], "Crew")     # MPFB's human description: sliders, skin, eyes, hair, rig
mpfb.pose(body.parent, model.params["pose"])         # bone -> XYZ Euler degrees from the rest pose
joints = mpfb.joints(body)                           # joint name -> (x, y, z), from MPFB's joint helpers
parts = mpfb.freeze(body)                            # part name -> plain mesh: Body, Eyes, Eyebrows, ..., Hair
arm = mpfb.bone_weights(parts["Body"], points, mpfb.ARM_BONES)   # the arm weight of the body under each point
```

How MPFB runs in the build:

- MPFB is a Blender system extension from the Nix store (`native/blender-extensions.nix`, `BLENDER_SYSTEM_EXTENSIONS`). `mpfb.services()` enables it in the running Blender only. Blender runs with `--factory-startup`, so the preferences are never saved.
- The body-part assets come from the MakeHuman system asset pack, `makehuman_system_assets_cc0.zip`, CC0 1.0, pinned by hash in `native/makehuman-assets.nix`. The derivation keeps only the folders `skins`, `eyes`, `eyebrows`, `eyelashes`, `teeth`, `tongue` and `hair`, and it removes the skins `special_suit` and `toon`. The pack's clothes never reach the store, so a model cannot use them. `mpfb.services()` makes this folder MPFB's second asset root.
- MPFB and the asset pack are inputs of the build tool, like Blender itself, and not third-party sources of a package (decision 26). No asset id names them, no sidecar describes them, and no import reads them on its own: MPFB reads them while a model script runs. So they need no fetch record and no source cache. Nix pins their bytes by hash, the assets shell downloads them once per machine, and the cache key of a scripted model names their store paths (`tools` in "The importer and a scripted model"). The skin, eye, eyebrow, eyelash and hair images end up in `model.glb` as parts of the person. They are CC0 1.0, which asks for no credit, so `CREDITS.md`, which lists the sidecars, has no line for them. The `.nix` files record their source page, size and license.
- MPFB writes logs and settings into Blender's user resource folder while it loads and works. `mpfb.services()` stops with an error unless `BLENDER_USER_RESOURCES` names a folder. The build tool and the bridge point it at a temporary folder of their own, which they delete.
- MPFB deletes every material without users when it makes a human. `mpfb.human` gives the materials of earlier scripts a fake user for the time of the call, so a material that `s00_scene.py` made for a later script survives.
- `mpfb.human` takes MPFB's own human description, and it stops with an error that names the mistake before MPFB builds anything: a key that MPFB's presets lack, an asset that the pack lacks, a detail target that MPFB does not hold by exactly that name, or a skin or eye material type other than `MAKESKIN`. MPFB alone skips an unknown asset or target with a log line, takes the first target whose name starts with a partial name, and builds materials that `freeze` cannot rewire. `MAKESKIN` is the default of both material types. `mpfb.pose` stops on a bone that the rig lacks.
- MPFB uses Python's `random` module only for the names of temporary shape keys. `run.run_script` seeds that module for each script, so those names repeat too. The hair of the MakeHuman assets is a fixed mesh of cards, with no random part.
- Two builds of the astronaut and two of the crew member with `--device cpu` (2026-10-26) gave the same bytes for every mesh: 33 meshes and 16 meshes. Only the normal map of the largest bake object differed, `Suit` and `Jumpsuit`, because the multithreaded bake computes their tangents in a changing order (ticket ter-b4k3).

`mpfb.freeze` turns the human into plain meshes, for the export:

| Rule | Reason |
|---|---|
| It applies the pose and removes the rig. A human without a rig is frozen the same way. | The importer does not exist yet, so the frozen meshes carry the pose into the game. Once it maps skins (decision 17, "Meshes"), the humans keep their `game_engine` rig, 53 joints, and export the armature at rest instead of freezing it ([skinning-and-morphs.md](skinning-and-morphs.md), "The importer's glTF mapping"). |
| It applies the shape keys and the helper mask, and removes the modifiers. | The macro sliders are shape keys, and the joint cubes and tights of the base mesh are helper geometry, not a part of the body. |
| It keeps the vertex groups of the bones and removes the others. | Garment scripts choose faces by bone weight. The glTF exporter writes no vertex group without a skin. |
| It rewires each MakeSkin material into one image texture that feeds the base color of one Principled BSDF. | glTF holds that material as it is, and the bake is not needed. |
| Only the hair, the eyebrows and the eyelashes keep the alpha of their image, with blended drawing (`BLEND`). The skin and the eyes are opaque. | An opaque part with alpha would export as `BLEND` and sort badly. |
| It deletes the faces of the eyes whose image is clear, the cornea. | Drawn opaque, the clear shell hides the iris. |

What MPFB gives in the build, and what it does not:

| Feature | In the build | Note |
|---|---|---|
| Macro sliders | Yes | `phenotype` of the human description |
| Detail targets | Yes | `targets` of the human description: the nose, the ears, the chin and the other parts |
| Eye colors | Yes | `alternative_materials` of the human description |
| MakeSkin skin | Yes | Rewired by `freeze` |
| Eyes, eyebrows, eyelashes, teeth, tongue | Yes | The MakeHuman assets |
| Hair | Yes | Alpha cards, drawn blended |
| Rig and pose | Yes, frozen | `game_engine` rig. `pose` sets bone rotations. |
| Expressions | No | MPFB 2.0.18 ships no expression presets: its folder `data/expressions` holds only a README. Its expressions need the face unit pack `faceunits01`, which is not pinned, and the system asset pack holds none. |
| Clothes | No, by rule | The scripts make every garment (decision 27). |
| Subdivision, proxies | Not used | `deserialization_settings(subdiv_levels=0)`. The base mesh is dense enough. |

#### Agents and the MCP server

The assets shell holds Blender 5.2.2 and `blender-mcp` 1.0.3. Its setup:

1. Nix gives Blender the MCP add-on and MPFB as system extensions: `native/blender-extensions.nix` makes a folder with both in `system/`, and the assets shell names it in `BLENDER_SYSTEM_EXTENSIONS`. The add-on file `mcp-1.0.3.zip` comes from a fixed-output Nix fetch with a pinned hash. Nothing goes into the user's Blender folders, so no setup step runs once per machine. (This replaced `tools/blender/setup.sh`, which installed the add-on into the user's extensions, on 2026-10-26. A Blender of the user then saw the add-on, and an uninstall was a manual step.)
2. `tools/blender/bridge.sh start` starts the bridge: `blender --background --factory-startup --online-mode --addons bl_ext.system.mcp --command blender_mcp`, with `TMPDIR` and `BLENDER_USER_RESOURCES` pointed at folders in the bridge's own state folder. Without online mode, the add-on opens no socket. `tools/blender/bridge.sh stop` stops it with SIGTERM and deletes its temporary folder. One bridge serves the machine, because the MCP server of every session connects to port 9876. The bridge belongs to the checkout that started it: `bridge.sh` records that checkout, and a start or a stop from another checkout or worktree stops with an error that names the owner. So a second agent never drives, wipes or stops the scene of the first.
3. `.mcp.json` registers the server `blender`, which runs `nix develop .#assets --command blender-mcp`. The MCP server connects to the bridge for each tool call.

An agent runs model scripts through `execute_blender_code` with the same function as the build tool:

```python
import importlib, sys
sys.path.insert(0, "/path/to/tertius/tools/blender")
from tertius_blender import run
importlib.reload(run)

run.new_scene()
result = run.run_script("/path/to/tertius/content/tertius.demo/assets/ships/nostromo.model", "s10_hull.py")
```

The agent looks at the scene through the render tools of the MCP server. They need a scene camera, so the agent first adds the stage with `stage.build` and picks a shot with `stage.use_shot`. `stage.build` also sets Cycles to the GPU, if it finds one, and saves every scene setting that it changes. A render tool writes its image into the temporary folder of the bridge and returns the path, which the agent opens with the Read tool. The MCP server waits at most 300 s for one tool call, so a render that takes longer fails the call while the bridge renders on. `stage.remove()` deletes the stage again before the next model script runs, and restores the saved settings. For the full build, the bake and the views, the agent runs the build tool with `--views` into a scratch folder. The project skill `procedural-modeling` holds the way of work, and the Nostromo is its worked example.

#### Readers and writers

| File | Writers | Readers |
|---|---|---|
| `<name>.model/model.json` | The author | `run.run_script`, `build.py`, the importer (AS1006, AS1007 and the cache key), editors through `.schemas/model.schema.json`, which `tertius content schema` writes |
| `<name>.model/*.py` | The author | `run.run_script` in the build tool and in the bridge, the importer (the cache key) |
| `<name>.model/README.md` | The author | People and agents. The importer only checks that it is the one extra file (AS1006). |
| `<name>.model.import.json` | The author | The importer |
| `tools/blender/tertius_blender/*.py` | Engine authors | Model scripts, `build.py`, the bridge, the importer (the cache key) |
| `tools/blender/build.py` | Engine authors | Blender, started by the build tool, and the importer (the cache key) |
| `tools/blender/build.sh` | Engine authors | The importer (it runs the tool, and the file is in the cache key), agents |
| `tools/blender/bridge.sh` | Engine authors | Agents and people |
| `native/mpfb.nix` | Engine authors | Nix, for the assets shell's `TERTIUS_MPFB`: the unpacked MPFB 2.0.18 add-on, pinned by hash. `native/blender-extensions.nix` links it as the system extension `mpfb`. |
| `native/makehuman-assets.nix` | Engine authors | Nix, for the assets shell's `TERTIUS_MAKEHUMAN_ASSETS`: the skins, eyes, eyebrows, eyelashes, teeth, tongues and hair of the MakeHuman system assets, pinned by hash. `tertius_blender.mpfb` makes it MPFB's second asset root. |
| `native/blender-extensions.nix` | Engine authors | Nix, for the assets shell's `BLENDER_SYSTEM_EXTENSIONS`: the MCP add-on and MPFB as Blender system extensions |
| `.mcp.json` | Engine authors | Claude Code |
| `build/model.glb` | `build.py` | The importer, agents |
| `build/model.blend` | `build.py` | Agents and people in Blender. The importer does not read it. |
| `build/textures/*.png` | `build.py` (the bake) | `model.blend`. `model.glb` embeds its own copies. |
| `build/build.json` | `build.py` | `build.sh` (the exit code), the importer (AS6002), agents |
| `build/blender.log` | `build.sh` | The importer (AS6002), agents |
| `views/<name>.png` | `build.py` with `--views` | Agents, with the Read tool. The importer never asks for views. |

### Import configuration

The configuration of one asset comes from four sources. Each later source wins over the earlier ones:

1. The built-in defaults of this plan.
2. The `defaults.import.json` files, from the `assets/` folder down to the folder of the asset.
3. The name rules: suffixes of the file name and folder names.
4. The sidecar of the asset.

The loader merges them with the merge rule of content formats. A nearer source describes fewer files, so it is more specific. A file name describes one file, so it wins over the folders above it. The sidecar is the most specific source.

Import files are plain JSON that the binder of content formats reads, so they get positions, the CT2xxx and CT3xxx errors and a schema. `tertius content schema` (content packages and mods) also writes `.schemas/import.schema.json`, and the VS Code entries map `*.import.json` to it.

A sidecar, `assets/props/rock-moss-set-01.gltf.import.json`. Its `files` key is the fetch record: the URL, size and SHA-256 of each downloaded file, at its path in the source tree. Its `approved` key records the approval of the user (see "Fetched sources").

```json
{
  "$schema": "../../.schemas/import.schema.json",
  "source": {
    "title": "Rock Moss Set 01",
    "authors": ["Kless Gyzen"],
    "provider": "polyhaven",
    "page": "https://polyhaven.com/a/rock_moss_set_01",
    "license": "CC0-1.0",
    "retrieved": "2026-10-10",
    "files": {
      "rock-moss-set-01.gltf": { "bytes": 9987, "sha256": "sha256:3b1f0c…",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/gltf/2k/rock_moss_set_01/rock_moss_set_01_2k.gltf" },
      "rock_moss_set_01.bin": { "bytes": 1466380, "sha256": "sha256:9a04e2…",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/gltf/8k/rock_moss_set_01/rock_moss_set_01.bin" },
      "textures/rock_moss_set_01_diff_2k.jpg": { "bytes": 649415, "sha256": "sha256:51c7aa…",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_diff_2k.jpg" },
      "textures/rock_moss_set_01_nor_gl_2k.jpg": { "bytes": 840957, "sha256": "sha256:0d93f4…",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_nor_gl_2k.jpg" },
      "textures/rock_moss_set_01_rough_2k.jpg": { "bytes": 293827, "sha256": "sha256:e2b810…",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_rough_2k.jpg" }
    },
    "approved": { "on": "2026-10-10", "record": "sha256:c81e07…" }
  },
  "model": {
    "collision": { "moving": "decompose" },
    "lods": { "count": 2 }
  }
}
```

A folder default file, `assets/props/defaults.import.json`, that makes every model in `props/` use convex decomposition, with every texture of those models at most 2048 pixels:

```json
{
  "$schema": "../../.schemas/import.schema.json",
  "model": {
    "collision": { "moving": "decompose" },
    "textures": { "maxSize": 2048 }
  }
}
```

The section `texture` applies to standalone texture assets, and `model.textures` applies to the textures inside a model file. A folder default file can hold both sections, because its folder can hold both kinds of asset. Only a sidecar gets AS1005 for a section that does not fit the kind of its asset.

#### Model configuration

| Key | Default | Meaning |
|---|---|---|
| `scale` | 1 | A uniform factor on every position. Use 0.01 for a model in centimeters. |
| `front` | `"+z"` | The glTF axis that is the front of the model: `"+z"`, `"-z"`, `"+x"` or `"-x"`. The importer turns it to -Z. |
| `expectedSize` | `[0.01, 5000]` | The plausible range of the largest extent, in m. Outside it, warning AS2013. |
| `pieces` | `{}` | Node name to piece configuration. A listed node becomes a piece. |
| `doubleSided` | `"duplicate"` | `"duplicate"` adds back faces. `"ignore"` keeps one side. |
| `lods.count` | 3 | The largest number of LODs after LOD 0, 0 to 4 |
| `lods.ratio` | 0.5 | The fraction of triangles that each LOD keeps of the level before it |
| `lods.maxError` | 0.02 | The largest error of a LOD, relative to the size of the mesh |
| `lods.minReduction` | 0.25 | A LOD must remove at least this fraction of triangles. Otherwise the chain ends. |
| `collision.moving` | `"hull"` | The moving collision with no markers: `"hull"`, `"decompose"`, `"box"`, `"sphere"`, `"capsule"` or `"none"` |
| `collision.static` | `"mesh"` | The static collision with no markers: `"mesh"` or any value of `moving` |
| `collision.meshError` | 0.01 | m: the largest simplification error of a static triangle mesh made from render geometry |
| `collision.decompose` | See "Collision" | The options of V-HACD |
| `blenderTimeout` | 1800 | s: the time limit of a Blender run, for a conversion and for the build of a scripted model. A scripted model with a large bake needs minutes. It changes no output, so it is not part of the cache key. |
| `lights.import` | `true` | Import the point and spot lights of the model |
| `lights.castsShadow` | `false` | Default for lights without the custom property `tertius_cast_shadow` |
| `textures` | | Texture keys that apply to every texture of the materials of the model |
| `budget.triangles` | 100000 | The largest triangle count of LOD 0 of the whole file. Above it, warning AS2015. |
| `budget.gpuMegabytes` | 64 | The largest GPU memory of the whole file, in MiB. Above it, warning AS2015. |

#### Texture configuration

| Key | Default | Meaning |
|---|---|---|
| `role` | From the name rules, otherwise `"albedo"` | `"albedo"`, `"normal"`, `"normalDx"`, `"metallicRoughness"`, `"emissive"`, `"data"` or `"ui"` |
| `maxSize` | 4096 | The largest side in pixels, a power of two, at most 16384 |
| `mips` | `true` (`false` for `ui`) | Make every mip level |
| `compression` | `"bc"` (`"none"` for `ui`) | `"bc"` or `"none"`. `"none"` stores RGBA8. |
| `toksvig` | `true` | Widen roughness in the mips of a bumpy normal map (see "Textures") |

#### Name rules

Texture roles from the end of the file name, without the extension. Matching ignores case, and the separator is `_` or `-`:

| Suffix | Role |
|---|---|
| `albedo`, `basecolor`, `diffuse`, `diff`, `color`, `col` | `albedo` |
| `normal`, `nor_gl`, `normalgl`, `nrm` | `normal` (OpenGL convention, +Y green, as in glTF) |
| `nor_dx`, `normaldx` | `normalDx`: the importer flips green |
| `metalrough`, `metallicroughness`, `arm`, `orm` | `metallicRoughness`: G is the roughness, B is the metallic, as in glTF. R (occlusion) is not read. |
| `emissive`, `emission` | `emissive` |
| `rough`, `roughness`, `metal`, `metallic`, `metalness`, `ao`, `disp`, `height` | `data` |

A folder named `ui` anywhere in the path gives the role `ui`.

Node names in a model, with prefixes that come from Unreal Engine (research):

| Prefix | Meaning |
|---|---|
| `PIECE_` | The node is a piece. |
| `UBX_` | Collision: a box fitted to the mesh of the node |
| `USP_` | Collision: a sphere fitted to the mesh of the node |
| `UCP_` | Collision: a capsule fitted to the mesh of the node, along the local +Y axis of the engine (local +Z in Blender) |
| `UCX_` | Collision: the convex hull of the mesh of the node |
| `UTM_` | Collision: the triangles of the mesh of the node. Static use only. |
| A suffix `_LOD1` to `_LOD9` | A hand-made LOD. The importer ignores the node and reports AS2014. |

Custom properties in Blender give the same marks and survive a rename: `tertius_piece`, `tertius_collision` (`"box"`, `"sphere"`, `"capsule"`, `"hull"` or `"mesh"`) and `tertius_cast_shadow`. Blender writes custom properties into glTF `extras` with the export option that the converter turns on.

### Axes, units and scale

| | Blender | glTF | Engine |
|---|---|---|---|
| Up | +Z | +Y | +Y |
| Front of a model | -Y | +Z | -Z |
| Right of a model | -X | -X | +X |
| Units | m (with the default unit scale) | m | m |
| Angles | degrees in the interface | radians | radians |

The Blender exporter turns +Z up into +Y up. The importer then turns the model by 180° about +Y. So a position (x, y, z) in Blender becomes (-x, z, y) in the engine. The front of the model then faces -Z, its right side faces +X, and the camera, spot lights and models agree. The `front` key changes the turn for a model that was built facing another way.

Transforms:

- Each glTF node has a translation, a rotation and a scale. The importer splits the world transform of each node into a rigid transform (translation and rotation) and a scale.
- Positions, normals and tangents of a piece are baked into the axes of the piece node. Normals use the inverse transpose.
- A transform with a negative determinant mirrors the geometry. The importer then reverses the winding of each triangle, as the glTF specification requires.
- A non-uniform scale on a node with rotated children gives shear. The importer bakes the shear into the vertices and reports warning AS2012. Node poses stay rigid.
- The `scale` key multiplies every position after the turn.

The importer reports the size of each piece in meters. A largest extent outside `expectedSize` is warning AS2013, and the hint names the likely unit mistake, such as centimeters in an FBX file. The preview shows a 1.8 m figure next to the model, so a wrong scale is visible.

### Meshes

The importer makes one mesh per material of each piece, in this order:

1. Read the triangles of each primitive. Triangle strips and fans become triangle lists. Points and lines are skipped (AS2004).
2. Read the attributes. Sparse accessors and KHR_mesh_quantization give plain floats.
3. If a primitive has no normals, make flat normals, as the glTF specification requires.
4. Bake KHR_texture_transform into the UVs (see "Materials").
5. Bake the node transforms into the axes of the piece (see "Axes, units and scale").
6. For a double-sided material, add a back copy of each triangle with reversed winding, a negated normal and a negated tangent sign. The renderer culls back faces, so this is the only way to see both sides.
7. Merge all primitives of the piece that use one material into one list.
8. Weld equal vertices with `meshopt_generateVertexRemap` over all attributes.
9. Make tangents (see below).
10. Optimize with `meshopt_optimizeVertexCache`, then `meshopt_optimizeOverdraw` with a threshold of 1.05, then `meshopt_optimizeVertexFetch`. The order is the one that the meshoptimizer documentation gives.
11. Make the LODs (see below).
12. Compute the bounds.

The engine vertex is 48 bytes. The importer fills it like this:

| `Vertex` field | glTF source | Notes |
|---|---|---|
| `Position` | `POSITION` | In the axes of the piece, in m |
| `Normal` | `NORMAL` | Unit length |
| `Tangent` | `TANGENT` or generated | xyz is the tangent, w is the bitangent sign. Zero for a material with no normal map. The shader then picks any perpendicular. |
| `TexCoord` | The `TEXCOORD_n` set that the textures of the material use | (0, 0) is the top left of the image in glTF and in `TextureData`, so V does not flip. |

Ignored attributes:

- `COLOR_0` (warning AS2007). The renderer has no vertex colors.
- A second UV set (warning AS2017). The renderer has one UV set.
- Animations (info AS2005).

Skins and morph targets ([skinning-and-morphs.md](skinning-and-morphs.md), "The importer's glTF mapping"):

| glTF | Engine | Notes |
|---|---|---|
| The joints of every skin of a piece's meshes | `Rig.Joints`, one rig per piece | The union of the joints, sorted parents first, siblings in glTF node order. Names follow the name rules, and a repeated name takes AS2008's suffix. |
| A joint node's transform | `RigJoint.Rest` | Relative to its parent joint, or to the piece's origin for a root joint. The bake's turn and uniform scale apply. A scale or shear left after the bake is error AS2020. |
| `skin.inverseBindMatrices` | `MeshSkin.InverseBinds` | Identity when absent, as glTF says, then the bake: IB_j becomes IBM_j composed with the inverse of the bake. A rig joint outside the mesh's skin takes the inverse of its model rest pose, and no vertex names it. |
| `JOINTS_n`, `WEIGHTS_n` | `VertexSkin` | Joint indices remapped from the skin's list to the rig. The four largest of every set are kept (info AS2021) and scaled to sum to 1. A sum of 0 is error AS2022. |
| `targets[].POSITION`, `NORMAL`, `TANGENT` | `MorphTarget.Positions`, `Normals`, `Tangents` | Positions turn and scale with the bake. Normals and tangents only turn. |
| `mesh.extras.targetNames` | `RigMorph.Name` | A target without a name is named `target{i}`, by its index in the mesh. |
| `mesh.weights`, or `node.weights`, which wins | `RigMorph.Rest` | Two meshes of a piece that give one morph different weights are warning AS2023. The first in glTF order wins. |
| A mesh node whose parent is a joint, without a skin | A `MeshSkin` with every vertex on that joint, weight 1 | So a helmet modeled on the head bone follows the head. |
| More than 65,535 joints in a piece | None | Error AS2024. |

The skinned mesh node's own transform is ignored, as glTF says, and its vertices are baked from the bind space into the piece's axes. Every step above that copies or reorders vertices carries the skin and the deltas: the back copies of double-sided materials, the weld, which compares them as attributes, the vertex fetch order and the LODs. The cooked mesh format gains the rigging. The parts of a model share the piece's one `Rig`, so an entity's one rig pose fits each of them.

Tangents:

- If the glTF has tangents and the material has a normal map, the importer keeps them. Blender writes MikkTSpace tangents, which match normal maps baked in Blender.
- Otherwise the importer calls `meshopt_generateTangents` with `meshopt_TangentCompatible`. The meshoptimizer header says that this option gives MikkTSpace-compatible tangents.
- `meshopt_generateTangents` gives one tangent per triangle corner. The importer then welds again over all attributes, tangents included, so a UV seam keeps two vertices.
- In meshoptimizer 1.2 the function is experimental. The header defines `MESHOPTIMIZER_EXPERIMENTAL` as `MESHOPTIMIZER_API`, so the shared library exports it. A test compares its tangents with tangents that Blender wrote for the same mesh.

Quantization is left out. The renderer has one vertex layout in one global buffer. A second, quantized layout costs a second pipeline path, and memory is not a problem yet: a model of 100,000 vertices uses 4.8 MB.

LODs:

- Level k targets `lods.ratio`^k of the triangles of LOD 0. The importer always simplifies from LOD 0, because the meshoptimizer documentation advises it.
- It calls `meshopt_simplifyWithAttributes` with `meshopt_SimplifyLockBorder`, so the parts of one piece stay closed where two materials meet. Normals and UVs are attributes with a weight of 0.5 each. Step 9 tunes the weights with the LOD previews.
- The target error is `lods.maxError`, relative to the mesh size. The result error times `meshopt_simplifyScale` gives the error in meters, which the LOD keeps.
- The chain ends at `lods.count` levels, or at the first level that removes less than `lods.minReduction` of the triangles of the level before it.
- A LOD is an index list into the vertices of LOD 0. The vertices are not copied, because `meshopt_simplify` returns indices into the original vertex buffer.
- Each LOD index list then goes through `meshopt_optimizeVertexCache`.

Bounds: the axis-aligned box of the vertices, and a sphere around the center of that box. The renderer also gets its own convention, the largest distance of a vertex from the origin.

### Materials

The importer converts each glTF material to a `MaterialDesc` (see [pbr-brdf.md](pbr-brdf.md)). The values of glTF metallic-roughness pass through as they are. The textures pass through too, with three exceptions that "Packing into `MaterialDesc`" names. A texture sample counts as 1 where the material has no texture.

```
Inputs, all linear:
  base = baseColorFactor                                RGBA
  m    = metallicFactor
  r    = roughnessFactor
  ior  = KHR_materials_ior.ior                          default 1.5
  sf   = specularFactor                                 KHR_materials_specular, default 1
  sc   = specularColorFactor                            default 1
  Y(c) = 0.2126 c.r + 0.7152 c.g + 0.0722 c.b           luminance

Outputs:
  BaseColor   = base                                    RGBA, the opacity is base.a
  Metallic    = m
  Roughness   = r
  F0d         = min(((ior − 1) / (ior + 1))² × sc, 1) × sf    dielectric reflectance, KHR_materials_specular, per channel
  Reflectance = clamp(sqrt(Y(F0d) / 0.16), 0, 1)        inverse of the engine's F0 = 0.16 × Reflectance² for a non-metal
  Emissive    = emissiveFactor × emissiveStrength       KHR_materials_emissive_strength, default 1
  EmissiveMap = emissiveTexture                         the new slot of MaterialDesc
```

Reasons for the formulas:

- The engine has one reflectance for a non-metal, and it is white. So a colored F0d becomes its luminance.
- The reflectance has no map channel. So `specularTexture` and `specularColorTexture` are not read, and only their factors count (warning AS3005).
- For the default dielectric (IOR 1.5), F0d is 0.04, and the reflectance is exactly 0.5, the default of `MaterialDesc`.
- The renderer takes the base color of a metal as its F0, and the diffuse color of a metal is 0. So the importer does nothing special for metals.
- The engine floors the roughness at 0.045 (`pbr-brdf.md`), so the importer does not.

Packing into `MaterialDesc`:

- `BaseColorMap` gets the glTF base color texture. `MetallicRoughnessMap` gets the glTF metallic-roughness texture, which has the engine's layout: roughness in G, metallic in B. R and A are not read. Each map channel multiplies its factor, as in glTF.
- The first exception is the normal map. `NormalMap` gets the normal map with its `scale` baked in: n = normalize((t × 2 − 1) × (scale, scale, 1)).
- The second exception is Toksvig (see "Textures"). A material with a normal map and `toksvig` on gets a `MetallicRoughnessMap` whose mips carry the widened roughness. For this, the importer bakes the factors into the map: G holds roughness factor × G, B holds metallic factor × B, and both factors become 1. A material with no such map gets a constant one.
- A baked texture uses the size of the largest input texture. The importer resizes smaller inputs with the filter of "Textures".
- Two materials that bake to equal bytes share one texture. The comparison uses the SHA-256 of the texels.
- The third exception is a texture set with separate roughness and metal images. A material with `tertius_roughnessTexture` or `tertius_metallicTexture` in its `extras` (see "Texture sets") gets a baked `MetallicRoughnessMap`: G holds roughness factor × the R channel of the roughness image, and B holds metallic factor × the R channel of the metal image. A missing image counts as 1. Both factors become 1, as for Toksvig.

Alpha modes and shading models ([skin-and-hair.md](skin-and-hair.md), "The importer's mapping from glTF"). The material holds both, `MaterialDesc.Alpha` and `MaterialDesc.Model`, so a model part has no flag of its own. The alpha of a cutout, fringed or glass material is base color map alpha times base color alpha.

| glTF | Engine |
|---|---|
| `alphaMode` `OPAQUE` | `OpaqueAlpha`. The base color texture loses its alpha channel. |
| `alphaMode` `MASK`, `alphaCutoff` c (default 0.5) | `CutoutAlpha(c)` |
| `alphaMode` `BLEND` | `GlassAlpha`, the glass rule of the transparent pass |
| `alphaMode` `MASK` or `BLEND` with `extras.tertius_alpha` = `"fringe"` | `FringedAlpha(c)`, with c the `alphaCutoff` when the file has one, else 0.5 |
| No `extras.tertius_shading`, or `"standard"` | `StandardShading(metallicFactor)` |
| `extras.tertius_shading` = `"skin"` | `SkinShading` |
| `extras.tertius_shading` = `"hair"`, with `extras.tertius_strand` = `"+u"`, `"-u"`, `"+v"` or `"-v"` | `HairShading(...)`. A hair material without `tertius_strand` is error AS3006. |

Blender writes a material's custom properties into its glTF `extras`, because the build tool exports with `export_extras=True`. glTF's `BLEND` composites by Porter and Duff's "over", which `FringedAlpha(1)` draws exactly, but `BLEND` stays glass, so a glass made in Blender keeps the glass rule.

Other material features:

| Feature | What the importer does |
|---|---|
| `doubleSided` | Back faces are duplicated geometry (see "Meshes"). |
| KHR_materials_unlit | Base color 0, metallic 0, reflectance 0, emissive = base color, and the base color texture becomes the emissive map |
| KHR_texture_transform | Baked into the UVs of the primitives of the material. Different transforms on two textures of one material are error AS3004. |
| `occlusionTexture` | Dropped (info AS3002). The G-buffer has no free channel for ambient occlusion. |
| `texCoord` other than the set of the base color texture | The base color set wins (warning AS2017). |
| KHR_materials_clearcoat, sheen, transmission, volume, iridescence, anisotropy, dispersion, diffuse_transmission | Ignored (warning AS3005) |
| KHR_materials_pbrSpecularGlossiness | Converted: its diffuse is the base color, its metallic is 0, its specular gives the reflectance by clamp(√(Y(specular) / 0.16), 0, 1), and its glossiness g gives the roughness 1 − g. |
| KHR_materials_variants | The default material is used (warning AS3005). |

### Lights

The importer reads KHR_lights_punctual point and spot lights. They become lights of their piece, with an offset from the piece origin. glTF lights point along -Z of their node, as engine lights do.

```
power (W)          = 4π · candela / 683                        for a point light
power (W)          = 2π (1 − cos θ_o) · candela / 683          for a spot light of outer half-angle θ_o
radius             = 0: a punctual light has no size
cone angles        = glTF innerConeAngle and outerConeAngle (half-angles in radians; glTF defaults 0 and π/4)
castsShadow        = the custom property tertius_cast_shadow, or lights.castsShadow
```

- The Blender exporter in its default lighting mode writes candela = W / (4π) × 683 for point and spot lights (glTF-Blender-IO `blender/exp/lights.py`). So the point light's formula gives back Blender's watts. A Blender spot light sends its watts into every direction as a point light would, and the importer keeps only what goes through the cone, as the engine's spot light sends its whole power through its cone.
- The importer writes no range and no cutoff: the renderer derives a light's intensity and its reach from the power, in each frame (local-light-shadows.md, "The cutoff and the reach"). A glTF range is ignored. Step 11 measures the GPU cost of the lights of a real interior.
- A directional light is ignored (warning AS2019). A star is an entity with a point light of its real power, at its real distance, not a light of a model.
- Emissive values pass through unchanged. KHR_materials_emissive_strength is a unitless factor (its specification), and the Blender exporter writes the Blender emission strength without a conversion. The engine adds emissive as radiance, and its diffuse term is base color × (1 − metallic) / π. So an emission strength of 1 looks as bright as in Blender.

The content formats design states the same unit, the watt, for the light sections of content files.

### Textures

#### Roles

A role says what an image holds. A slot (`TextureSlot` of content formats) says what a field or a material key needs. An image fits a slot in one case: its role is in the row of that slot. The slot `Any` takes every role.

| Role | Source color space | Slot | Format | Bytes per pixel | Mips |
|---|---|---|---|---|---|
| `albedo` | sRGB | `BaseColor` | `Bc7Srgb` | 1 | Yes |
| `normal`, `normalDx` | Linear | `Normal` | `Bc5Unorm` (X and Y) | 1 | Yes |
| `metallicRoughness` | Linear | `MetallicRoughness` | `Bc7Unorm`: the map needs G and B, and BC5 holds two channels | 1 | Yes |
| `emissive` | sRGB | `Emissive`, the new `EmissiveMap` of a material | `Bc7Srgb` | 1 | Yes |
| `data` | Linear | `Data`, for component fields | `Bc4Unorm` for 1 channel, `Bc5Unorm` for 2, `Bc7Unorm` for 3 or 4 | 0.5 or 1 | Yes |
| `ui` | sRGB | `Ui`, for component fields | `Rgba8Srgb` | 4 | No |

`compression` `"none"` stores `Rgba8Srgb` or `Rgba8Unorm` for any role. The mips add one third. A 2048 × 2048 BC7 texture with mips uses 5.6 MB.

The normal map stores only X and Y. The shader computes Z = sqrt(1 − X² − Y²), which is a renderer change. The built-in flat normal texture and the RGBA8 normal maps of today still work, because their vectors have unit length.

#### Processing

1. Read the size from the header with `ImageInfo.FromStream` before the image is decoded. A side over 16384 pixels is error AS4002. This also stops an image file that claims a huge size.
2. Decode. Convert sRGB roles to linear floats.
3. For `normalDx`, flip green.
4. If a side is not a power of two, resize to the nearest power of two (info AS4003). The filter is a separable Lanczos-3 in linear space. BC blocks and mip chains are simplest with power-of-two sizes.
5. Make the mips with a 2 × 2 box filter in linear space:
   - Base color and emissive: filter color weighted by alpha, then divide by the filtered alpha. Transparent texels then do not darken the edges.
   - Normal: average the vectors. Keep the average length |Na| for Toksvig. Renormalize.
   - Metallic and roughness: average the metallic. Average the roughness, convert it to n by n = 2 / α² − 2 with α = roughness², apply Toksvig with |Na| of the normal map at the same level, and convert back.
   - Data: average each channel.
6. Drop the top levels until the largest side is at most `maxSize` (info AS4004). The kept levels are then the same as a fresh build at the smaller size.
7. Encode each level with BCnEncoder.Net: `BcEncoder.EncodeToRawBytes` with its own mipmap generation off, one call per level, at the quality `Balanced`.
8. Write a KTX2 file.

Toksvig widens the roughness where a normal map is bumpy at a small mip, so highlights do not sparkle. With exponent n and the average normal length |Na| of the mip:

```
ft = |Na| / (|Na| + n × (1 − |Na|))
n' = ft × n
```

The [anti-aliasing](anti-aliasing.md) design lists Toksvig as an optional renderer feature. Doing it in the mips costs nothing at runtime. A material with a normal map and a constant roughness then gets a `MetallicRoughnessMap` texture, so that the mips can carry the widened roughness. `toksvig: false` turns this off for one asset or folder.

#### The container

The importer writes KTX2 version 2.0 files with its own small writer, and the store reads them with its own reader:

- The header holds `vkFormat` of the final GPU format, so the renderer uploads the blocks unchanged.
- No supercompression.
- One basic data format descriptor block for the format.
- The key-value data holds only `KTXorientation` = `rd`, which means that the top row comes first, and `KTXwriter` = `Tertius importer 1`. No dates, so the bytes are deterministic.
- The level data is stored from the smallest level to the largest, with the padding that the specification requires.

`ktx validate` from ktx-tools 4.4.2 tests every written file in the test suite.

#### Texture sets

A texture set is a group of images for one material, such as the downloads of Poly Haven and ambientCG. `tertius asset add --kind texture-set` writes a material-only `.gltf` file with one material into `assets/`. The images are fetched files in a folder next to it, `painted-metal/` in the source tree, and the sidecar of the `.gltf` holds their fetch record. The importer then converts the material like any glTF material. A texture set therefore needs no new file format. The `.gltf` is text that intake writes, not a download, so git keeps it.

- The material uses the images by role, from the name suffixes: albedo as `baseColorTexture`, normal as `normalTexture`, and an ORM image (R occlusion, G roughness, B metal) as `metallicRoughnessTexture`.
- If the set has separate roughness and metal images, the material names them in its `extras`: `tertius_roughnessTexture` and `tertius_metallicTexture`, each a glTF texture info. The importer packs them into the `MetallicRoughnessMap` (see "Materials"). The packing is deterministic.
- A set with a DirectX normal map marks its normal texture info with `"extras": {"tertius_normalDx": true}`. The importer flips green, as for the role `normalDx`.

Before decision 26, intake packed the ORM image and flipped the green channel itself, and wrote new image files. Now the images are fetched files with pinned bytes, and a packed or flipped image would be binary build output in git. So the importer does both at import, and the `.gltf` names the images as they were downloaded.

The material is then `materials/painted-metal.gltf#painted-metal`, or `materials/painted-metal.gltf` because the file has one material.

#### Size limits

| Limit | Value | Source |
|---|---|---|
| Largest side | 16384 | `MaxImageDimension2D` of the M1 Max through MoltenVK |
| Default `maxSize` | 4096 | A 4096 × 4096 BC7 texture with mips uses 22 MB. |
| Default fetch resolution | 2k | See "Fetching and intake" |
| User textures | 4093 | The bindless array of the renderer |

### Collision

#### Facts that shape the design

- A Jolt mesh shape must be static. The Tertius backend rejects a mesh shape on any body that moves (research).
- A Jolt convex hull has at most 256 points, and Jolt drops interior points first (Jolt 5.6.0 source, `cMaxPointsInHull`).
- joltc exports `JPH_ConvexHullShape_GetNumPoints`, `JPH_ConvexHullShape_GetPoint` and `JPH_Shape_GetCenterOfMass` (`joltc.h` at the pinned revision). The points of a built hull are relative to its center of mass.
- joltc exports no shape save or restore functions (research).
- Build times from one cold build in a Release probe on the M1 Max (research):

| Shape | Size | Build time |
|---|---|---|
| Convex hull | 64 points | 0.17 ms |
| Convex hull | 256 points | 1.25 ms |
| Triangle mesh | 16,000 triangles | 5.2 ms |
| Triangle mesh | 65,000 triangles | 22.7 ms |

#### What the importer makes

The mapping of content formats asks for a collision shape with a use. A static use is an `interiorShape` or a body with motion `static`. Every other body is a moving use.

| Use | Markers in the piece | Result |
|---|---|---|
| Moving | `UBX_`, `USP_`, `UCP_` or `UCX_` nodes | One shape for one marker, or a `CompoundShapeDesc` with child poses for several |
| Moving | Only `UTM_` nodes | Error AS5002: a triangle mesh cannot move |
| Moving | None | `collision.moving`: by default one convex hull of all render vertices of the piece |
| Static | `UTM_` nodes | One `MeshShapeDesc` of all `UTM_` triangles of the piece. The other markers are left out. |
| Static | Only `UBX_`, `USP_`, `UCP_` or `UCX_` nodes | The same shape as the moving use |
| Static | None | `collision.static`: by default a triangle mesh of the render geometry, simplified to `collision.meshError` |

If the piece has `UTM_` markers, a static use leaves out its convex markers. Convex hulls are solid. In the static shape of a ship, the hulls of its outer hull fill the inside of the ship. Put the outer surfaces into the `UTM_` mesh too, so that a person on a spacewalk can stand on the hull (ARCHITECTURE.md, Physics).

A collision marker inside a piece belongs to that piece. A marker that is a root node names its piece: `UCX_Hull_01` belongs to the piece `Hull`. The importer removes the prefix and a trailing `_` with digits. A root marker that names no piece is warning AS5006.

Fitting:

| Kind | Fit |
|---|---|
| Box | The axis-aligned box of the vertices in the axes of the node. The child pose is the node pose moved to the box center. |
| Sphere | Center at the center of that box, radius to the farthest vertex |
| Capsule | The axis is the local +Y of the node. The radius reaches the farthest vertex from the axis. The half height is half the extent along the axis minus the radius, at least 0. |
| Convex hull | Jolt builds a hull from the vertices at import. The importer stores the points of the result, at most 256, moved back by the center of mass. |
| Triangle mesh | Welded triangles. A `UTM_` mesh keeps every triangle. A mesh from render geometry is simplified with `meshopt_SimplifyErrorAbsolute` to `collision.meshError` meters. |
| Decomposition | V-HACD with the options below. Each hull then goes through the Jolt hull step. |

V-HACD options in `collision.decompose`:

| Key | Default | V-HACD parameter |
|---|---|---|
| `maxHulls` | 16 | `m_maxConvexHulls` |
| `resolution` | 400000 | `m_resolution` (voxels) |
| `maxVerticesPerHull` | 64 | `m_maxNumVerticesPerCH` |
| `errorPercent` | 1 | `m_minimumVolumePercentErrorAllowed` |

The importer runs V-HACD with `m_asyncACD` off. Step 12 runs it twice on one input and compares the hashes. If the outputs differ, that step pins V-HACD to one thread.

The importer builds every final shape once with `ShapeCooker.TryBuild` (see "Engine changes"). A shape that Jolt rejects is error AS5003 at import, not a failure in the middle of a game.

#### At load

The store makes one `ShapeDesc` object per (piece, use). Every body that names it shares it, as the reference-keyed `ShapeCache` of the physics backend needs (content formats). `AssetStore.Shapes` lists the current shape of each (piece, use), and a reload replaces the entry. After a content load, the host calls `IPhysicsBackend.Prepare` for each shape of `AssetStore.Shapes`, on every new physics backend. Jolt then builds the shapes outside a tick, and the sync point never stalls on a large shape. A demo with 40 hulls and two 16,000-triangle meshes prepares in about 20 ms by the measured costs.

Scale: Jolt shapes have no scale, and `RenderData.Scale` changes only rendering. A blueprint that scales a model by 2 does not scale its collision. Use the `scale` key of the import configuration instead.

### Cooked output

One folder per source asset, named like the source file:

```
bin/assets/ships/corvette.blend/
  cooked.json          the index: key inputs, pieces, meshes, parts, materials, textures, nodes, lights, shapes
  geometry.bin         every vertex and index of every mesh and LOD
  collision.bin        hull points and collision triangles
  textures/0.ktx2      one KTX2 file per texture, numbered in order of first use
  converted.glb        only for models that Blender converted
  converted.png        only for TIFF and WebP images, which Blender converted
  build/               only for scripted models: the outputs of the build tool (see "Scripted models"),
                       outside the byte rules of "Caching and determinism"
```

`cooked.json` (shortened to one piece):

```json
{
  "format": 1,
  "kind": "model",
  "key": "sha256:5d0c41a8…",
  "keyInputs": {
    "format": 1,
    "importer": "model/1",
    "tools": {
      "bcnencoder": "2.3.0",
      "blender": "/nix/store/b16pszpc…-blender-5.2.2",
      "dotnet": "11.0.0",
      "joltc": "/nix/store/…-joltc-double",
      "meshoptimizer": "/nix/store/…-meshoptimizer-1.2",
      "vhacd": "/nix/store/…-vhacd-4.1.0"
    },
    "platform": "osx-arm64",
    "source": "sha256:77e1d0…",
    "dependencies": {},
    "configuration": { "model": { "collision": { "moving": "hull" } } }
  },
  "pieces": [
    {
      "name": "Hull",
      "node": 0,
      "parts": [0, 1],
      "lights": [0],
      "bounds": { "min": [-12.1, -3.2, -30.6], "max": [12.1, 6.6, 30.7], "center": [0, 1.7, 0.05], "radius": 33.1 },
      "collision": { "moving": 0, "static": 1 }
    }
  ],
  "meshes": [
    { "vertexOffset": 0, "vertexCount": 30211, "indexOffset": 0, "lods": [
      { "indexOffset": 0, "indexCount": 123630, "error": 0 },
      { "indexOffset": 123630, "indexCount": 61812, "error": 0.0041 }
    ] }
  ],
  "parts": [
    { "mesh": 0, "material": 0, "offset": { "position": [0, 0, 0] } },
    { "mesh": 1, "material": 1, "offset": { "position": [0, 0, 0] } }
  ],
  "materials": [
    { "name": "HullPaint", "baseColor": [1, 1, 1, 1], "metallic": 0, "roughness": 0.2, "reflectance": 1, "emissive": [0, 0, 0],
      "baseColorMap": 0, "normalMap": 1, "metallicRoughnessMap": 2, "emissiveMap": null }
  ],
  "textures": [
    { "file": "textures/0.ktx2", "role": "albedo", "format": "bc7Srgb", "width": 2048, "height": 2048, "levels": 12 }
  ],
  "nodes": [
    { "name": "Hull", "parent": -1, "piece": 0, "pose": { "position": [0, 0, 0] } },
    { "name": "AirlockDoor", "parent": 0, "piece": 1, "pose": { "position": [0, 1.1, 16] } },
    { "name": "AirlockInner", "parent": 0, "piece": 0, "pose": { "position": [0, 1.1, 12] } }
  ],
  "lights": [
    { "kind": "point", "offset": { "position": [0, 2.4, -6] }, "color": [1, 0.85, 0.7], "power": 233.7, "radius": 0, "castsShadow": false }
  ],
  "shapes": [
    { "kind": "compound", "children": [ { "kind": "convexHull", "points": [0, 64], "pose": { "position": [0, 0, -20] } } ] },
    { "kind": "mesh", "vertices": [768, 4410], "indices": [0, 25080] }
  ]
}
```

Binary files:

- All numbers are little-endian. `geometry.bin` holds 48-byte `Vertex` records, then `uint32` indices. Each section starts at a multiple of 16 bytes.
- `collision.bin` holds float triples for points and vertices, then `int32` triangle indices, as `MeshShapeDesc` takes them.
- Offsets and counts in `cooked.json` count elements, not bytes.

The writer and the reader live in `Tertius.Assets`, so they cannot disagree. The reader makes sure that every count fits the file sizes and reports AS8003 otherwise.

Writes are atomic per asset:

1. Write the new folder as `bin/assets/<path>.tmp-<random>/`.
2. Rename the old folder to `.old-<random>`, then rename the new folder into place.
3. Delete the old folder.

A crash between two steps leaves no cooked folder, or an old one. The next import repairs both, and a reader never sees half a folder.

A lock file `bin/assets/.lock` keeps two importers out of one package at a time. An importer that finds the lock taken waits for it, up to `--lock-wait` seconds (default 120). The editor and an agent often import at the same time (see the editor design), so a short wait is normal. After the wait, the importer reports AS6006 and exits with code 7 of content packages and mods, not with code 3. An agent then knows that the content has no error.

### Caching and determinism

The cache key is the SHA-256 of the canonical JSON of `keyInputs`. Canonical JSON has keys in ordinal order, numbers in shortest round-trip form and no whitespace.

| Key input | Contents |
|---|---|
| `format` | The version of the cooked format |
| `importer` | The importer kind and its version number. A change of behavior raises the number. |
| `tools` | For each native tool and library that this kind of asset uses, its Nix store path. For each C# library, its package version. Also the .NET runtime version. A store path changes with every rebuild, also with the same version number, and a new runtime can change the results of `Math` functions. |
| `platform` | The runtime identifier, such as `osx-arm64` |
| `source` | The SHA-256 of the source file. For a fetched file, the SHA-256 that its fetch record pins, so the key needs neither the file nor a download (see "Fetched sources"). For a scripted model, the hash over its folder (see "Scripted models"). |
| `dependencies` | Path to SHA-256 of each file that the last import read besides the source: the `.bin` and images of a `.gltf`, and the files that Blender loaded for a `.blend`. A fetched dependency counts with its pinned SHA-256. For a source that Blender converts, also every `.py` file of the model library in `tools/blender/tertius_blender/`, because `export.write_glb` decides the bytes of `converted.glb`. For a scripted model: the files of the build tool and the model library. |
| `configuration` | The effective configuration, without `source` and without `blenderTimeout`, which change no output |

The `platform` input exists because native code and floating-point code can round differently on another CPU. The pipeline promises the same bytes on one platform, not across platforms.

An import of an asset:

1. Compute the key. A stamp file, `bin/assets/.stamps.json`, maps each source path to its size, modification time and hash. A file whose size and time did not change keeps its stored hash, and the importer does not read it again. A fetched file needs no stamp for the key, because its pin stands for it.
2. If the cooked folder holds the same key, the asset is up to date. A machine whose cooked output is up to date therefore sends no request, also with an empty source cache.
3. Otherwise, for an asset with fetched files, make sure that each one is in the source cache with its pinned bytes, and download the missing ones (see "Fetched sources").
4. Import it. A stale asset reports the reason, from a comparison of the old and new key inputs: `source`, `dependencies`, `configuration`, `importer`, `tools` or `format`.

The stamp is a shortcut, and it can be wrong. A copy that keeps the old size and time, such as `cp -p`, `tar` or `rsync -t`, hides a change. The stale output then looks fresh. `--rehash` hashes every file again, and CI always uses it. The base library of .NET has no public API for the inode or the change time of a file (probe). So the stamp cannot add them.

Ordering rules, each with its reason:

| What | Order | Reason |
|---|---|---|
| Assets of a package | Ordinal order of the path | Directory listings differ between operating systems. |
| Packages | Load order of content packages and mods | It exists already and is deterministic. |
| Nodes, meshes, materials and images of a glTF | glTF index order | The file stores them in that order. |
| Pieces | Node order, depth first | A parent comes before its child pieces. |
| Parts of a piece | Order of the first use of each material, depth first through the nodes | |
| Textures in a cooked asset | Order of first use by the materials | |
| Parallel work | Results are stored by index, not by finish time. | Thread timing must not change a result. |
| Diagnostics | The order of `DiagnosticBag.Sorted` (content formats): package load order, file path, line, column, pointer, code | One input gives one report. A `.glb` diagnostic has no line, which counts as 0. |
| Name dedupe | glTF index order | |

Other rules for deterministic bytes:

- No dates, paths of the local machine or random values in cooked output. The temporary folder names never reach the output. The `build/` folder of a scripted model is not cooked output in this sense: it is the report and the by-products of the build tool, and the game never reads it (see "Scripted models").
- JSON output has a fixed key order. Floats are written in shortest round-trip form with the invariant culture.
- C# image filters are scalar code, so the JIT cannot fuse operations differently between runs.
- The V-HACD shim builds with `-ffp-contract=off`.
- A test imports every fixture twice into fresh folders and compares every byte. A second test compares one thread with all threads, and a third reverses the asset order.

### Runtime: the asset store

`AssetStore` in `Tertius.Assets` implements the `IAssetResolver` of content formats. One store lives for the whole process. It outlives content sets. So `ReloadData` of content packages and mods gets the same ids for unchanged assets, and GPU memory does not grow with each reload.

```csharp
namespace Tertius.Assets;

public sealed record AssetStoreOptions
{
    /// <summary>Development builds: compare each cooked key with its sources and warn about stale assets (AS8002).</summary>
    public bool CheckFreshness { get; init; }
}

/// <summary>
/// Loads cooked assets and creates their renderer resources. Implements the resolver of content formats.
/// It loads each sub-asset on its first request and returns the same id or ShapeDesc object for every later request.
/// Thread-safe. Without a renderer it reads and resolves everything and returns None ids, for tests and tools.
/// </summary>
public sealed class AssetStore : IAssetResolver, IDisposable
{
    public AssetStore(IRenderer? renderer, AssetStoreOptions? options = null);

    /// <summary>The current shape of each (piece, use). A reload replaces the entry. The host passes each one to IPhysicsBackend.Prepare.</summary>
    public IReadOnlyCollection<ShapeDesc> Shapes { get; }

    /// <summary>GPU bytes by kind, load times and counts.</summary>
    public AssetStoreStats Stats { get; }

    /// <summary>
    /// Reads the cooked output of these assets again and replaces their resources in place. Ids stay the same.
    /// Call it on the thread that owns the renderer, between two rendered frames, because it calls IRenderer.Replace*.
    /// </summary>
    public AssetReload Reload(IReadOnlyList<ContentId> assets);

    /// <summary>The asset tables and the id tables agree. Tests and the debug switch call it.</summary>
    public void AssertInvariants();
}

public sealed record AssetReload(
    IReadOnlyList<ContentId> Replaced,
    bool CollisionChanged,   // shapes changed: worlds must be rebuilt to use them
    bool StructureChanged,   // pieces, materials or nodes were added or removed: run ContentLoader.ReloadData
    IReadOnlyList<ContentDiagnostic> Diagnostics);
```

The content formats design shows the interface `IAssetResolver`, with `TextureSlot` and `CollisionUse`. These rules belong to this design:

- `Texture` takes the slot, so an image whose role does not fit the slot fails at load with AS4005 (see "Roles"). A `normalDx` image fits the slot `Normal`, because the importer flips its green channel. An example: a normal map named `rock.png`, which has the role `albedo`. The hint names the suffix or the `role` key that fixes it.
- `Collision` takes the use, so that one piece can give convex hulls for a body and a triangle mesh for an interior.
- `BeginLoad` gives the store the package folders and their forms. The resolver cannot find `bin/assets/` without them.
- For a built package of content packages and mods, the store reads only `bin/assets/`, because the package has no `assets/` folder.
- A resolver error is a string that starts with its AS code. ContentLoader reports it as CT4008 (content packages and mods), and the message keeps the AS code for agents.
- `Primitive` and `InlineMaterial` keep the behavior of `PrimitiveAssets` from content formats.

A host wires the store like this:

```csharp
var store = new AssetStore(renderer, new AssetStoreOptions { CheckFreshness = isDevelopment });
LoadResult result = ContentLoader.Load(request, store);
var physics = new JoltPhysicsBackend(...);
foreach (ShapeDesc shape in store.Shapes)
    physics.Prepare(shape);
PreparedScene scene = result.Set!.Scene("tertius.demo:start");
var world = new World(new WorldOptions { StartTime = scene.StartTime, Blueprints = result.Set.Blueprints }, physics);
SceneSpawner.Spawn(world, scene);
```

where

- `store` = the asset store for the whole process.
- `renderer` = the `IRenderer` of the host, or null for a world without graphics.
- `isDevelopment` = true for the launcher in development and for the editor.
- `request` = the `ContentRequest` of content packages and mods.
- `physics` = the physics backend of the world. Every new backend gets `Prepare` for every shape of the store.
- `scene` = the prepared start scene of content formats.

### Engine changes

Each change goes in test-first, and the existing tests and snapshots stay green.

The rigging of a mesh is already in the engine: `MeshData.Rigging`, a `MeshRigging(Rig, Skin, Morphs)` of `Tertius.Core/Seams/Rig.cs` ([skinning-and-morphs.md](skinning-and-morphs.md), "The mesh data"). The importer fills it, and the cooked mesh format carries it.

Core, render seam:

```csharp
namespace Tertius;

/// <summary>A renderer model handle: parts and lights drawn with one entity transform. default is None.</summary>
public readonly record struct ModelId { ... }   // like MeshId: the value is stored plus 1

/// <summary>One mesh with one material, placed by an offset from the model origin.</summary>
public readonly record struct ModelPart(MeshId Mesh, MaterialId Material, Pose Offset);

/// <summary>A point light of a model, placed by an offset from the model origin. Units as PointLightData.</summary>
public readonly record struct PointModelLight(Pose Offset, Vector3 Color, float Power, float Radius, bool CastsShadow);

/// <summary>A spot light of a model. Units as SpotLightData. It points along -Z of its offset.</summary>
public readonly record struct SpotModelLight(
    Pose Offset, Vector3 Color, float Power, float Radius, bool CastsShadow, float InnerConeAngle, float OuterConeAngle);

/// <summary>A light of a model: a PointModelLight or a SpotModelLight. A custom [Union], as RenderLight is. default holds no light.</summary>
[Union]
public readonly struct ModelLight : IEquatable<ModelLight> { ... }

/// <summary>The sphere bounds every part, in model space.</summary>
public sealed record ModelDesc(
    ImmutableArray<ModelPart> Parts, ImmutableArray<ModelLight> Lights, Vector3 BoundsCenter, float BoundsRadius);

/// <summary>A coarser index list over the vertices of the same mesh. Error is the largest deviation in meters, in mesh space.</summary>
public sealed record MeshLod(uint[] Indices, float Error);

public sealed record MeshData(Vertex[] Vertices, uint[] Indices)
{
    /// <summary>Coarser levels, coarsest last. Each one indexes the vertices of this mesh.</summary>
    public ImmutableArray<MeshLod> Lods { get; init; } = [];
}

public enum TextureFormat : byte { Rgba8Srgb, Rgba8Unorm, Bc4Unorm, Bc5Unorm, Bc7Srgb, Bc7Unorm }

/// <summary>A texture with every mip level given. Level 0 is the largest. Rows go from top to bottom.</summary>
public sealed record TextureMips(int Width, int Height, TextureFormat Format, ImmutableArray<ReadOnlyMemory<byte>> Levels);

// MaterialDesc gains a last parameter: TextureId EmissiveMap = default. Emissive radiance = Emissive × EmissiveMap.rgb.
// RenderInstance gains: public ModelId Model. With a model, Mesh and Material are None, and the renderer draws the parts.

public interface IRenderer : IDisposable
{
    // ... the members of today ...
    bool SupportsBlockCompression { get; }
    TextureId CreateTexture(TextureMips texture);
    ModelId CreateModel(ModelDesc model);
    void ReplaceMesh(MeshId mesh, MeshData data);
    void ReplaceTexture(TextureId texture, TextureMips data);
    void ReplaceMaterial(MaterialId material, MaterialDesc data);
    void ReplaceModel(ModelId model, ModelDesc data);
}
```

Core, physics seam:

```csharp
public interface IPhysicsBackend : IDisposable
{
    // ... the members of today ...
    /// <summary>Builds and caches the native shape now, outside a tick. Later bodies with this ShapeDesc object reuse it.</summary>
    void Prepare(ShapeDesc shape);
}
```

Simulation:

- `RenderData` gains `public ModelId Model`. The snapshot builder copies it into `RenderInstance.Model`.
- `World.Spawn` rejects a render row with a model and a mesh or material, with an ordinary argument guard. The binder of content formats reports the same mistake in a file as CT3011.
- The content packages and mods design lists `ModelId` in the mod API as a render value, next to `MeshId`. Its constructor is `[EngineOnly]`, like the other id constructors.

Rendering:

| Change | Tests | Cost to measure |
|---|---|---|
| Upload of `TextureMips` in the six formats, with the given mips and no blits. `SupportsBlockCompression` reads `textureCompressionBC`. | CPU test of the format table. Snapshot of a BC7 checkerboard and a BC5 normal map. | Upload time of a 2048² BC7 texture |
| Normal maps: Z from X and Y | Every existing snapshot stays the same. | None |
| `EmissiveMap`: one more bindless index in the material buffer and one sample | Snapshot of an emissive texture | One texture sample |
| Models: the batcher expands a model instance into one instance per part, with the part offset, and adds the model lights. | CPU tests of the expansion and of previous transforms. Snapshot of a model with two materials and a light. | Batch time for 1,000 models of 4 parts |
| Bounds of a model: the sphere center offset, for culling and impostor fitting | CPU test: a model with its origin at one end fits its impostor to its geometry | None |
| One impostor tile per model instance: the capture draws every part | CPU test: a model of 4 parts uses one tile | Capture time |
| LOD selection: the coarsest LOD with a projected error under `RendererOptions.LodPixelError` (default 1 pixel). Shadow cascades use the texel size of the cascade. `RendererOptions.Lods` turns it off. | CPU tests of the selection math. Snapshot pairs with LODs on and off. | Triangles drawn and GPU time, with and without LODs |
| `Replace*`: same id, new data. A replaced texture frees its old image after the GPU frames in flight finish, which are the frames that the GPU still draws. A replaced mesh takes a new range in the global buffers. | CPU tests through the upload queue. Headless test: a replaced texture shows new pixels. | None |

The projected error of a LOD is error × scale × (output height / (2 × tan(fov / 2))) / distance, in pixels. The deterministic test mode of renderer snapshot tests picks LODs the same way, so snapshots stay stable.

A replaced mesh leaves its old range in the global buffers until the process ends. Hot reload is a development feature, so the stats report this memory and nothing frees it.

Physics:

```csharp
namespace Tertius.Physics;

/// <summary>Jolt work for tools. Every native call runs under the Gate of its backend.</summary>
public static class ShapeCooker
{
    /// <summary>The points of the hull that Jolt builds from these points: at most 256, in the space of the input.</summary>
    public static Vector3[] ReduceConvexHull(ReadOnlySpan<Vector3> points);

    /// <summary>Builds the shape once. Returns null, or the reason that Jolt rejects the shape.</summary>
    public static string? TryBuild(ShapeDesc shape);
}
```

`JoltPhysicsBackend.Prepare` builds the shape into the existing `ShapeCache` and the radius cache, under the backend's `Gate`.

### Interfaces in content formats and in content packages and mods

These two designs hold every interface, file key, value form, name rule and layout entry that this design needs. Examples are `IAssetResolver`, `render.model`, the node pose, `emissiveMap`, the import file names, `bin/assets/`, `FindPackages` and the order of resolver calls. One layout entry is new: the source cache `bin/sources/` (see "Fetched sources"). The content packages and mods design must add it to the package layout and to the folders that a built package leaves out.

### Hot reload

The editor and the launcher in development use `AssetWatcher` from `Tertius.Import`:

1. A `FileSystemWatcher` on the `assets/` folder of each package reports changes. Renames count, because Blender saves through a temporary file and a rename.
2. After 300 ms with no new change, the watcher collects the changed files.
3. It finds the affected assets: a changed source, its sidecar, a dependency that the last import recorded, or a `defaults.import.json` above it. A change of any file inside a model folder `X.model/` affects the scripted model `X.model`.
4. It imports them on a background thread, with the same importer as the CLI.
5. It raises `Imported` with the report on that thread. The owner of the store queues the report.
6. The owner calls `AssetStore.Reload` on the thread that owns the renderer, between two rendered frames. The editor does this on its main thread at the start of a UI frame (see the editor design). Meshes, textures, materials and models keep their ids, and the next rendered frame shows the new data.

| Change | Editor | Launcher in development |
|---|---|---|
| Geometry, textures, materials, lights | Live, with the same ids | Live, with the same ids |
| Collision | `CollisionChanged`: the editor rebuilds its edit world, and the next play world gets the new shapes (see the editor design). | Logged. A restart applies it. |
| New or removed pieces, materials or nodes | `StructureChanged`: the editor runs `ContentLoader.ReloadData` and rebuilds its worlds. | Logged. A restart applies it. |

A failed import keeps the last good cooked folder, so the running game keeps working. A removed asset keeps its resources until the process ends.

Fetched sources: a change of a sidecar, such as a new fetch record, affects its asset like any sidecar change. The import that the watcher starts downloads the missing fetched files as every import does (see "Fetched sources"), on its background thread. Offline, it reports AS9005 and keeps the last good output. The watcher does not watch `bin/sources/`, because nobody edits the source cache. A `.blend` source and a scripted model need the assets shell. In the default shell, the watcher reports AS6001 for a changed `.blend` file or model folder and keeps the last good output.

Scripted models:

- Each save in a model folder starts the full build of the model, with the bake on the CPU, as every import does. A large model takes minutes: the Nostromo takes about 15 minutes on this Mac, nearly all of it the bake. A save during a running build marks the model again, and the watcher builds it once more after the running build ends. An author who tries out a change uses the bridge, which shows a change in seconds, and lets the watcher bring the result into the game.
- The watcher does not watch `tools/blender/`, which lies outside every `assets/` folder. An edit of the model library or of the build tool changes the `dependencies` key input of every scripted model, so the next import builds them again.

Budget: from the save of a `.glb` to new pixels in under 2 s, for a model without new textures. A `.blend` source adds the time of Blender, which step 15 measures. A scripted model adds the time of its build.

### Provenance and licenses

Provenance is the record of where an asset came from. The `source` block holds it, in the sidecar of the asset. A folder default file holds no `source` block (AS1009), because the fetch record names the files of one asset.

| Key | Required | Meaning |
|---|---|---|
| `title` | Yes | The title at the source |
| `authors` | Yes | The authors or the organization, as the source names them |
| `license` | Yes | An SPDX license id, or `LicenseRef-NASA-Media` |
| `page` | Yes | The page of the asset |
| `provider` | No | `polyhaven`, `ambientcg`, `kenney`, `quaternius`, `nasa`, `solarsystemscope`, `sketchfab` or `other` |
| `attribution` | Yes, for CC BY licenses | The credit line in TASL form: title, author, source and license, with a note of changes. If `fetch` gets no `--attribution`, it builds this line from `title`, `authors`, `page`, `license` and `changes`. |
| `retrieved` | Yes | The date of the first download, `YYYY-MM-DD` |
| `files` | Yes | The fetch record: path in the source tree to the `url`, `bytes` and `sha256` of each downloaded file. A file from an archive has `archive` and `entry` instead of `url` (see "Fetched sources"). |
| `archives` | For files from an archive | Archive name to the `url`, `bytes` and `sha256` of each downloaded archive |
| `approved` | Yes | The approval of the user: `on`, the date, and `record`, the SHA-256 of `files` and `archives` as the user approved them (see "Fetched sources") |
| `changes` | No | The changes that the pipeline makes to the work, such as the packed map of a texture set |

Every third-party asset is a download, so every `source` block has a fetch record. An asset with no `source` block is original work of the package. Its authors and license are those of the package manifest.

License policy:

| License | Result |
|---|---|
| `CC0-1.0`, `CC-BY-3.0`, `CC-BY-4.0`, `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `Unlicense`, `LicenseRef-NASA-Media` | Allowed |
| `CC-BY-SA-*` | Warning AS7003: a changed asset must keep the same license. |
| Any license with `NC` or `ND` | Error AS7002: no commercial use, or no adaptations |
| An unknown id | Error AS7002 |

The policy lives in `tools/Tertius.Import/LicensePolicy.cs`. The user can change it there, and the tests lock in the list.

`tertius asset credits` prints the attribution list of a package, grouped by license. `--write` writes `CREDITS.md` (content packages and mods). A test fails on a `CREDITS.md` that does not match the records. The CLI reports the same problem as warning AS7006. Git keeps both the sidecars and `CREDITS.md`, so `credits` and AS7006 read only the records: they need no fetched file and no network. A line looks like this:

```
- "Rock Moss Set 01" by Kless Gyzen, from Poly Haven (https://polyhaven.com/a/rock_moss_set_01), CC0-1.0.
```

NASA media also needs the line "Courtesy of NASA" and no use of NASA logos. The CLI adds that line for `LicenseRef-NASA-Media`.

### The CLI

The commands live in `tools/Tertius.Cli` next to the content commands that content packages and mods adds. Every command takes `--json`, and asset ids follow the content packages and mods design. A package id alone means every asset of the package.

| Command | What it does |
|---|---|
| `tertius asset import [ids or packages] [--force] [--rehash] [--lock-wait s] [--no-previews] [--timings]` | Imports changed assets. `--force` ignores the cache key. `--rehash` ignores the stamp file. |
| `tertius asset reimport <ids>` | The same as `import --force` for the named assets |
| `tertius asset validate [ids or packages]` | Reads every source, configuration and provenance record, computes keys, and reports errors and stale assets. Writes nothing and downloads nothing. A check that needs the bytes of a fetched file, such as AS1004, runs only when the file is in the source cache. |
| `tertius asset inspect <id>` | Prints pieces, parts, materials, textures, LODs, collision, nodes, lights, sizes and provenance |
| `tertius asset preview <id> [--view v]` | Renders preview images and prints their paths |
| `tertius asset list [packages] [--status s]` | Lists assets with kind, status (`upToDate`, `stale`, `missing`, `failed`) and GPU bytes |
| `tertius asset clean [packages] [--all]` | Deletes cooked folders and previews whose source is gone, and files and archives in `bin/sources/` that no fetch record names. `--all` deletes all cooked folders and previews. It keeps the source cache, because its files cost a download. |
| `tertius asset credits [packages] [--write]` | Prints or writes the attribution list |
| `tertius asset fetch <provider>:<id> [--resolution r] [provenance options] [--approved]` | Prints a download plan. With `--approved`, downloads into a new folder and records the approval. |
| `tertius asset add <path> --to <package>:<path> (--from-fetch <folder> \| --own)` | Records a fetched download in a package, or copies an own file into it. Writes the sidecar, imports the asset and renders previews. |
| `tertius asset watch [packages]` | Imports on every change and prints one JSON line per import |

The exit codes are the codes of every `tertius` subcommand, in the table of the content packages and mods design. Two of them matter most here. Code 5 means that a tool is missing, such as Blender outside the assets shell. Code 7 means that another import held the lock of the package for longer than the wait.

An import:

```
$ tertius asset import tertius.demo --json
```

```json
{
  "diagnostics": [
    {
      "code": "AS3005",
      "severity": "warning",
      "package": "tertius.demo",
      "file": "content/tertius.demo/assets/props/fence.glb",
      "pointer": "/materials/2",
      "line": null,
      "column": null,
      "message": "Material 'Wire' uses the extension KHR_materials_sheen, which the engine does not support. It is ignored.",
      "hint": "Remove the sheen from the material in the source file, or accept the material without it."
    }
  ],
  "assets": [
    {
      "id": "tertius.demo:props/fence.glb",
      "status": "imported",
      "staleReason": "source",
      "key": "sha256:9f2c7e01…",
      "seconds": 2.84,
      "pieces": 1,
      "parts": 2,
      "triangles": 5840,
      "textures": 3,
      "gpuBytes": 7340032,
      "previews": ["content/tertius.demo/bin/previews/props/fence.glb/views.png"]
    },
    { "id": "tertius.demo:ships/corvette.blend", "status": "upToDate", "key": "sha256:5d0c41a8…" }
  ],
  "summary": { "imported": 1, "upToDate": 1, "failed": 0, "errors": 0, "warnings": 1, "seconds": 2.91 }
}
```

The diagnostic keys are those of the content formats design. For a `.glb`, `pointer` is a JSON Pointer into the JSON chunk, and `line` and `column` are null. For a `.gltf`, the importer maps the pointer to a line and column with `JsonPositions.Find` of content formats.

An inspection:

```
$ tertius asset inspect tertius.demo:ships/corvette.blend --json
```

```json
{
  "id": "tertius.demo:ships/corvette.blend",
  "kind": "model",
  "status": "upToDate",
  "provenance": { "own": false, "title": "Corvette", "authors": ["…"], "license": "CC-BY-4.0", "page": "…", "from": "sidecar", "fetchedFiles": 1, "cachedFiles": 1 },
  "pieces": [
    {
      "name": "Hull",
      "size": [24.2, 9.8, 61.3],
      "parts": [
        { "material": "HullPaint", "triangles": 41210, "lods": [{ "triangles": 20604, "error": 0.0041 }, { "triangles": 10302, "error": 0.0112 }] },
        { "material": "Glass", "triangles": 410, "lods": [] }
      ],
      "lights": [{ "kind": "point", "node": "Lamp", "power": 233.7, "candela": 12704 }],
      "collision": {
        "moving": { "kind": "compound", "children": 12, "points": 704, "from": "markers" },
        "static": { "kind": "mesh", "triangles": 8360, "from": "markers" }
      }
    },
    { "name": "AirlockDoor", "size": [2.1, 2.4, 0.2], "parts": [{ "material": "DoorMetal", "triangles": 620, "lods": [] }] }
  ],
  "materials": [
    { "name": "HullPaint", "metallic": 0, "roughness": 0.3, "reflectance": 0.5, "baseColorMap": "2048x2048 bc7Srgb", "normalMap": "2048x2048 bc5Unorm" }
  ],
  "nodes": [
    { "name": "AirlockDoor", "piece": "AirlockDoor", "pose": { "position": [0, 1.1, 16] }, "relativeTo": "Hull" },
    { "name": "AirlockInner", "pose": { "position": [0, 1.1, 12] }, "relativeTo": "Hull" },
    { "name": "NozzleL", "pose": { "position": [-3, 0, 30] }, "relativeTo": "Hull" }
  ],
  "gpuBytes": 31457280
}
```

`relativeTo` names the piece of the node pose, so an agent can paste the pose or use the `{"node": ...}` form. `fetchedFiles` counts the files of the fetch record, and `cachedFiles` counts those of them that are in the source cache.

### Previews

`tertius asset preview` renders PNG images with the headless renderer of host and scripted runs, in the deterministic mode of renderer snapshot tests. They go to `bin/previews/<asset path>/`. An agent opens them with the Read tool. Each image except the thumbnail is 1024 × 1024 pixels and has text labels from a small bitmap font.

| View | Contents |
|---|---|
| `views.png` | Four views of the whole file: front, right, three-quarter from above, and top. A file with several pieces also gets one `views-<piece>.png` per piece. A floor grid with 1 m lines, a 1.8 m figure next to the model, and an arrow that marks the front (-Z). Labels: piece name, size in meters, triangle count. |
| `collision.png` | The model in pale gray, with the moving collision in orange and the static collision in teal, both transparent. Labels: shape kinds, hull count and point count. |
| `lods.png` | LOD 0 to the last LOD side by side at one distance. Labels: triangles and error in millimeters. |
| `textures.png` | Each texture after BC compression, decoded again: base color, alpha, normal, metallic, roughness and emissive as separate tiles. Labels: role, size and format. |
| `materials.png` | One sphere per material, with the textures of the material. Labels: material names. |
| `thumbnail.png` | 256 × 256 pixels, with no labels: the three-quarter view of a model, or the sphere of a texture. The asset browser of the editor shows it. |

A texture asset gets `textures.png` and a sphere with the texture. The light rig is fixed: a star entity from the front, above and to the left, of the Sun's power 1 AU away, and the default hemisphere ambient light. The camera fits the bounding sphere with a vertical field of view of 35°. The 8 × 8 font comes from a public-domain source such as `dhepper/font8x8`, and step 16 makes sure of its license first.

### Fetching and intake

`fetch` and `add` turn a download into a package asset. Git then keeps the sidecar with the fetch record, and the downloaded files go into the source cache `bin/sources/` (see "Fetched sources"). Downloaded files are untrusted data, also in the source cache:

- Every download of `fetch` goes into a new empty folder, `.tertius/downloads/<provider>-<id>-<date>/`. Git ignores `.tertius/`.
- The folder also gets `fetch.json`, with the plan, the provenance, the URL, size and SHA-256 of each file, and the approval: the date of `--approved`. `add --from-fetch` reads it.
- The CLI and the importer never run a downloaded file.
- Archive extraction rejects absolute paths, `..` segments, links, and more than 2 GiB or 10,000 entries (AS9003).
- A download over 2 GiB needs `--max-bytes` (AS9004).
- Blender runs with `--disable-autoexec`, so a script inside a `.blend` file never runs. The Blender manual lists this as the default, and the importer states it on purpose.

| Provider | API | License | Default |
|---|---|---|---|
| `polyhaven` | `https://api.polyhaven.com`: `/info/<id>` and `/files/<id>`, with a size and MD5 per file | CC0-1.0 | Models as glTF at 2k. Textures as albedo, `nor_gl` and `arm` at 2k. |
| `ambientcg` | `https://ambientcg.com/api/v2/full_json?id=<id>&include=downloadData` | CC0-1.0 | The `2K-JPG` zip |
| `url` | Any HTTPS URL. A HEAD request gives the size. | From `--license` | Needs `--license`, `--author`, `--title` and `--page`. `--attribution` is optional. |

- Poly Haven asks for a unique User-Agent. The CLI sends `Tertius-AssetCli/<version>` and no personal data.
- The Poly Haven terms forbid scraping, so the CLI fetches only the ids that a command names.
- An MD5 from a provider is compared with the downloaded bytes (AS9002).
- Every download URL must serve the file without a login, because the importer downloads it again on other machines. Sketchfab needs the user to log in, so a Sketchfab file is a file without a URL (see "Fetched sources").

A fetch without `--approved` prints a plan and downloads nothing:

```
$ tertius asset fetch polyhaven:rock_moss_set_01 --json
```

```json
{
  "plan": {
    "provider": "polyhaven",
    "id": "rock_moss_set_01",
    "title": "Rock Moss Set 01",
    "authors": ["Kless Gyzen"],
    "license": "CC0-1.0",
    "page": "https://polyhaven.com/a/rock_moss_set_01",
    "files": [
      { "name": "rock_moss_set_01_2k.gltf", "bytes": 9987, "md5": "c45cebb55c848a11233126f691557ca0",
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/gltf/2k/rock_moss_set_01/rock_moss_set_01_2k.gltf" },
      { "name": "rock_moss_set_01.bin", "bytes": 1466380,
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/gltf/8k/rock_moss_set_01/rock_moss_set_01.bin" },
      { "name": "textures/rock_moss_set_01_diff_2k.jpg", "bytes": 649415,
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_diff_2k.jpg" },
      { "name": "textures/rock_moss_set_01_nor_gl_2k.jpg", "bytes": 840957,
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_nor_gl_2k.jpg" },
      { "name": "textures/rock_moss_set_01_rough_2k.jpg", "bytes": 293827,
        "url": "https://dl.polyhaven.org/file/ph-assets/Models/jpg/2k/rock_moss_set_01/rock_moss_set_01_rough_2k.jpg" }
    ],
    "totalBytes": 3260566
  },
  "approvalNeeded": "Download 5 files (3.3 MB) of 'Rock Moss Set 01' by Kless Gyzen (CC0-1.0) from polyhaven.com? This also lets the importer download the same bytes again on other machines.",
  "command": "tertius asset fetch polyhaven:rock_moss_set_01 --approved --json"
}
```

`add` options:

| Option | Meaning |
|---|---|
| `--to <package>:<path>` | The target. For a model, the path without the extension: `tertius.demo:props/rock-moss-set-01` gives the asset `props/rock-moss-set-01.gltf` and its sidecar `assets/props/rock-moss-set-01.gltf.import.json`. |
| `--kind model\|texture-set\|image` | Optional. Without it, `add` guesses the kind from the files. |
| `--from-fetch <folder>` | A third-party asset. Takes the provenance, the fetch record and the approval from the `fetch.json` of a fetch. |
| `--own` | The asset is original work of the package. No `source` block. |
| `--configure <json>` | Writes these configuration keys into the new sidecar |

`add` needs exactly one of `--from-fetch` and `--own`. A third-party file always comes through `fetch`, because its fetch record needs the URL of each file. So `add` has no provenance options of its own: `fetch` takes them.

`add` makes names follow the rules of content packages and mods: lowercase, `-` for spaces, and no dots except the extension. The extension of the asset is the extension of its main file. For a `.gltf` with separate files, only the `.gltf` gets the new name, because the `.gltf` names its `.bin` and images by their paths. With `--from-fetch`, `add` writes the sidecar with the provenance and the fetch record into `assets/`, copies the downloaded files into the source cache, then imports and renders previews. For a texture set, it also writes the material `.gltf` into `assets/`. With `--own`, it copies the file into `assets/`. It changes no bytes in either case: the importer makes every conversion (see "Source formats"). The output is the output of `import` plus the list of written files.

`add` writes the fetch record and the approval only for files of the approved plan, with the hashes of the approved download. So it never widens an approval. For one file of a pack, the record names that file, the files that it names, and the archive that holds them.

`add` changes a package completely or not at all:

- Before it writes anything, it makes sure that every tool that the input needs exists. If one is missing, it exits with code 5 and writes nothing.
- If a later stage fails, it removes every file that it wrote, in `assets/` and in the source cache, and the package is as before.
- The path of a single file inside a download folder adds only that file and the files that it names. Use this for a pack of many models, such as a Kenney or Quaternius archive.

### Fetched sources

A fetched file is a file of a third-party asset whose bytes git does not keep (decision 26). Every downloaded file is a fetched file. Git keeps its fetch record in the sidecar of its asset, and the importer downloads the file when an import needs it.

| Kind of file | Examples | Where its bytes lie | What git keeps |
|---|---|---|---|
| Own file | Original work of the package, sidecars, folder default files, scripted models, the material `.gltf` of a texture set | `assets/<path>` | The file |
| Fetched file | Every file that came from a download | `bin/sources/<path>`, the source cache | The fetch record in the sidecar of its asset |

A fetched file keeps the path in the source tree that it would have in `assets/`. So asset ids, the paths inside a `.gltf`, the name rules and the rules of "Assets in a package" stay the same. Only the place of the bytes differs. Every reader of a source file opens it through one function, `SourceTree.Open(path)`, which reads an own file from `assets/` and a fetched file from the source cache. `add` writes only text into `assets/`, so no downloaded file and no binary build output goes into git.

The source cache is `bin/sources/` and not a part of `bin/assets/`, for two reasons. The importer replaces a cooked folder as a whole at each import, and the source cache must outlive imports. A built package carries `bin/assets/`, and a player needs no source file. So a built package leaves out `bin/sources/`, as it leaves out `bin/previews/`.

#### The fetch record

The `files` and `archives` keys of the `source` block (see "Provenance and licenses") are the fetch record. A file that a provider sends as one download has a `url`. A file from an archive, such as the zip of an ambientCG texture set, names its archive and its entry:

```json
"source": {
  "title": "Painted Metal 001",
  "authors": ["ambientCG"],
  "provider": "ambientcg",
  "page": "https://ambientcg.com/view?id=PaintedMetal001",
  "license": "CC0-1.0",
  "retrieved": "2026-10-26",
  "archives": {
    "PaintedMetal001_2K-JPG.zip": { "url": "https://ambientcg.com/…/PaintedMetal001_2K-JPG.zip", "bytes": 21733120, "sha256": "sha256:6e02b1…" }
  },
  "files": {
    "painted-metal/painted-metal_albedo.jpg": { "archive": "PaintedMetal001_2K-JPG.zip", "entry": "PaintedMetal001_2K-JPG_Color.jpg", "bytes": 4810211, "sha256": "sha256:a7c3d0…" },
    "painted-metal/painted-metal_normal.jpg": { "archive": "PaintedMetal001_2K-JPG.zip", "entry": "PaintedMetal001_2K-JPG_NormalGL.jpg", "bytes": 6130044, "sha256": "sha256:19fe62…" },
    …
  },
  "approved": { "on": "2026-10-26", "record": "sha256:04b9aa…" },
  "changes": ["Roughness and metal packed into one map"]
}
```

The paths in `files` are relative to the folder of the sidecar. Each file has its own `sha256`, also a file from an archive, so the importer checks each file in the source cache alone. Only `add` writes `files`, `archives` and `approved`. A person may edit the other keys of the `source` block.

#### How the importer fetches

An import that needs the bytes of an asset (step 3 of "An import of an asset" in "Caching and determinism") makes sure that its fetched files are in the source cache:

1. It checks the approval. The SHA-256 of the canonical JSON of `{"archives": …, "files": …}` must equal `approved.record`. Otherwise it reports AS9007 and downloads nothing.
2. For each fetched file, it looks in the source cache. A file whose hash equals its pin is ready. The stamp file gives the hash, as for any source. A file whose hash differs counts as missing, and the importer deletes it.
3. It downloads each missing file from its `url` into `bin/sources/<path>.tmp-<random>`. For a file from an archive, it downloads the archive once into `bin/sources/.archives/`, named by the hex digits of its SHA-256, checks the archive against its pin, and takes out the entry with the archive rules of "Fetching and intake" (AS9003). It stops reading after `bytes` + 1 bytes, so a file that grew cannot fill the disk.
4. It compares the size and the SHA-256 with the pin. On a match, it renames the file into place. On a mismatch, it deletes the file, reports AS9006, and keeps the last good cooked folder.
5. A request that fails gives AS9005 with exit code 2. The importer keeps the last good cooked folder.

Then the import goes on as for any source. The downloads run under the lock of the package (see "Cooked output"). The importer sends the User-Agent of `fetch`, and it requests only the URLs of fetch records. It keeps a downloaded archive, so the assets of one pack download it once. `clean` deletes an archive that no fetch record names.

#### The approval

Decision 20 says that every download needs the approval of the user. The user gives it once, when the user approves the plan of `fetch`. `fetch --approved` downloads the files and writes the date into `fetch.json`. `add` copies it into `approved`, with the SHA-256 of the fetch record, which holds the SHA-256 of every file.

A later download of the same pinned bytes, by any import on any machine, reuses that approval. The importer asks nobody, also in CI. The reason: the user approved these files, these sizes, this license and these URLs. The pins let only exactly the approved bytes into the source cache, so a later download brings nothing that the user did not approve. It also sends requests only to the URLs that the user approved.

A new or changed file needs a new approval. This holds for a new file, a new URL, a new hash and a new archive. `approved.record` stops a record that changed after the approval (AS9007). The way to a new version is a new `fetch`, a new approval in chat and a new `add`.

#### Offline, and a lost URL

- A machine whose cooked output is up to date needs no network, also with an empty source cache. `validate`, `list`, `inspect` and `credits` never download.
- The first import on a new machine, a fresh clone or CI needs the network. Offline, it gives AS9005, and the asset keeps its last good cooked folder, or none. The user can also copy `bin/sources/` from another machine. The importer checks every copied file against its pin, so a copy cannot bring wrong bytes.
- A URL that no longer serves the file gives AS9005. A URL that serves other bytes gives AS9006. The source cache of another machine is then the only copy of the approved bytes. To use the new bytes, the user approves a new fetch.

#### Files without a URL

A fetched file needs a URL that serves exactly its bytes without a login, because the importer downloads it again on every new machine. A file without such a URL cannot be a source. There are two cases:

- A file that only a login gives, such as a Sketchfab download. The user downloads it.
- A third-party file that a person changed, for example in an image editor. No URL serves the changed bytes.

For such a file, the user picks one of these:

1. Put the file at an HTTPS URL that serves it without a login, on a host that the user chooses. The license must allow a copy, and every license of the allowed list does. Then the agent runs `tertius asset fetch url:<that URL> --page <the page of the asset> --author <name> --title <title> --license <spdx> --json`. The record then names the true page and authors, and the user approves the plan as for any download. The URL goes into git, so everyone with the repository can download the file.
2. Leave the asset out, or pick another asset from a provider of the runbook.

A change that the import configuration can make, such as the scale, the front or a texture role, needs no new file. It goes into the sidecar.

#### Readers and writers

| File | Writers | Readers |
|---|---|---|
| `source` block of a sidecar | `add --from-fetch`, from `fetch.json`. People edit only the keys outside the fetch record. | The importer (the key, the downloads, AS1002, AS1008, AS9007), `validate`, `inspect`, `credits` |
| `.tertius/downloads/<folder>/fetch.json` | `fetch --approved` | `add --from-fetch` |
| `.tertius/downloads/<folder>/` other files | `fetch --approved` | `add --from-fetch`, which copies them into the source cache |
| `bin/sources/<path>` | `add --from-fetch` (a copy), the importer (a download) | The importer through `SourceTree.Open`, and Blender in a conversion |
| `bin/sources/.archives/<sha256>` | The importer | The importer. `clean` deletes the ones that no record names. |
| `CREDITS.md` | `credits --write` | People, and the check of AS7006 |

### Runbook: pull in an asset

An agent that the user tells to "pull in X" follows these steps.

1. Find out what X is: a model, a texture set, or a single image such as a planet map. If X is audio, an HDR sky or an animated character, stop. Tell the user that the engine cannot use it yet.
2. Search the providers in this order: Poly Haven, ambientCG, Kenney, Quaternius, NASA 3D Resources, Solar System Scope. These give CC0 or permissive licenses. Use Sketchfab only through the user, as "Files without a URL" in "Fetched sources" says.
3. Search with the web tools, because the CLI never searches. Take the id from the URL of the asset page. For example, `https://polyhaven.com/a/rock_moss_set_01` gives `polyhaven:rock_moss_set_01`.
4. Read the license on the asset page. If it is not in the allowed list, tell the user and stop.
5. For Poly Haven or ambientCG, run `tertius asset fetch <provider>:<id> --json`. For another source, find the direct URL and run `tertius asset fetch url:<url> --license <spdx> --author <name> --title <title> --page <page> --json`. The URL must serve the file without a login. When a provider offers several formats, pick glTF, because a glTF needs no Blender on any machine that imports it.
6. Ask the user in chat. Name the files, the source, the license and the total size. Say that this approval also covers later downloads of the same files by the importer on other machines. Wait for a clear yes.
7. Run the same command with `--approved`. If it fails with exit code 2, report the error and stop.
8. Pick a path by kind: `props/`, `ships/`, `materials/`, `planets/` or another folder that the package already uses.
9. Run `tertius asset add <input> --to <package>:<path> --from-fetch <download folder> --json`. For one asset, the input is the download folder. For a pack of many models, the input is the one file that the user wants. `add` writes the sidecar into `assets/` and copies the files into `bin/sources/`, not into `assets/`.
10. If `add` exits with code 5, it wrote nothing and needs the assets shell. Find out whether Blender is in the Nix store with `test -e "$(nix eval --raw --inputs-from . nixpkgs#blender.outPath)"`. If the test fails, ask the user first, and name the download of about 620 MiB. Then run `add` again inside `nix develop .#assets`.
11. If a command exits with code 7, wait one minute and run it again. The editor or another agent holds the lock.
12. Read every diagnostic, and act on each family as the table below says. After a change to the sidecar, run `tertius asset reimport <id> --json`.
13. Open `views.png`, `collision.png` and `textures.png` with the Read tool. Make sure that the size matches the 1.8 m figure. Make sure that the front arrow points at the front of the model. Compare the materials with the asset page. Write down what you see.
14. If the user wants it used, write or change a blueprint with `render.model` and an asset shape. Run `tertius content validate --json`.
15. Run `tertius asset credits <package> --write`.
16. Report to the user: the asset ids, pieces and materials, and the license and attribution line. Also report the triangle count, the GPU bytes, every remaining warning, the preview paths and what the previews show. Do not commit unless the user asks. A commit holds the sidecar, `CREDITS.md` and, for a texture set, the material `.gltf`. The downloaded files stay in `bin/sources/`, which git ignores.

| Family | What it covers | What to do |
|---|---|---|
| AS1xxx | Files and configuration | Correct the file name or the key that the hint names. Then import again. |
| AS2xxx | Model content | If the hint names a configuration key, such as `scale`, `front` or `collision.moving`, set it in the sidecar. For AS2015, a heavy asset, fetch a lower resolution, or ask the user before you raise the budget. For an error that no key fixes, stop and report, because the source file needs a change in Blender. |
| AS3xxx | Materials | For AS3006, set `tertius_strand` on the material in the model script. Report the warnings that remain. |
| AS4xxx | Images | For AS4005, set `role` in the sidecar. For AS4001 and AS4002, stop and report. |
| AS5xxx | Collision | Set a `collision` key. For AS5003, stop and report the reason from Jolt. |
| AS6xxx | Tools | AS6001 and AS6004 come with exit code 5: see step 10. AS6006 comes with exit code 7: see step 11. For AS6002 and AS6005, stop and report the log lines. |
| AS7xxx | Provenance | Correct the record with the data of the asset page. For AS7002, stop and tell the user about the license. |
| AS8xxx | Cooked output | Run `tertius asset import <id> --json`. |
| AS9xxx | Downloads | Stop and report. Never try another URL that a downloaded file suggests. For AS9005, report the URL: the user decides whether to wait for the network or to copy `bin/sources/` from another machine. For AS9006 and AS9007, a new version needs a new fetch and a new approval (step 5). |

### Diagnostics

| Code | Severity | Meaning |
|---|---|---|
| AS1001 | Error | A file in `assets/` is not a source asset, a sidecar, a folder default file or a known dependency. |
| AS1002 | Error | A sidecar has no source file. |
| AS1003 | Error | A `.bin` or `.mtl` file has no file that uses it. |
| AS1004 | Error | A `.gltf` names a missing file or a file outside the source tree of its package. |
| AS1005 | Error | A section of a sidecar does not apply to the kind of its asset. Folder default files never get it. |
| AS1006 | Error | A scripted model folder holds a file that `model.json` does not name, or a folder. |
| AS1007 | Error | `model.json` does not bind to its schema, or it names a missing script. |
| AS1008 | Error | A fetched file is also in `assets/`. Git keeps only its fetch record. The hint says to delete the copy in `assets/`. |
| AS1009 | Error | A folder default file holds a `source` block. Provenance lives in the sidecar of each asset. |
| AS2001 | Error | The glTF cannot be read. The message holds the reason from SharpGLTF. |
| AS2002 | Error | The glTF requires an unsupported extension. Draco and meshopt compression are not errors: they go through the Blender round trip (see "Source formats"). |
| AS2003 | Warning | An optional extension is ignored. |
| AS2004 | Warning | A primitive with points or lines is skipped. |
| AS2005 | Info | Animations are ignored. (Changed on 2026-10-26: skins are no longer ignored.) |
| AS2006 | None | Retired on 2026-10-26: morph targets are no longer ignored. The code is not used again. |
| AS2007 | Warning | Vertex colors are ignored. |
| AS2008 | Warning | Two nodes have the same name. A later one gets a `~n` suffix. |
| AS2009 | Error | The reference names a node that is not a piece. The hint names the piece that holds it. |
| AS2010 | Error | Unknown fragment, or no fragment in a file with several pieces or materials. The message lists the names. |
| AS2011 | Error | A mesh reference names a piece with more than one material. Use `render.model`. |
| AS2012 | Warning | Shear from a non-uniform scale is baked into the vertices. |
| AS2013 | Warning | The size is outside `expectedSize`. |
| AS2014 | Warning | A hand-made LOD node is ignored. |
| AS2015 | Warning | The file is heavier than `budget.triangles` or `budget.gpuMegabytes`. |
| AS2016 | Info | A camera is ignored. |
| AS2017 | Warning | A second UV set is ignored. |
| AS2018 | Error | A mesh or model reference names a piece without geometry. |
| AS2019 | Warning | A directional light is ignored. |
| AS2020 | Error | A joint or an inverse bind has a scale or a shear after the bake. Joints are rigid. |
| AS2021 | Info | A vertex has more than four joint influences. The four largest are kept. |
| AS2022 | Error | A vertex's skin weights sum to 0. |
| AS2023 | Warning | Two meshes of a piece give one morph different default weights. The first in glTF order wins. |
| AS2024 | Error | A piece's rig has more than 65,535 joints. |
| AS3002 | Info | An occlusion texture is dropped. |
| AS3004 | Error | The textures of one material use different texture transforms. |
| AS3005 | Warning | An unsupported material extension is ignored. |
| AS3006 | Error | A hair material (`tertius_shading` `"hair"`) has no `tertius_strand`. |
| AS4001 | Error | The image cannot be decoded, or its format is not supported. |
| AS4002 | Error | The image is larger than 16384 pixels on a side. |
| AS4003 | Info | The image is resized to a power of two. |
| AS4004 | Info | Mip levels above `maxSize` are dropped. |
| AS4005 | Error | The role of the texture does not fit the slot that uses it. |
| AS5002 | Error | A triangle mesh is requested for a moving body. |
| AS5003 | Error | Jolt rejects the shape. The message holds the reason. |
| AS5004 | Warning | A decomposition gives more than 32 hulls or takes more than 30 s. |
| AS5005 | Error | A collision marker has no mesh to fit. |
| AS5006 | Warning | A collision marker at the root names no piece. |
| AS5007 | Error | Collision is requested, and the configuration is `none`. |
| AS6001 | Error | Blender is needed, but `TERTIUS_BLENDER` is not set. Exit code 5. |
| AS6002 | Error | Blender failed. The message holds its exit code and last log lines. |
| AS6003 | Warning | Blender reported a warning, such as a missing texture file. |
| AS6004 | Error | A native library does not load. Exit code 5. |
| AS6005 | Error | A tool did not finish in time. |
| AS6006 | Error | Another import held the lock of the package for longer than the wait. Exit code 7. |
| AS6007 | Error | The volume of the cooked folder has less free space than the build of a scripted model needs. The message names both sizes. |
| AS7001 | Error | A `source` block lacks a required key, such as its fetch record. |
| AS7002 | Error | The license is not allowed or unknown. |
| AS7003 | Warning | A share-alike license |
| AS7004 | Error | A hand-written record with a CC BY license has no `attribution`. `fetch` and `add` always write one. |
| AS7006 | Warning | `CREDITS.md` does not match the provenance records. |
| AS8001 | Error | The cooked output is missing. The hint is `tertius asset import <id>`. |
| AS8002 | Warning | The cooked output is stale. Only with `CheckFreshness`. |
| AS8003 | Error | The cooked output is damaged or has an unknown format version. |
| AS8004 | Error | The GPU does not support BC textures. |
| AS9001 | Error | A network request failed. Exit code 2. |
| AS9002 | Error | A download does not match the hash of the provider. |
| AS9003 | Error | An archive entry is unsafe, or the archive is too large. |
| AS9004 | Error | The download is larger than `--max-bytes`. |
| AS9005 | Error | A fetched file is not in the source cache, and its download failed. The message names the file and its URL. Exit code 2. The last good cooked folder stays. |
| AS9006 | Error | A fetched file does not match the size or the SHA-256 that its fetch record pins. The importer keeps no byte of it, and the last good cooked folder stays. |
| AS9007 | Error | A fetch record has no approval, or it changed after the user approved it. The importer downloads nothing. |

### Native tools and libraries

Every version below comes from the pinned nixpkgs (`nix eval`) or from NuGet, as the research recorded it.

| Tool or library | Version | License | From | Used for |
|---|---|---|---|---|
| SharpGLTF.Core | 1.0.7 | MIT | NuGet | Reading glTF |
| SharpGLTF.Toolkit | 1.0.7 | MIT | NuGet, tests only | Writing fixtures |
| StbImageSharp | 2.30.16 | Unlicense or MIT | NuGet (already in the repository) | PNG, JPEG, TGA, HDR |
| StbImageWriteSharp | 1.16.7 | Unlicense or MIT | NuGet (already in the repository) | Preview PNG files |
| TinyEXR.NET | 1.2.0 | MIT | NuGet | EXR |
| BCnEncoder.Net | 2.3.0 | MIT or Unlicense | NuGet | BC4, BC5 and BC7 encoding, and decoding for previews |
| meshoptimizer | 1.2 | MIT | nixpkgs `meshoptimizer`, library output | Welding, tangents, optimization, LODs |
| V-HACD | 4.1.0 | BSD-3-Clause | `native/vhacd.nix` | Convex decomposition |
| libjoltc_double | Jolt 5.6.0 | MIT | `native/joltc.nix` (exists) | Hull reduction, shape tests |
| ktx-tools | 4.4.2 | Apache-2.0 | nixpkgs `ktx-tools` | `ktx validate` in tests |
| `check-jsonschema` | 0.38.0 | Apache-2.0 | nixpkgs `check-jsonschema` (content formats) | Schema conformance of import files. Import files are plain JSON, so it reads them as they are. |
| Blender | 5.2.2 | GPL-2.0-or-later | nixpkgs `blender`, `assets` shell only | FBX, OBJ, `.blend`, TIFF and WebP conversion at import, the round trip of compressed glTF, and the build of scripted models |
| `blender-mcp` | 1.0.3 | GPL-3.0-or-later | The official tag, built in `flake.nix`, `assets` shell only. Its add-on `mcp-1.0.3.zip` comes from a fixed-output fetch. | The MCP server and the Blender add-on through which agents model (see "Scripted models") |
| MPFB | 2.0.18 | GPL-3.0-or-later for the code, CC0-1.0 for the base mesh, the targets and the rigs | `native/mpfb.nix`, a fixed-output fetch from the Blender extensions platform, `assets` shell only | The people of scripted models (see "Humans from MPFB") |
| MakeHuman system assets | The pack of 2024-04-14 | CC0-1.0 | `native/makehuman-assets.nix`, a fixed-output fetch without the clothes, `assets` shell only | The skins, eyes, eyebrows, eyelashes, teeth, tongue and hair of those people |

Running Blender or MPFB as a tool does not put its license on the output files (research belief, the usual reading of the GPL). The meshoptimizer README asks for a credit line. The importer prints it in `tertius asset --version`, and the game does not ship meshoptimizer.

Flake changes. The `common` attributes and the `assets` shell exist since ter-q218, and the importer adds its tools to them:

```nix
# In flake.nix: shared attributes for both shells.
common = {
  packages = [ /* ...the packages of today... */ pkgs.ktx-tools pkgs.check-jsonschema ];
  # ...the variables of today...
  TERTIUS_MESHOPT_DIR = "${pkgs.lib.getLib pkgs.meshoptimizer}/lib";
  TERTIUS_MESHOPT_VERSION = pkgs.meshoptimizer.version;
  TERTIUS_VHACD_DIR = "${vhacd}/lib";
};
devShells.default = pkgs.mkShell common;
devShells.assets = pkgs.mkShell (common // {
  packages = common.packages ++ [ pkgs.blender blenderMcp ];   # blenderMcp: native/blender-mcp.nix
  TERTIUS_BLENDER = pkgs.lib.getExe pkgs.blender;
  TERTIUS_BLENDER_VERSION = pkgs.blender.version;
  TERTIUS_MPFB = "${mpfb}";                                   # native/mpfb.nix: MPFB 2.0.18, unpacked
  TERTIUS_MAKEHUMAN_ASSETS = "${makehumanAssets}";            # native/makehuman-assets.nix: body-part assets, no clothes
  BLENDER_SYSTEM_EXTENSIONS = "${blenderExtensions}";         # native/blender-extensions.nix: MCP add-on and MPFB
  BLENDER_PATH = pkgs.lib.getExe pkgs.blender;                # for the tools of blender-mcp that start Blender
  BLENDER_MCP_HOST = "localhost";                             # the bridge, for the MCP server and bridge.sh
  BLENDER_MCP_PORT = "9876";
});
```

- `blender-mcp` 1.0.3 needs the Python package `mcp` below version 2. The pinned nixpkgs has `mcp` 1.29.0, so the package builds from the nixpkgs Python set with no override. `native/blender-mcp.nix` asserts the version, so a nixpkgs update to `mcp` 2 stops the build with a message, not the server at run time. The earlier Nostromo project met the same limit by pinning an older nixpkgs.
- The Blender of nixpkgs has no Python module `requests`. Blender's extension add-on prints a traceback at each start because of it. Enabling and running the MCP add-on and MPFB works all the same. The nixpkgs function `blender.withPackages` adds Python modules, but its build fails on macOS.

- `meshoptimizer` has the outputs `bin`, `dev` and `out`, and `bin` is the default output. So the library path uses `lib.getLib`.
- `native/vhacd.nix` pins V-HACD by tag and hash, like `native/joltc.nix`. It compiles `native/vhacd/tertius_vhacd.cpp`, which includes `VHACD.h` with `ENABLE_VHACD_IMPLEMENTATION` and exports a C API:

```c
typedef struct { uint32_t maxHulls, resolution, maxVerticesPerHull; double errorPercent; } tvhacd_params;
typedef struct tvhacd_result tvhacd_result;
int      tvhacd_decompose(const float* positions, uint32_t vertexCount, const uint32_t* indices, uint32_t triangleCount,
                          const tvhacd_params* params, tvhacd_result** result);   /* 0 on success */
uint32_t tvhacd_hull_count(const tvhacd_result* result);
uint32_t tvhacd_hull_points(const tvhacd_result* result, uint32_t hull, float* xyz, uint32_t capacity);
void     tvhacd_free(tvhacd_result* result);
```

- `Directory.Build.targets` copies the meshoptimizer and V-HACD libraries into the output of every project that sets `<UsesAssetTools>true</UsesAssetTools>`, with `CopyToOutputDirectory="Always"`. Nix store files have the date 1970, so `PreserveNewest` never replaces an old copy (project memory).

The Blender command:

```
$TERTIUS_BLENDER --background --factory-startup --disable-autoexec --python-exit-code 3 --threads 1 \
  --python tools/Tertius.Import/Blender/tertius_convert.py -- <input> <output.glb> <options.json>
```

- The working folder is a new empty temporary folder, and every path is an argument.
- `--threads 1` makes the tangents of the export the same on every run (see "Determinism" in "Scripted models").
- The script starts from an empty scene and imports by extension: `bpy.ops.import_scene.fbx`, `bpy.ops.wm.obj_import` (forward -Z, up Y), `bpy.ops.import_scene.gltf` for a compressed glTF, or `bpy.ops.wm.open_mainfile`. For a TIFF or WebP image, it loads the image and writes its pixels as a PNG file instead of a `.glb`. An export after `open_mainfile` works in headless Blender 5.2.2 (tested on 2026-10-26).
- It exports with `export.write_glb` of the model library in `tools/blender/`, which holds the export options (see "Scripted models"). It leaves the camera check of `write_glb` off: a third-party file often holds a camera. The exporter leaves cameras out in any case. The script prints `TERTIUS-CAMERA <name>` for each camera, and the importer reports AS2016 for it, as rule 7 of "Models: nodes and pieces" says.
- It prints `TERTIUS-DEP <path>` for every external file that Blender loaded and `TERTIUS-WARN <text>` for problems. The importer records the dependencies and turns the warnings into AS6003.
- The time limit is the model configuration key `blenderTimeout` (AS6005).

### What is verified and what is believed

Facts read in primary sources during this planning work:

- The glTF specification: +Y up, the front of an asset at +Z, and a negative determinant reverses the winding.
- The Blender light conversion of 683 lumens per watt (`blender/exp/lights.py` and `blender/com/conversion.py` of glTF-Blender-IO).
- KHR_lights_punctual units (candela and lux) and the unitless KHR_materials_emissive_strength.
- The engine shading in `lighting.glsl` and `material.glsl` (see [pbr-brdf.md](pbr-brdf.md)):
  - diffuse = base color × (1 − metallic) / π
  - F0 = 0.16 × reflectance² for a non-metal, and the base color for a metal
  - α = roughness², with a GGX lobe
  - emissive is added radiance
- The joltc hull and center-of-mass functions in the pinned `joltc.h`.
- The meshoptimizer 1.2 functions, flags and the experimental macro (`meshoptimizer.h` at tag v1.2).
- `ImageInfo.FromStream`, `ImageResultFloat` in StbImageSharp, and `EncodeToRawBytes` per mip level in BCnEncoder.Net.
- The nixpkgs versions in the table above, and the outputs of `meshoptimizer`.

Beliefs, which the steps test first:

- Headless Blender runs from the dev shell on macOS (step 1).
- V-HACD with `m_asyncACD` off gives the same output on every run (step 12).
- BCnEncoder.Net gives the same bytes on one thread and on many, and encodes a 2048² BC7 texture in a few seconds (step 7).
- The accessor reads of SharpGLTF apply sparse data and dequantize KHR_mesh_quantization (step 8).
- `meshopt_generateTangents` with `meshopt_TangentCompatible` matches the tangents of Blender within 1° (step 9).
- TinyEXR.NET reads the EXR files that Blender writes (step 6).
- Blender keeps a backup `*.blend1` on each save and saves through a temporary `*.blend@` file (step 15).
- Blender writes the same `.glb` bytes for the same `.blend` file and version, with `--threads 1` (step 15). With more threads, the tangents differ in their last bits (tested on 2026-10-26). If it does not, the cooked key still holds, but the golden test of the `.blend` fixture compares the cooked output only.

### Performance budgets

These are targets, not measurements. Step 20 replaces them with numbers.

| Work | Target | How it is measured |
|---|---|---|
| Find that 1,000 unchanged assets are up to date | Under 0.5 s with a warm stamp file | `--timings` of the CLI |
| Import a 50,000-triangle model without textures | Under 1 s | BenchmarkDotNet on a fixture |
| Encode one 2048² texture to BC7 with mips | Under 3 s on all cores | BenchmarkDotNet. Above 10 s, a native BC7 encoder (bc7e) becomes a later step. |
| Import a model with four 2048² textures | Under 10 s | `--timings` |
| Convert an FBX file through Blender | Under 10 s, Blender start included | `--timings` |
| Decompose a 50,000-triangle mesh | Under 10 s | BenchmarkDotNet |
| Load one cooked model of 50,000 triangles and four 2048² textures, CPU work | Under 20 ms | `AssetStore.Stats` |
| Prepare 32 hulls of 64 points | Under 10 ms | Physics benchmark. The research measured 0.17 ms per hull. |
| Hot reload of a `.glb` without textures, from save to pixels | Under 2 s | An editor test with timestamps |
| One preview image | Under 2 s | `--timings` |

Memory:

| Data | Cost |
|---|---|
| Geometry | 48 bytes per vertex and 4 bytes per index. LODs add at most 87.5% of the LOD 0 indices: 0.5 + 0.25 + 0.125 with the default ratio and count. |
| BC7 and BC5 textures | 1 byte per pixel, times 4/3 for mips |
| BC4 textures | 0.5 bytes per pixel, times 4/3 |
| Models | One small record per model in the renderer. Parts expand only in the batch lists of each frame. |

`--timings` adds the time of each stage to the JSON of each asset: read, convert, meshes, materials, textures, collision, write and previews. The renderer measures LOD selection and model expansion with its GPU timestamps and `RenderStats`.

### ARCHITECTURE.md changes

1. Toolchain: the libraries and tools of "Native tools and libraries", and the `assets` dev shell.
2. Projects: `src/Tertius.Assets` depends on Core, Simulation and Content. `tools/Tertius.Import` is a tool, and it can reference every module.
3. Rendering: every renderer change of "Engine changes". These are models with parts and lights, one impostor tile per model and LOD selection by projected error. They also include BC textures with given mips, the emissive map, normal Z from X and Y, and the `Replace` methods.
4. Physics: `Prepare`, and asset shapes as points and triangles that Jolt builds at load.
5. A new section, "Assets". It records the formats, the axes, the front at -Z, the light unit of 683 cd, and pieces, parts and fragments. It also records cooked output, the cache key, the ordering rules and provenance. It records fetched sources: the source tree, the fetch record, the source cache `bin/sources/` and the rule that one approval covers the pinned bytes on every machine. It records scripted models and the build tool. It states that every machine that imports a source that Blender converts or a scripted model needs the assets shell, and that the first import of a fetched source needs the network.
6. Rules for references: `AssetStore` holds no mod types, so it does not block the unload that content packages and mods defines.

### Hand-offs to the editor

- `Importer`, `AssetWatcher` and the `Imported` event for the asset browser and its error list. The event comes on a background thread.
- `AssetStore.Reload`, which runs on the thread of the renderer, and its flags `CollisionChanged` and `StructureChanged`. They tell the editor to rebuild worlds or to run `ReloadData`.
- `AssetStore.Shapes` and `Prepare` for every new physics backend.
- `thumbnail.png` of each asset for the asset browser, and the JSON of `inspect` as the data of an asset panel.
- `add` as the code behind a drop of a file onto the editor. A dropped file is own work (`--own`). A third-party file comes in through `fetch` (see "Fetched sources").

## Steps

Each step writes its tests first and sees them fail for the right reason. The engine builds and every test passes after each step.

Ticket ter-q218 comes before these steps. It adds the scripted model format, the assets shell, the Blender MCP server, the model library, the build tool and the Nostromo. It builds no part of the importer.

1. Add the tools to the flake: `ktx-tools`, the meshoptimizer variables and `native/vhacd.nix`. The `assets` shell with Blender and `blender-mcp` exists already (ter-q218, see "Scripted models"), and this step adds the importer's variables to it. Add `.tertius/`, `dist/`, `*.blend[0-9]*` and `*.blend@` to the root `.gitignore`. Tests first: smoke tests that load each native library and call one function. The Blender smoke test skips with a reason outside the `assets` shell. Done: `nix develop .#assets --command bash -c 'blender -b --factory-startup --python-exit-code 1 --python-expr "import bpy; print(bpy.app.version_string)"'` prints 5.2.2.
2. Create the projects and the fixtures. Write `FixtureGenerator` with SharpGLTF.Toolkit, and make the glTF and image fixtures of "Testing". Make the Blender fixtures with a script in the `assets` shell. Add them to the repository with a `Fixtures/README.md` that lists how each was made. Done: a test reads every fixture.
3. Make the Core and Physics changes. Add the value types `ModelId`, `ModelPart`, `ModelLight` with its cases `PointModelLight` and `SpotModelLight`, `ModelDesc`, `MeshLod`, `TextureFormat` and `TextureMips`. Add `MaterialDesc.EmissiveMap`, `IPhysicsBackend.Prepare` with its Jolt implementation, and `ShapeCooker`. This step adds no `IRenderer` member. Tests: `ReduceConvexHull` keeps at most 256 points, all on the input hull. After `Prepare`, a spawn builds no shape at the sync point, which a count of shape builds shows.
4. Write the KTX2 writer and reader and the cooked format. Tests: round trips for every `TextureFormat`, `ktx validate` on every written file, a `cooked.json` round trip, offsets in `geometry.bin`, and AS8003 for damaged files.
5. Make the renderer and Simulation changes from the table in "Engine changes", one row at a time. Each new `IRenderer` member comes in the same row as its Vulkan implementation, and every fake renderer of the tests gets it too. Each row writes its CPU tests and snapshot scenes first. Done: every existing snapshot is unchanged, and each new reference image is approved after a real review.
6. Write image decoding and texture processing. Tests:
   - The mip of a black and white checkerboard is sRGB 188, not 128.
   - Alpha-weighted mips keep the color at transparent edges.
   - Normal mips have unit length.
   - Toksvig widens roughness on a bumpy normal map and keeps it on a flat one.
   - Resizing to a power of two, `maxSize`, and the green flip of `normalDx`.
   - An image header that claims 100,000 × 100,000 pixels gives AS4002 before decoding.
   - EXR, HDR and 16-bit PNG input.
7. Add BC encoding. Test a floor per format on the peak signal-to-noise ratio (PSNR), the size of the compression error in decibels. Test the same bytes on one thread and on all threads. Add a benchmark of a 2048² BC7 texture.
8. Read glTF into the import scene. Tests:
   - The front turn, `scale` and `front`.
   - Name dedupe, pieces and every way to mark one, and node poses.
   - A negative determinant and the shear warning.
   - Required and optional extensions, sparse and quantized accessors, strips and fans, and points and lines.
   - Texture transforms and UV sets.
9. Write mesh processing. Tests:
   - Welding, and tangents compared with Blender tangents on a fixture.
   - Double-sided duplication, and optimization that keeps the same triangle set.
   - LODs with an error under the limit, the `minReduction` rule, and bounds.
10. Write the material conversion. Tests:
    - One test per formula line.
    - The default dielectric gives reflectance 0.5.
    - Metallic, roughness and the base color pass through, and the reflectance follows clamp(√(Y(F0d) / 0.16), 0, 1) for IOR and specular factors.
    - Toksvig in the mips: n = 2 / α² − 2 and back gives the same roughness for |Na| = 1.
    - Metals, unlit, every alpha mode, packing into textures and texture sharing.
    - A texture set with separate roughness and metal images in `extras`, and `tertius_normalDx`.
11. Write the light conversion. Tests: candela to watts for point and spot lights, spot cones and ignored directional lights. Measure the GPU cost of the lights of the interior fixture.
12. Write collision. Tests:
    - Each marker kind, root markers and the defaults for both uses.
    - A static use of a piece with `UTM_` markers leaves out its convex markers.
    - Jolt reduction, AS5002 and the simplification error of static meshes.
    - V-HACD run twice with equal hashes.
13. Write the configuration and the cache. Tests:
    - Sidecars bind through the binder of content formats.
    - The precedence of the four sources.
    - Each key input, changed alone, makes the asset stale with the right reason.
    - A provenance change makes no asset stale.
    - The key of a fetched source uses its pin: with an empty source cache and an up-to-date cooked folder, an import sends no request.
    - The stamp file, `--rehash`, atomic writes, and the lock with its wait and exit code 7.
    - A store path in `tools`, changed alone, makes the asset stale with the reason `tools`.
    - The model and texture sections in a folder default file, and AS1005 only for sidecars.
    - Determinism: two imports give the same bytes, and so do serial and parallel imports and a reversed asset order.
    - The schema of import files passes `check-jsonschema` against good and bad fixtures.
14. Add `IAssetResolver.Model`, and bind `render.model`, `ModelId` fields and `emissiveMap` in the binder of content formats. Then write `AssetStore`, the implementation of the resolver. Tests:
    - Every resolver member, the use rules, and CT4008 with the AS code.
    - Every row of the role and slot table, also `Ui`, `Data` and `Any` for component fields.
    - A built package with no `assets/` folder resolves the same ids as its source form.
    - Lazy loading, and one `ShapeDesc` object per piece and use.
    - `CheckFreshness` and `AssertInvariants`.

    Done: a fixture package with a model, asset collision and a door at a node pose loads, spawns, and runs one tick.
15. Add the Blender path. Test a scripted model fixture with each exit code of the build tool. Other tests: FBX, OBJ and `.blend` fixtures, the conversion of TIFF, WebP and Draco fixtures at import, recorded dependencies, the time limit, AS6001 outside the `assets` shell, and a `.blend` fixture with an auto-run script. Save a fixture twice from a Blender script, and make sure that its `*.blend1` file gives no diagnostic. That script writes a marker file, and the test makes sure that the file does not exist after the import. Measure the time from a save to new pixels.
16. Write the CLI commands, the previews and the thumbnails. Test the command classes directly, and run the process once per command as a smoke test. Test the JSON shapes and every exit code. Test AS2015 with a small budget. Approve the preview images of the fixtures after a review.
17. Write provenance, licenses, credits, `add`, `fetch` and fetched sources. The tests use recorded API responses and a fake `HttpMessageHandler` that serves fixture bytes, and never the network. Test the archive rules, the plan without `--approved`, the User-Agent, every license rule and `CREDITS.md`. Test the attribution line that `fetch` builds for a CC BY download. Test that `add` writes nothing for a missing tool, and that it removes its files after a failed import, also from the source cache. Fetched sources:
    - An import with an empty source cache downloads each file of an approved record, asks nothing, and the second import downloads nothing.
    - A file from an archive downloads the archive once for two assets.
    - A served file with other bytes, or more bytes than its pin, gives AS9006 and never reaches the source cache.
    - A failed request gives AS9005 with exit code 2, and the last good cooked folder stays.
    - A record that changed after its approval, or that has none, gives AS9007 and sends no request.
    - A damaged file in the source cache is deleted and downloaded again. A copied source cache with right bytes needs no download.
    - AS1002 for a fetched source, AS1008, AS1009, and `add` without one of `--from-fetch` and `--own`.
    - `add` writes an approval only for files of the approved plan.
18. Write hot reload. Tests:
    - The debounce, and the affected assets of a changed folder default file.
    - Replacement with the same ids in the headless renderer, and the two flags of `AssetReload`.
    - `Imported` comes on the background thread, and `Reload` runs on the thread of the renderer.
    - A failed import keeps the last good output.
19. Build the material comparison scene: the `material-grid` fixture rendered with the preview rig. Render the same file in Blender with a fixed script in the `assets` shell, and put both images side by side. Review highlight sizes, brightness and colors, and describe the differences. Metals differ by design. Approve the engine image as a snapshot.
20. Write the benchmarks for the budgets and report the numbers. Move the decisions into ARCHITECTURE.md, and mark this plan done.

## Testing

Fixtures in `tests/Tertius.Import.Tests/Fixtures/`. Each one is small and tests one thing:

| Fixture | Tests |
|---|---|
| `cube-textured.gltf` | The basic path: one piece, one material, three textures |
| `pieces.glb` | Root pieces, a `PIECE_` door, a custom-property piece, empties for node poses |
| `collision-markers.glb` | Every marker kind, root markers and nested markers |
| `material-grid.gltf` | Roughness 0 to 1 across, metallic 0 to 1 down, plus IOR and specular rows |
| `mirrored.glb` | A negative scale |
| `shear.glb` | A non-uniform scale with a rotated child |
| `sparse.gltf`, `quantized.glb`, `strips-fans.gltf` | Accessor and topology forms |
| `lights.glb` | Point, spot and directional lights |
| `texture-transform.gltf`, `two-uv-sets.gltf` | UV baking |
| `double-sided.glb`, `alpha-modes.glb` | Material modes |
| `blender-tangents.glb` | Tangents that Blender wrote |
| `interior.glb` | A room of about 10,000 triangles with 8 lights, for collision and the cost of the lights |
| `door.fbx`, `crate.obj` with `crate.mtl`, `ship.blend`, `autoexec.blend`, `draco.glb`, `checker.tif`, `checker.webp` | The Blender path, and the conversions at import |
| `texture-set.gltf` with separate roughness, metal and DirectX normal images | The packing of a texture set at import |
| `crate.model/` | A small scripted model fixture for the importer |
| `checker-srgb.png`, `normal-bumpy.png`, `normal-dx.png`, `alpha-edge.png`, `npot-300x200.png`, `deep-16bit.png`, `sky.hdr`, `normal.exr`, `huge-header.png` | Texture processing |
| `Packages/assets-demo/` | A package for the store, the resolver and the CLI. It has a `ship.blend1` backup file. It also has a sidecar with a fetch record, whose URLs the fake `HttpMessageHandler` serves from fixture files. |

Most fixtures come from `FixtureGenerator`, so their contents are known exactly. The Blender fixtures come from a Blender script. No third-party asset is in the fixtures, so the tests need no licenses. The fetch records of the fixtures name own fixture files, which the fake handler serves, so no test downloads anything.

Other tests:

- Golden outputs: the cooked folders of a few fixtures are in `Golden/`. A test compares every byte. `TERTIUS_APPROVE_GOLDEN=1` writes them again on purpose, for example after a meshoptimizer update. The agent that approves them describes the changed files and the reason in its report.
- Round trips:
  - KTX2 write and read, and cooked write and read.
  - The glTF of a fixture to `MeshData`, against the expected vertices within float tolerance.
  - Import files: bind, write with `ContentFormat` from content formats, and bind again.
- Determinism: the three tests of "Caching and determinism".
- Conformance: `ktx validate` on every KTX2 file, and `check-jsonschema` on import files.
- Snapshots: the renderer changes, the previews and the material grid, as the renderer snapshot tests design describes.
- Security: the auto-run `.blend`, archive entries with `..` and links, the false image header, and a served file that is larger than its pin.
- Contracts, for the state that must stay coherent:
  - `CookedAsset.AssertInvariants`: every part names a mesh and a material in range. LOD index lists are in range, and their errors increase. Every node parent comes before its children.
  - `AssetStore.AssertInvariants`: each loaded sub-asset has one id, the id tables and the asset tables agree, and `Shapes` holds exactly the shapes that the store made.
- Lock-in tests for the decisions:
  - The front turn and the light unit of 683.
  - Reflectance 0.5 for the default dielectric, Toksvig on by default, and MASK maps to `CutoutAlpha(alphaCutoff)`.
  - Provenance outside the key, and the configuration precedence.
  - The static collision of a ship piece holds no solid hulls.
  - The license list, and no download from `fetch` without `--approved`.
  - An approved fetch record downloads on a new machine without a question, and a changed record downloads nothing.
  - Bytes that do not match their pin never reach the source cache.
  - The key of a fetched source needs no download.
  - A lock that another import holds gives exit code 7, never 3.
  - Blender backup files cause no diagnostic.

## Decided by the user

The user accepted every recommendation of this section on 2026-10-10.

1. Metals look right under the BRDF: the base color of a metal is its F0 (changed on 2026-10-24, see decision 13). Reflections of the surroundings get a renderer plan of their own. When ships and stations need them, that plan comes next.
2. Binary source files in `content/*/assets/` use Git LFS from the start, because a later move to LFS rewrites the history. The other options were plain git with size limits, and fetching third-party files again from their provenance records. (Replaced on 2026-10-26 by decision 21: the remote has no Git LFS, and no binary build output goes into git. Then decision 26 took the third option: third-party files are fetched from their provenance records.)

## Out of scope and later

- Skeletal animation: clips, blending and state machines. Skinning and morph targets exist ([skinning-and-morphs.md](skinning-and-morphs.md)), and a component writes an entity's rig pose by hand. A later animation epic adds a clip format and becomes one more writer of the rig pose. glTF animations stay ignored until then (AS2005).
- Animated textures and node animation from glTF.
- Vertex colors, a second UV set and ambient occlusion maps. Each needs a renderer change.
- Hand-made LODs, meshlets and quantized vertices.
- Cooked Jolt shapes. A small C++ shim in `native/joltc.nix` can wrap `Shape::SaveWithChildren` and `Shape::sRestoreWithChildren`. The cooked data must then carry the Jolt version, because the format changes between versions. Measure load times first.
- CoACD as a second decomposition method. It needs pinned sources for zlib, boost, openvdb, spdlog and eigen.
- Zstd supercompression of KTX2 files, and a native BC7 encoder such as bc7e.
- A budget for GPU uploads per frame, and the release of GPU resources at a content unload.
- Material overrides on a model in a render section, such as a red variant of a ship.
- Attachment prefabs that spawn an entity for each piece. They need the scene instancing of content formats.
- Sky maps and imported environment maps. The renderer captures its environment map from the scene ([environment-map.md](environment-map.md)), so it has no slot for an imported one.
- Audio assets.
- Cooked output in archives for shipping, and mod distribution.
- The Sketchfab download API, which needs the login of the user.
- A host of the project for third-party files that no URL serves without a login. Until one exists, the user puts such a file at a URL of the user's choice (see "Fetched sources").
- The same cooked bytes on different platforms.
