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
| `stage` | `build(views, device="auto")`, `use_shot(shot)`, `render(views, folder)`, `remove()`, `camera_name(shot)`. `remove` restores the render settings that `build` changed. |
| `bake`, `export` | Used by the build tool. `export.roots()`, `export.marked()` and `export.triangles()` also help a check. |
| `mpfb` | `human(info, name)`, `pose(rig, degrees)`, `joints(body)`, `freeze(body)`, `bone_weights(body, points, prefixes)`, `ARM_BONES`, `services()`. A person from MPFB (see "A human with MPFB"). |

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
| 6 | Less free space on the volume of the output folder than the build needs: 1 GB, plus 32 bytes per pixel of one map, plus 24 bytes per pixel and bake object (`tools/blender/needed_mb.py`). The Nostromo needs 3 GB. |
| 130 | SIGINT or SIGTERM stopped the build. The build tool stopped Blender and deleted its temporary folder. |

The build runs Blender twice. The first run runs the scripts, bakes, saves `model.blend` and renders the views. The second run opens `model.blend` with one thread and exports `model.glb`. The output folder gets `model.glb`, `model.blend`, `textures/`, `views/` (only with `--views`), `build.json` and `blender.log`. A folder that holds an earlier `build.json` is replaced. Any other folder with files is refused and kept.

`build.json` holds the `result` of each script, the root objects (`roots`) and the objects marked with `tertius_piece = 1` (`marked`). The importer decides the pieces from these and from the `PIECE_` prefix, the sidecar and the collision markers. So a script that sets `result = {...}` reports its numbers there: face counts, the scatter count, the check results.

## Without the MCP tools

If the MCP server is not approved in this session, drive the same functions through a headless Blender:

```bash
nix develop .#assets --command bash -c 'BLENDER_USER_RESOURCES=<scratch>/user "$TERTIUS_BLENDER" --background --factory-startup --python-exit-code 3 --python-expr "$(cat <scratch>/look.py)"'
```

`BLENDER_USER_RESOURCES` gives Blender a user folder in the scratch folder. MPFB writes its logs and settings there, and `mpfb.services()` refuses to run without it, so never leave it out and never point it at your own Blender folder. Delete the scratch folder after the work.

`look.py` holds the code of the authoring loop: `run.new_scene()`, the scripts, `stage.build`, `stage.use_shot` and `bpy.ops.render.render(write_still=True)` with `scene.render.filepath` set to a scratch path. Print a marked JSON line for the numbers that you need, and read it from the output. Each call starts a new Blender, so it is slower than the bridge. For the bake and the export, use the build tool.

## Pitfalls

### The bridge and Blender

- The add-on opens its socket only in online mode. `bridge.sh` starts `blender --background --factory-startup --online-mode --addons bl_ext.system.mcp --command blender_mcp`.
- `execute_blender_code` runs each call in a new namespace. `sys.path` and the scene stay between calls. Deferred completion (`check_is_finished`) is rejected in background mode.
- The render tools write into `blender_mcp/` of the bridge's temporary folder, `~/.local/state/tertius/blender-bridge-9876/tmp/`. `bridge.sh stop` deletes that folder.
- `render_viewport_to_path` renders with the stage settings: 1920 x 1080 at 192 samples for the Nostromo, about 12 to 25 s on Metal. `stage.build` picks the GPU. Without it, the bridge renders on the CPU, because the factory settings of `run.new_scene()` do. Lower `resolution_percentage` and `scene.cycles.samples` for quick looks. `render_thumbnail_to_path` overrides them by itself.
- The MCP server waits at most 300 s for one tool call (`_TIMEOUT` in `blmcp/tools_helpers/connection.py`). A longer call fails, and the bridge works on.
- Python loads `tertius_blender` once per Blender process. So the bridge runs the library of the checkout that started it, whatever `sys.path` says later. `bridge.sh` refuses a start or a stop from another checkout, and `run.run_script` raises an error for a model folder of another checkout.
- `run_script` reloads every module of the library except `run`, which cannot reload itself while it runs. The snippet of the authoring loop calls `importlib.reload(run)` first.
- `bridge.sh` trusts its pid file only when that process is the bridge, so a pid that a reboot gave to another process is safe.
- `nix develop` sets a new `TMPDIR` on every call. So the bridge keeps its pid, log and temporary folder in `~/.local/state/tertius/`.
- The nixpkgs Blender lacks the Python module `requests`, so each start prints a traceback from the extension add-on. It is harmless.
- Blender 5 materials always have a node tree. `use_nodes` is deprecated.
- Now and then a build on Metal aborts at the start of the bake: `blender.log` ends with an `NSInvalidArgumentException` from `MetalKernelPipeline::compile`, and the build tool exits with code 3 and no `build.json` (ticket ter-jnyk). Delete the output folder and build again. A build with `--device cpu` never meets it.

### Determinism

- A `set` of BMesh edges or of objects iterates in memory-address order. The bevel code of the earlier Nostromo project used one. The addresses differ between Blender processes and over a long bridge session, so the bevel, and so the mesh, can differ between builds. `dict.fromkeys(...)` keeps the order of first use.
- A set of plain integers, such as vertex indices, is safe for membership tests.
- `create_uvsphere`, `spin` and the weld of `remove_doubles` order the elements that they make by memory address. Then the faces, the vertex that the weld keeps, the exported indices, the UV maps and every later scatter change between runs. `MB.finish` sorts the vertices by their coordinates, starts each face at its lowest vertex and sorts the faces by their vertices, all before the weld. The first Nostromo build showed the problem: two builds gave different engines, nav lights and structures. The fix changed the order of the hull faces, so the greeble scatter of the earlier project changed once.
- A bake on Metal is not the same bytes twice: a few pixels differ by one level out of 255. A bake on the CPU is. Compare two Metal builds by their meshes and their views, not by the bytes of the `.glb`.
- Blender computes MikkTSpace tangents on several threads, and the order of the sums changes between runs. Thirty calls of `calc_tangents` on the Nostromo's `Engines` gave thirty different results, and with `--threads 1` one result. The exporter rounds tangents to four decimals, so one to four values changed per build. The build tool exports in a second Blender with `--threads 1`.
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
- Cycles writes tile buffers into Blender's temporary folder while it bakes big maps: 344 MB at the peak for a map of 4096 x 4096 on Metal. The bake deletes them after each map.
- Two bake objects with one material would show the maps of the last bake. The bake stops with an error that names both objects.
- The glTF exporter computes tangents only for triangles and quads. `export.write_glb` adds a Triangulate modifier for the time of the export.

### Export

- An export after `bpy.ops.wm.open_mainfile` works in headless Blender 5.2.2, also with no active object (tested on 2026-10-26). The earlier Nostromo project saw it fail on `context.active_object`, in an older Blender.
- In a build, a camera in the scene fails the export, because it means that the stage leaked into the model. Call `stage.remove()` after each look. The converter of third-party files exports without this check.

### Views

- A dark world hides the faces that point down. The stage adds a weak, cool fill sun from below, on the side of the camera. Without it, the parts under a hull look as if they float free.
- A camera close to the hull sees whatever stands between it and the hull. The Nostromo's close-up looks along the starboard side of the nose and the bridge. From there the bridge hides the deck conduits, and `s90_checks.py` requires 0 % `Pipes` and `Pipe_Brackets` in that shot.

## A human with MPFB

### The macro sliders

`parameters.human.phenotype` holds them. Each runs from 0 to 1, and MPFB's default is 0.5. MPFB blends targets between the named points, linearly.

| Slider | 0 | 0.5 | 1 | Note |
|---|---|---|---|---|
| `gender` | Female | Between | Male | |
| `age` | Baby | Young adult | Old | 0.1875 is a child. On MakeHuman's scale, 0, 0.1875, 0.5 and 1 are about 1, 10, 25 and 90 years. |
| `muscle` | Least | Average | Most | |
| `weight` | Thinnest | Average | Heaviest | |
| `height` | Shortest | Average | Tallest | Between 0.49 and 0.51 no height target loads. |
| `proportions` | Uncommon | Neither | Ideal | |
| `cupsize` | Smallest | Average | Largest | The breasts. MPFB does not weigh them by gender, so keep 0.5 for a man. |
| `firmness` | Least | Average | Most | The breasts, as `cupsize` |

`race` holds the shares of `african`, `asian` and `caucasian`. They weigh the race targets and should sum to 1. MPFB's default is 0.33 each.

### The detail targets

`parameters.human.targets` holds the detail targets: the shape of the nose, the ears, the chin, the cheeks, the eyes, the mouth, the head and the parts of the body. It is a list of `{"target": "<name>", "value": <0 to 1>}`. Two people with the same sliders and no detail targets have the same shape.

- The names are the file names without `.target.gz` in the folders of `$TERTIUS_MPFB/data/targets/`, one folder per part, such as `nose`, `ears`, `chin`, `cheek`, `mouth`, `eyes`, `head`, `neck`, `torso`, `arms`, `hands`, `legs` and `asym` (small asymmetries). For example, `ls $TERTIUS_MPFB/data/targets/nose` in the assets shell.
- Most come in pairs, such as `nose-scale-vert-decr` and `nose-scale-vert-incr`, or `l-ear-lobe-decr` and `l-ear-lobe-incr`. Give one side of a pair. `$TERTIUS_MPFB/data/targets/target.json` lists the pairs of each folder.
- Give the exact name. A name that MPFB does not hold stops the build. MPFB alone would skip it without a word, or take the first target whose name starts with it.
- Leave out the folders `macrodetails` (the sliders make those) and `expression`.

### The asset names

The MakeHuman system assets in `$TERTIUS_MAKEHUMAN_ASSETS` hold these. Name each one as `<folder>/<folder>.mhclo`, and a skin as `<folder>/<folder>.mhmat`. MPFB finds an asset by its file name, and the folder chooses between two files of the same name. A name that the pack does not hold stops the build.

| Key | Folders |
|---|---|
| `skin_mhmat` | `young_`, `middleage_` and `old_` with `african_`, `asian_` and `caucasian_`, then `female` or `male`, such as `young_african_female`. Also `young_caucasian_female2` and `young_caucasian_male2`. |
| `eyes` | `high-poly`, `low-poly` |
| `eyebrows` | `eyebrow001` to `eyebrow012` |
| `eyelashes` | `eyelashes01` to `eyelashes04` |
| `teeth` | `teeth_base`, `teeth_shape01` to `teeth_shape05` |
| `tongue` | `tongue01` |
| `hair` | `afro01`, `bob01`, `bob02`, `braid01`, `long01`, `ponytail01`, `short01` to `short04` |
| `rig` | `game_engine` is the tested one. MPFB also has `default`, `default_no_toes`, `game_engine_with_breast`, `mixamo`, `mixamo_unity`, `cmu_mb` and `openpose`. |

Look at a hair before you choose it. `bob02` rendered nearly white, and the fringe of `braid01` covered an eye. `afro01` looks right, but its cards show as straight dark edges up close (ticket ter-omuj). Each hair has one color: to change the color, choose another hair.

The eyes are brown. `alternative_materials` gives them another color: it maps the UUID of the eyes asset to a material of `eyes/materials/`. The UUID of `high-poly` is `2c12f43b-1303-432c-b7ce-d78346baf2e6`, and of `low-poly` `1cc97a30-85a1-42e6-a9f7-b3e753732baa`. The colors are `blue`, `bluegreen`, `brown`, `brownlight`, `deepblue`, `green`, `grey`, `ice` and `lightblue`:

```json
"alternative_materials": {"2c12f43b-1303-432c-b7ce-d78346baf2e6": "materials/green.mhmat"}
```

### The bones of `game_engine`

The rig has 53 bones: `pelvis`, `spine_01` to `spine_03`, `neck_01`, `head`, and per side (`_l`, `_r`) `clavicle`, `upperarm`, `lowerarm`, `hand`, `index_01` to `_03` (also `middle`, `ring`, `pinky` and `thumb`), `thigh`, `calf`, `foot` and `ball`. `mpfb.ARM_BONES` holds the name prefixes of both arms.

The axes of `parameters.pose`, as measured on the left side (`_l`). The right side mirrors the sign of `y` and `z`.

| Bone | Axis | What a positive angle does |
|---|---|---|
| `upperarm_l` | z | Raises the arm. A negative angle lowers it from the A-pose toward the side: -30 brings it close to the body. |
| `upperarm_l` | x | Swings the arm forward |
| `lowerarm_l` | x | Bends the elbow forward |
| `lowerarm_l` | z | Bends the forearm outward, away from the body. A negative angle bends it inward. |
| `index_01_l` ... `pinky_03_l` | x | Curls the finger. 15, 25 and 15 degrees for the three joints give a relaxed hand. |
| `thumb_02_l` | x | Curls the thumb |

### The joints

`mpfb.joints` returns the centers of MPFB's joint helpers. Useful names: `neck`, `head`, `pelvis`, `spine-1` (the chest) to `spine-4`, and per side `l-` or `r-` with `clavicle`, `shoulder`, `elbow`, `hand`, `upper-leg`, `knee`, `ankle` and `foot-1`. Read them before the freeze. They do not move with your later shift, so add the shift yourself, as `s10_human.py` does.

### Garments from the body

The crew member's `s20_jumpsuit.py` shows each method:

- Choose the faces of a garment by bone weight: the summed weight of the bones of the trunk, the arms and the legs, cut at the wrists and ankles by the position along the limb (`along`).
- Smooth the anatomy away before you push the cloth out. Plain Laplacian smoothing of 40 steps, weighted by the trunk bones, flattens the chest. Smoothing along the normals only, or many more steps, grows spikes at the nipples. Leave the neck out of the trunk smoothing, or the neck tube collapses.
- Subdivide once, push the surface out along its normals by a thickness per bone (`suit_thickness`), and smooth again a little.
- Make folds with seeded waves: `rng = np.random.default_rng(model.rng.getrandbits(64))`.
- Make a ring, such as a collar, a cuff or a belt, with rays cast from an axis out to the garment (`ring`). Limit each ray with `reach`, or a ray finds the far side of another limb. Cast level rays from the middle of the neck for a collar, so no ray leaves through the opening.
- Make a pocket or a patch with a grid of rays cast at the garment from outside (`patch`), so its edges are straight.
- Give trims thickness with `finish(..., solidify=...)`, which applies a Solidify modifier through `new_from_object`.
- Make boots from metaballs: a heel ball, a foot ellipsoid, a toe box and a shaft capsule. A garment that follows the foot shows the toes.
- Delete the body faces under the cloth, but keep a few rows of faces under each opening (`keep_rings`), so no gap shows at a cuff or a collar.
- When the arms hang beside the trunk, a ring or a hose around the waist must leave them out. `mpfb.bone_weights(body, points, mpfb.ARM_BONES)` gives the arm weight of the body under each point. Keep the points below 0.5.

### Materials

`mpfb.freeze` gives each part one image texture into one Principled BSDF. glTF holds these as they are, so the parts need no bake. Only the hair, the eyebrows and the eyelashes keep alpha, as `BLEND`. Bake only your garments. A garment material can read the joints, such as a band around the upper arm between two distances from the shoulder joint.

### Pitfalls

- MPFB's own presets, saved from its interface, can hold `"skin_material_type": "ENHANCED_SSS"` and `"eyes_material_type": "PROCEDURAL_EYES"`. `freeze` cannot rewire those for glTF, so `mpfb.human` stops on them. Leave both keys out.
- A human without a `rig` cannot be posed, and its garments find no bone weights. `freeze` still turns its parts into plain meshes.
- The eyes of MakeHuman have a clear cornea shell. Drawn opaque, it hides the iris. `freeze` deletes its faces.
- MPFB 2.0.18 has no expressions: its folder of expression presets is empty, its expressions need the face unit pack `faceunits01`, which is not pinned, and the asset pack holds none.
- MPFB adds a collection for the human. Remove it when it is empty after you move the parts.
- A pose that lowers the arms brings the sleeves next to the trunk. Look at the armpits and the waist from the side, not only from the front.

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
| `s80_assemble.py` | The root empty `Nostromo` as the one piece, and the turn to face -Y | One piece for a rigid ship. The front of the pipeline. A turn about the root through `matrix_world`, which turns any top object, a mesh, an empty or a light, wherever it sits. |
| `s90_checks.py` | The attachment check and the close-up's view check | A check that raises. The stage built and removed inside a check. |

The build frame: scripts `s10` to `s75` build the ship with its nose toward +Y, the frame of the earlier project. `s80_assemble.py` parents every top object under the root and turns it 180 degrees about the +Z axis of the root: `ob.matrix_world = turn @ world`. The children of a top object turn with it. The mesh data stays in the build frame, so the materials, which read object coordinates, bake the same maps as before. The shots in `model.json` are in the final axes. A new model starts with the nose toward -Y and needs no turn.

The greebles and the windows use `random.Random` with the seeds 1979 and 7 from `parameters.seeds`, the seeds of the earlier project. The lit portholes are the same as there. The greeble scatter follows the same rules and density, but its places changed once, when `MB.finish` got its fixed order (see "Determinism").

One build on this Mac, with `--views`, takes about 5 minutes: 38 s for the scripts, about 165 s for twelve maps of 4096 x 4096 on Metal, and four views of 1920 x 1080 at 192 samples. The `.glb` is about 134 MB, nearly all of it the twelve PNG maps. The build reports 192,238 triangles, 333 structure islands with 0 free, and no pipe in the close-up.
