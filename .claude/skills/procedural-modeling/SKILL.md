---
name: procedural-modeling
description: How to model, build or change a 3D model of Tertius in Blender, such as a ship, a station, a prop or any other Blender asset. The only source of a model is a folder of Blender Python scripts, and the .blend, .glb, baked textures and views are build outputs. Load it for any request to model, build, change, fix, bake, export or render a 3D model, a ship, a prop or a Blender asset, and before any use of the blender MCP tools or of tools/blender/.
---

# Procedural modeling in Blender

This skill teaches the way of work for the models of Tertius. The design is in `docs/plans/asset-pipeline.md`, "Scripted models". The tools are in `tools/README.md`, "Scripted models in Blender". Long detail is in [reference.md](reference.md) next to this file.

## 1. The scripts are the only source

- A scripted model is a folder whose name ends in `.model`, inside the `assets/` folder of a content package. Example: `content/tertius.demo/assets/ships/nostromo.model/`.
- The folder holds `model.json`, the model scripts that `model.json` names and, if you want, `README.md`. A file that `model.json` does not name stops the build.
- The `.blend` file, the `.glb` file, the baked textures and the views are build outputs. Nobody edits a `.blend` file by hand.
- No build output goes into git: no `.blend`, no `.glb`, no baked PNG. The remote, a tangled knot, has no Git LFS.
- To change a model, change a script. Then build again from an empty scene.

## 2. One-time setup

1. The assets shell, `nix develop .#assets`, holds Blender 5.2.2 and the MCP server `blender-mcp` 1.0.3. The tools in `tools/blender/` enter it by themselves. Its first start downloads about 600 MiB, which take 1.7 GiB in the Nix store.
2. Install the MCP add-on into Blender: `tools/blender/setup.sh`. If version 1.0.3 is already installed and enabled, the script changes nothing.
3. Approve the MCP server. The project's `.mcp.json` registers the server `blender`. In a new session, Claude Code asks the user once to approve the servers of the project. The user can also enable it with `/mcp`. Ask the user for this approval. Never add `enabledMcpjsonServers` to a settings file yourself.

After the approval, the native tools are `mcp__blender__execute_blender_code`, `mcp__blender__render_thumbnail_to_path`, `mcp__blender__render_viewport_to_path` and the other tools of the server. Without the approval, use the fallback in reference.md, "Without the MCP tools".

## 3. The authoring loop

The MCP server sends each tool call to the bridge: a headless Blender that listens on `localhost:9876`. One bridge serves the machine, and it belongs to the checkout or worktree that started it. `bridge.sh` refuses a start or a stop from another checkout and names the owner. Then wait until the other agent stops its bridge. Never drive another checkout's bridge.

1. Start the bridge: `tools/blender/bridge.sh start`.
2. Build the scene with `execute_blender_code`. Run every script of `model.json` in order, through the same function that the build tool calls:

   ```python
   import importlib, sys
   sys.path.insert(0, "<repository>/tools/blender")
   from tertius_blender import run
   importlib.reload(run)

   MODEL = "<repository>/content/tertius.demo/assets/ships/nostromo.model"
   run.new_scene()
   result = {}
   for script in run.load_model(MODEL)["scripts"]:
       result[script] = run.run_script(MODEL, script)
   ```

   `<repository>` is the absolute path of your checkout or worktree. `run_script` reads `model.json` again and reloads the other modules of the model library on each call, and the snippet reloads `run` itself, so every edit applies at once. `run_script` raises an error if the bridge runs the library of another checkout. The tool returns the dictionary that the code assigns to `result`.
3. Add the stage and pick a shot. The render tools need a camera, and only the stage adds one. `stage.build` also sets Cycles to the GPU, and saves every render setting that it changes:

   ```python
   import bpy
   from tertius_blender import run, stage

   views = run.load_model(MODEL)["views"]
   stage.build(views)
   stage.use_shot(views["shots"][0])
   bpy.context.scene.render.resolution_percentage = 50
   ```

4. Render with `render_thumbnail_to_path` (small and fast) or `render_viewport_to_path` (the full settings of the stage). Pass a file name, such as `look.png`. The tool writes the image into `blender_mcp/` in the temporary folder of the bridge and returns the path. The MCP server waits at most 300 s for one tool call. A longer render fails the call while the bridge renders on, so lower `resolution_percentage` or `scene.cycles.samples` for a quick look.
5. Open that path with the Read tool and look at the image. Say what you see before you change anything.
6. Remove the stage with `stage.remove()`. It also restores the render settings. A camera in the scene fails the build.
7. Change the script file with the Edit tool. Then go back to step 2, from `run.new_scene()`. Later scripts build on earlier ones, so run the whole list and not only the changed script.
8. Before each commit, build from scratch with the build tool, look at every view and read `build.json`:

   ```bash
   tools/blender/build.sh <model folder> --out <scratch folder>/build --views
   ```

9. At the end of the work, stop the bridge: `tools/blender/bridge.sh stop`. Open every image first, because the stop deletes the temporary folder.

## 4. Seeds and parameters in model.json

`model.json` names the scripts in order, the `seed`, the `parameters`, the `bake` and the `views` with their shots. reference.md lists every key.

- Every random choice comes from `model.rng` or from a `random.Random` seeded with a value of `model.json`. `model.rng` is seeded with `<seed>/<script file name>`, so a new script does not change the random choices of the others.
- A value that you tune belongs in `parameters`, not in a script. A script reads it as `model.params`.
- Never iterate over a `set` of Blender objects or BMesh elements. Python orders such a set by memory address, so the model differs between builds. `dict.fromkeys(...)` keeps a fixed order.
- A script changes only the scene. It writes no file, and it adds no camera and no light for the views. The bake, the save, the export and the views belong to the build tool.
- Two builds of the same scripts on one machine give the same meshes, UV maps and nodes. `MB.finish` puts the elements of each mesh into an order of their coordinates, because some BMesh operators order them by memory address. Two builds with `--device cpu` give the same `.glb` bytes. A bake on Metal differs in a few pixels by one level, so compare two Metal builds by their look, not by their bytes.

## 5. The checks

A check is a model script that raises an exception when the model breaks a rule. The build then fails with exit code 3, and `build.json` names the script and the message.

- Attachment: `checks.attachment(objects, anchors)` finds the mesh islands of `objects` that touch no island of `anchors`, directly or through other islands. Require `"free": 0`. A part that floats free is a modeling mistake.
- Views: `checks.view_shares(camera)` casts a grid of rays through the frame of a shot and returns the share of each object that the camera sees. Use it to forbid or require an object in a shot. It needs the camera of the shot: build the stage, check, and remove the stage in a `finally` block (see `s90_checks.py` of the Nostromo).
- Polygon count: `build.json` gives `triangles`. The importer warns (AS2015) above `budget.triangles`, 100,000 by default, unless the sidecar raises it. Check the count after each build.
- Your eyes: open every view of the build with the Read tool. A check finds only what it tests.

## 6. Bakes and export

- glTF holds no procedural shader. So the build bakes the material of each object in `bake.objects` into three maps: base color, roughness and normal.
- The contract of a bake object: a mesh with one material and an active UV map. No other object uses that material. The material has one Principled BSDF node, linked to the material output, and its metallic input holds a constant. A model script makes the UV map.
- Base color and roughness bake as emission of the value that feeds the input, so metallic and lighting stay out of the maps.
- Emission and transparency are not baked. Give a glowing part a Principled BSDF with constant inputs, which glTF holds as it is.
- If Cycles finds a GPU, the bake runs on it: Metal on this Mac. `--device cpu` forces the CPU. On Linux, the nixpkgs Blender bakes on the CPU. The first Metal bake in a new Blender compiles the GPU kernels, which takes about three minutes. The importer always bakes on the CPU, because only a CPU bake gives the same maps on every run.
- `export.write_glb` holds the export options of the pipeline. The build tool runs it in a second Blender with one thread, and with the camera check on: a camera in the scene fails the build.

## 7. The conventions of the asset pipeline

- Units are meters. In Blender, +Z is up, the front of the model faces -Y and its right side faces -X.
- Every root object is a piece. Parent the parts of one rigid thing under one root, or every root mesh becomes a piece of its own. Mark a piece below the root with the custom property `tertius_piece = 1`. The custom property survives a rename.
- The origin of a piece node is the origin of the piece in the engine. Put the origin of a door on its hinge.
- The asset id keeps the extension of the folder, and a fragment names a piece: `tertius.demo:ships/nostromo.model#Nostromo`.
- Script names and shot names use lowercase letters, digits, `-` and `_`.
- Collision markers, node poses and lights follow "Models: nodes and pieces" and "Collision" of the design.

## 8. Rules for this machine

- Run one Blender at a time: one build or one bridge. `pgrep -fl blender` shows a running one.
- Quit Blender when the work is done. The build tool stops its own Blender. Stop the bridge with `bridge.sh stop`.
- Blender ignores Ctrl+C and SIGINT. Stop it with SIGTERM, then delete its temporary folder. The tools do both.
- Before each bake or full build, run `df -h /System/Volumes/Data`. Stop if less than 30 GB is free. Other work on this machine needs the space too. The build tool itself stops with exit code 6 only below its estimate of what the build needs, 3 GB for the Nostromo.
- Other agents time GPU passes on this machine, and they wait while a Blender process runs. Run your Blender work back to back, keep the bridge up only while you use it, and start no Blender work while another agent times GPU passes.
- Put build outputs into a scratch folder. Never put them into the repository, and never into `bin/assets/`, which the importer owns.

## 9. Pitfalls that cost time before

reference.md, "Pitfalls", explains each one.

- The bridge needs `--online-mode`. Without it, the add-on opens no socket. `bridge.sh` passes it.
- Blender computes tangents on several threads, and their last bits change between runs. The build tool exports in a second Blender with `--threads 1`. A `.glb` that you export yourself in the bridge can differ in its tangents.
- Overlapping blocks of one hull hide faces inside each other, and the hidden faces take texture space. An exact boolean self-union makes one closed surface.
- A greeble on a slope leaves a gap under one edge. Sink each greeble into the hull, and test its footprint corners with rays.
- Parts under the hull look as if they float, even when they touch. The stage has a weak fill light from below for this. The attachment check is the proof.
- Pipes in front of the hull spoil a close-up. Pick the camera so that the hull hides them, and lock it with a view check.
- Build meshes with `MB` and end them with `MB.finish`. A mesh made by raw BMesh operators keeps an order that changes between runs.

## 10. The worked example: the Nostromo

The Nostromo is a tug of about 244 m, with 192,238 triangles in 19 meshes under the one piece `Nostromo`.

- Folder: `content/tertius.demo/assets/ships/nostromo.model/`.
- Build: `tools/blender/build.sh content/tertius.demo/assets/ships/nostromo.model --out <scratch folder>/nostromo --views`.
- Its twelve scripts build the hull, engines, greebles, pipes, structures, windows, materials and UV maps, then turn the ship to face -Y and run the checks. reference.md, "The Nostromo", lists each script and what it teaches.
- The build bakes `Hull_Fore`, `Hull_Mid`, `Hull_Aft` and `Engines` into twelve maps of 4096 x 4096. It renders four views: `three-quarter`, `side`, `rear-low` and `closeup`.
- Its 192,238 triangles are above the default `budget.triangles` of 100,000. So its import warns (AS2015) until a sidecar `nostromo.model.import.json` raises the budget.

Read its scripts before you start a new model. Copy its shape: one script per part of the work, a short comment at the top of each, the tuned values in `model.json`, and the checks last.
