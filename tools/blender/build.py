"""The build of one scripted model. Runs inside headless Blender, started twice by build.sh:

    blender --background --factory-startup --python build.py -- <model folder> <out folder> [--views] [--device d]
    blender --background --factory-startup --threads 1 <out>/model.blend --python build.py -- --export <out folder>

The first run writes model.blend, textures/ and, with --views, views/ into the output folder. The second run opens
model.blend and writes model.glb. Its single thread makes the tangents of the export the same on every run. Both runs
write build.json. See docs/plans/asset-pipeline.md, "The build tool".
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

# What build.json says after the first run, until the export run replaces it. If the export run never ends, the
# build failed there.
_EXPORT_PENDING = "Blender stopped before the export finished. See blender.log."


def _at(report, step, script=None):
    """Records the step of the build that runs now, so build.json names the step of a failure."""
    report["step"], report["script"] = step, script


def main(argv):
    parser = argparse.ArgumentParser(prog="build.py")
    parser.add_argument("model", nargs="?")
    parser.add_argument("out")
    parser.add_argument("--views", action="store_true")
    parser.add_argument("--device", choices=("auto", "cpu"), default="auto")
    parser.add_argument("--export", action="store_true")
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out)
    if args.export:
        report = _read_report(out)
        _run(report, out, lambda: export_run(out, report))
    else:
        report = {"format": 1, "status": "failed", "model": os.path.abspath(args.model),
                  "blender": bpy.app.version_string, "seconds": 0.0}
        _run(report, out, lambda: build(args, out, report))
        if report["status"] == "ok":
            report["status"] = "failed"
            report["error"] = {"step": "export", "script": None, "message": _EXPORT_PENDING, "traceback": None}
    _write_report(out, report)


def _run(report, out, work):
    """Runs one of the two runs and records its status, its error and its seconds in `report`."""
    _at(report, None)
    start = time.time()
    try:
        work()
        report["status"] = "ok"
        report.pop("error", None)
    except run.ModelError as ex:
        report["status"] = "badInput"
        report["error"] = _error(report, ex)
    except Exception as ex:  # noqa: BLE001 - every failure of a build is reported in build.json.
        report["status"] = "failed"
        report["error"] = _error(report, ex)
    report.pop("step")
    report.pop("script")
    report["seconds"] = round(report["seconds"] + time.time() - start, 2)


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
    _at(report, "save")
    export.save_blend(os.path.join(out, "model.blend"))
    if args.views:
        # The stage comes after the save, so it never reaches model.blend or model.glb.
        _at(report, "views")
        stage.build(spec["views"], device=args.device)
        paths = stage.render(spec["views"], os.path.join(out, "views"))
        report["views"] = [os.path.relpath(p, out) for p in paths]


def export_run(out, report):
    """The second run: Blender opened model.blend with one thread. Exports model.glb and completes the report."""
    _at(report, "export")
    export.write_glb(os.path.join(out, "model.glb"), refuse_cameras=True)
    report["roots"] = export.roots()
    report["marked"] = export.marked()
    report["triangles"] = export.triangles()
    report["outputs"] = sorted(os.path.relpath(os.path.join(d, f), out)
                               for d, _dirs, files in os.walk(out) for f in files
                               if f not in ("blender.log", "build.json"))


def _read_report(out):
    with open(os.path.join(out, "build.json"), encoding="utf-8") as fh:
        return json.load(fh)


def _write_report(out, report):
    with open(os.path.join(out, "build.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
        fh.write("\n")


def _error(report, ex):
    return {"step": report["step"], "script": report["script"], "message": str(ex),
            "traceback": traceback.format_exc()}


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
