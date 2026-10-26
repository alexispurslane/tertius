# Procedural modeling: reference

The detail behind [SKILL.md](SKILL.md). The design in `docs/plans/asset-pipeline.md`, "Scripted models", is binding. This file repeats only what an author needs at hand.

## model.json

```json
{
  "format": 1,
  "scripts": ["s00_scene.py", "s10_hull.py", "s90_checks.py"],
  "seed": 1979,
  "parameters": { "seeds": { "greebles": 1979, "windows": 7 } },
  "bake": { "objects": ["Hull_Fore"], "size": 4096, "samples": 8, "margin": 16 },
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
| `format` | Required | 1 |
| `scripts` | Required | The model scripts in the order of the build |
| `seed` | Required | An integer. `model.rng` of each script is seeded with `<seed>/<script file name>`. |
| `parameters` | `{}` | Free JSON values. A script reads them as `model.params`. |
| `bake.objects` | `[]` | The mesh objects to bake, in order. Empty: no bake. |
| `bake.size` | 1024 | The side of each map in pixels: a power of two from 64 to 8192 |
| `bake.samples` | 8 | Cycles samples per pixel of the bake |
| `bake.margin` | 16 | Pixels that the bake extends each UV island |
| `views.resolution` | `[1920, 1080]` | The size of each view |
| `views.samples` | 128 | Cycles samples per pixel of each view |
| `views.shots` | `[]` | `name` gives `views/<name>.png`. `camera` and `target` are positions in m. `lens` is in mm on a 36 mm sensor. `sun` is the direction toward the key light. All in Blender axes. |

A model script gets one name, `model`:

| Member | Meaning |
|---|---|
| `model.folder` | The absolute path of the model folder |
| `model.seed` | The `seed` of `model.json` |
| `model.params` | The `parameters` of `model.json` |
| `model.rng` | A `random.Random` seeded with `<seed>/<script file name>` |

A script reads the rest of `model.json` with `run.load_model(model.folder)`, for example the bake objects or the shots.

## The model library

`tools/blender/tertius_blender/`. Model scripts import from it.

| Module | What it gives |
|---|---|
| `run` | `new_scene()`, `run_script(folder, script)`, `load_model(folder)` |
| `geometry` | `MB`, a mesh builder that collects boxes, tapered blocks, cylinders, lathes, tubes and spheres in one BMesh and ends as one object (`finish`). `coll`, `frame_matrix`, `align_z`, `beam`, `rod`, `constant_material`. |
| `nodes` | `NB`, a builder of shader node trees: math, mixes, map ranges, noise and brick textures |
| `checks` | `attachment(objects, anchors)` and `view_shares(camera)` |
| `stage` | `build(views)`, `use_shot(shot)`, `render(views, folder)`, `remove()`, `camera_name(shot)` |
| `bake`, `export` | Used by the build tool. `export.pieces()` and `export.triangles()` also help a check. |

## The build tool

```bash
tools/blender/build.sh <model folder> --out <folder> [--views] [--device auto|cpu]
```

| Exit code | Meaning |
|---|---|
| 0 | Built |
| 2 | Bad input: an argument, `model.json`, a missing script, a stray file, or an output folder that holds other files |
| 3 | The build failed: a script raised, or the bake or the export failed. `build.json` and `blender.log` say where. |
| 5 | Blender is missing: `nix develop .#assets` failed |
| 6 | Less than 30 GB free on the volume of the output folder |

The output folder gets `model.glb`, `model.blend`, `textures/`, `views/` (only with `--views`), `build.json` and `blender.log`. A folder that holds an earlier `build.json` is replaced. Any other folder with files is refused and kept.

`build.json` holds the `result` of each script. So a script that sets `result = {...}` reports its numbers there: face counts, the scatter count, the check results.

## Without the MCP tools

If the MCP server is not approved in this session, drive the same functions through a headless Blender:

```bash
nix develop .#assets --command bash -c '"$TERTIUS_BLENDER" --background --factory-startup --python-exit-code 3 --python-expr "$(cat <scratch>/look.py)"'
```

`look.py` holds the code of the authoring loop: `run.new_scene()`, the scripts, `stage.build`, `stage.use_shot` and `bpy.ops.render.render(write_still=True)` with `scene.render.filepath` set to a scratch path. Print a marked JSON line for the numbers that you need, and read it from the output. Each call starts a new Blender, so it is slower than the bridge. For the bake and the export, use the build tool.

## Pitfalls

### The bridge and Blender

- The add-on opens its socket only in online mode. `bridge.sh` starts `blender --background --online-mode --command blender_mcp`.
- `execute_blender_code` runs each call in a new namespace. `sys.path` and the scene stay between calls. Deferred completion (`check_is_finished`) is rejected in background mode.
- The render tools write into `blender_mcp/` of the bridge's temporary folder, `~/.local/state/tertius/blender-bridge-9876/tmp/`. `bridge.sh stop` deletes that folder.
- `render_viewport_to_path` renders with the stage settings: 1920 x 1080 at 192 samples for the Nostromo, about 12 to 25 s on Metal. Lower `resolution_percentage` and `scene.cycles.samples` for quick looks. `render_thumbnail_to_path` overrides them by itself.
- `nix develop` sets a new `TMPDIR` on every call. So the bridge keeps its pid, log and temporary folder in `~/.local/state/tertius/`.
- The nixpkgs Blender lacks the Python module `requests`, so each start prints a traceback from the extension add-on. It is harmless.
- Blender 5 materials always have a node tree. `use_nodes` is deprecated.

### Determinism

- A `set` of BMesh edges or of objects iterates in memory-address order. The bevel code of the earlier Nostromo project used one. The addresses differ between Blender processes and over a long bridge session, so the bevel, and so the mesh, can differ between builds. `dict.fromkeys(...)` keeps the order of first use.
- A set of plain integers, such as vertex indices, is safe for membership tests.
- `create_uvsphere`, `spin` and the weld of `remove_doubles` order the elements that they make by memory address. Then the faces, the vertex that the weld keeps, the exported indices, the UV maps and every later scatter change between runs. `MB.finish` sorts the vertices by their coordinates, starts each face at its lowest vertex and sorts the faces by their vertices, all before the weld. The first Nostromo build showed the problem: two builds gave different engines, nav lights and structures. The fix changed the order of the hull faces, so the greeble scatter of the earlier project changed once.
- A GPU bake is not the same bytes twice: a few pixels differ by one level out of 255. One to four tangent values of the Nostromo's `Engines` also differ in their last bits, for a cause not yet found. Compare two builds by their meshes and their views, not by the bytes of the `.glb`.
- `model.rng` per script keeps the random choices of a script stable when another script joins the list. A fixed seed in `parameters` keeps an approved scatter, as the Nostromo does with the seeds of its first build.

### Geometry

- Tapered blocks have sloped tops and sides. A part that sits on one must start at the lowest height of the slope under it, or it floats above part of the slope. The Nostromo computes the slope with small functions such as `nose_bottom_z(y)`.
- Overlapping blocks of one mesh hide faces inside each other. The bake gives those faces texture space, and the maps lose detail. `s15_union.py` merges each hull part with an exact boolean self-union and counts the islands that remain. The count must be 1.
- Greebles on a slope: sink each one into the hull (`SINK = 0.15` m), cast a ray down at each footprint corner and keep the greeble only when all four hit a face that points the same way, and cast a ray up from the sample point to keep clear of the hull above. Keep the footprints on the grid of the hull: no random yaw.
- Moving parts that rest folded, such as landing struts, must reach into their bay. Measure each distance along the face normal from the hull surface.
- `MB.finish` replaces an object of the same name, so a script runs again in the bridge without a duplicate.

### Materials and the bake

- glTF holds no procedural shader. The bake turns base color, roughness and normal into maps. Emission and transparency are not baked.
- Base color and roughness bake as emission of the value that feeds the input. A plain diffuse bake would mix in the light and the metallic part.
- A procedural material that reads object coordinates bakes the same maps after the object turns or moves. A material that reads world coordinates does not.
- Cycles writes large tile buffers into Blender's temporary folder while it bakes big maps. The bake deletes them after each map, and the build tool needs 30 GB free before it starts.
- The glTF exporter computes tangents only for triangles and quads. `export.write_glb` adds a Triangulate modifier for the time of the export.

### Export

- Headless Blender that opens a saved `.blend` file and then exports fails: the exporter reads `context.active_object`, and that context has no such attribute. The earlier Nostromo project met this. The build tool exports in the process that ran the scripts.
- A camera in the scene fails the export, because it means that the stage leaked into the model. Call `stage.remove()` after each look.

### Views

- A dark world hides the faces that point down. The stage adds a weak, cool fill sun from below, on the side of the camera. Without it, the parts under a hull look as if they float free.
- A camera close to the hull sees whatever stands between it and the hull. The Nostromo's close-up looks along the starboard side of the nose and the bridge. From there the bridge hides the deck conduits, and `s90_checks.py` requires 0 % `Pipes` and `Pipe_Brackets` in that shot.

## The Nostromo

The Nostromo is a tug in the style of the ship of the film. An earlier agent built it in its own project through the same MCP server. It moved into Tertius as the first scripted model. Its folder is `content/tertius.demo/assets/ships/nostromo.model/`.

| Script | What it does | What it teaches |
|---|---|---|
| `s00_scene.py` | Scene name, collections, the materials as constant placeholders | Later scripts take materials by name. The comment defines the build frame. |
| `s10_hull.py` | The three hull parts: `Hull_Fore`, `Hull_Mid`, `Hull_Aft` | Massing from tapered blocks. One baked texture set per hull part. |
| `s15_union.py` | An exact self-union of each hull part, and an island count | Hidden faces cost texture space |
| `s20_engines.py` | Engine bells with wall thickness, housings, cooling rings, glow discs | Lathe profiles. A separate glow mesh with an emissive material. |
| `s30_greebles.py` | About 1,240 greebles on the hull, and the frame ribs | Seeded scatter with ray tests. Three meshes by paint: light, gray, metal. |
| `s40_pipes.py` | Pipe runs, conduits and brackets | `MB.tube` with rounded corners. Brackets along each straight run. |
| `s50_structures.py` | Masts, dishes, the sensor array, docking clamps, folded landing struts | Each part overlaps the part it hangs from |
| `s60_windows.py` | Windows, frames, the bridge visor, navigation lights | Parts placed on a slanted face through a face matrix |
| `s70_materials.py` | Procedural hull and engine materials, constant materials for the rest | Panels by box projection, grime, hazard stripes, a Bump node into the normal |
| `s75_uv.py` | UV maps of the bake objects and their texel density | The bake contract needs an active UV map |
| `s80_assemble.py` | The root empty `Nostromo` as the one piece, and the turn to face -Y | One piece for a rigid ship. The front of the pipeline. |
| `s90_checks.py` | The attachment check and the close-up's view check | A check that raises. The stage built and removed inside a check. |

The build frame: scripts `s10` to `s75` build the ship with its nose toward +Y, the frame of the earlier project. `s80_assemble.py` turns every part 180 degrees about +Z through its object transform. The mesh data stays in the build frame, so the materials, which read object coordinates, bake the same maps as before. The shots in `model.json` are in the final axes. A new model starts with the nose toward -Y and needs no turn.

The greebles and the windows use `random.Random` with the seeds 1979 and 7 from `parameters.seeds`, the seeds of the earlier project. The lit portholes are the same as there. The greeble scatter follows the same rules and density, but its places changed once, when `MB.finish` got its fixed order (see "Determinism").

One build on this Mac, with `--views`, takes about 5 minutes: 38 s for the scripts, about 165 s for twelve maps of 4096 x 4096 on Metal, and four views of 1920 x 1080 at 192 samples. The `.glb` is about 134 MB, nearly all of it the twelve PNG maps. The build reports 192,238 triangles, 333 structure islands with 0 free, and no pipe in the close-up.
