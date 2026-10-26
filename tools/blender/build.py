"""The build of one scripted model. Runs inside headless Blender, started by build.sh:

    blender --background --factory-startup --python build.py -- <model folder> <out folder> [--views] [--device d]

Writes model.blend, model.glb, textures/ and, with --views, views/ into the output folder, and always build.json.
See docs/plans/asset-pipeline.md, "The build tool".
"""

import argparse
import json
import os
import sys
import time
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from tertius_blender import bake, export, run, stage  # noqa: E402


def _at(report, step, script=None):
    """Records the step of the build that runs now, so build.json names the step of a failure."""
    report["step"], report["script"] = step, script


def main(argv):
    parser = argparse.ArgumentParser(prog="build.py")
    parser.add_argument("model")
    parser.add_argument("out")
    parser.add_argument("--views", action="store_true")
    parser.add_argument("--device", choices=("auto", "cpu"), default="auto")
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out)
    report = {"format": 1, "status": "failed", "model": os.path.abspath(args.model),
              "blender": bpy.app.version_string, "step": None, "script": None}
    start = time.time()
    try:
        build(args, out, report)
        report["status"] = "ok"
    except run.ModelError as ex:
        report["status"] = "badInput"
        report["error"] = _error(report, ex)
    except Exception as ex:  # noqa: BLE001 - every failure of a build is reported in build.json.
        report["error"] = _error(report, ex)
    report.pop("step")
    report.pop("script")
    report["seconds"] = round(time.time() - start, 2)
    with open(os.path.join(out, "build.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
        fh.write("\n")


def build(args, out, report):
    _at(report, "input")
    spec = run.load_model(os.path.abspath(args.model))
    run.new_scene()
    report["scripts"] = []
    for script in spec["scripts"]:
        _at(report, "script", script)
        begin = time.time()
        result = run.run_script(args.model, script)
        report["scripts"].append({"name": script, "seconds": round(time.time() - begin, 2), "result": result})
    _at(report, "bake")
    report["device"] = bake.use_device(args.device)
    report["bake"] = []
    for name in spec["bake"]["objects"]:
        maps = bake.bake_object(name, os.path.join(out, "textures"), spec["bake"]["size"], spec["bake"]["samples"],
                                spec["bake"]["margin"])
        report["bake"] += [{**m, "file": os.path.relpath(m["file"], out)} for m in maps]
    _at(report, "export")
    export.save_blend(os.path.join(out, "model.blend"))
    export.write_glb(os.path.join(out, "model.glb"))
    report["pieces"] = export.pieces()
    report["triangles"] = export.triangles()
    if args.views:
        _at(report, "views")
        stage.build(spec["views"])
        paths = stage.render(spec["views"], os.path.join(out, "views"))
        report["views"] = [os.path.relpath(p, out) for p in paths]
    report["outputs"] = sorted(os.path.relpath(os.path.join(d, f), out)
                               for d, _dirs, files in os.walk(out) for f in files if f != "blender.log")


def _error(report, ex):
    return {"step": report["step"], "script": report["script"], "message": str(ex),
            "traceback": traceback.format_exc()}


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
