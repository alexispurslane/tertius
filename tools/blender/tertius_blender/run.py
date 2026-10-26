"""Runs model scripts. The build tool and an agent's execute_blender_code call use the same functions, so a script
runs the same way in both (docs/plans/asset-pipeline.md, "Model scripts").

An agent in the bridge:

    import importlib, sys
    sys.path.insert(0, "/path/to/tertius/tools/blender")
    from tertius_blender import run
    importlib.reload(run)  # run_script reloads the other modules of the library, but not its own.

    run.new_scene()
    result = run.run_script("/path/to/model folder", "s10_hull.py")
"""

import importlib
import json
import os
import random
import re
import sys

import bpy

# The modules of the model library that run_script reloads, in the order of their imports of each other. A module
# cannot reload itself while it runs, so the code of the authoring loop reloads run.
_LIBRARY = ("geometry", "nodes", "checks", "bake", "export", "stage", "mpfb")

# tools/blender of the checkout whose model library this Blender runs.
_LIBRARY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The name rules of content packages and mods: lowercase letters, digits, "-" and "_".
_SCRIPT_NAME = re.compile(r"^[a-z0-9][a-z0-9_-]*\.py$")
_SHOT_NAME = re.compile(r"^[a-z0-9][a-z0-9_-]*$")
_OPTIONAL_FILES = ("README.md",)


class ModelError(Exception):
    """model.json or the model folder breaks a rule. The build reports it as bad input."""


class Model:
    """What a model script gets as the name `model`."""

    def __init__(self, folder, seed, params, script):
        self.folder = folder
        self.seed = seed
        self.params = params
        # Seeded per script, so a new script in the list does not change the random choices of the others.
        self.rng = random.Random(_script_seed(seed, script))


def _script_seed(seed, script):
    return "{:d}/{:s}".format(seed, script)


def new_scene():
    """Replaces the open file with an empty scene in meters. Every build starts here."""
    bpy.ops.wm.read_homefile(use_empty=True, use_factory_startup=True)
    units = bpy.context.scene.unit_settings
    units.system = "METRIC"
    units.scale_length = 1.0
    units.length_unit = "METERS"


def run_script(folder, script):
    """Runs one model script of the model folder `folder` and returns the dictionary it assigns to `result`.

    Reads model.json again and reloads the model library first, so edits apply in a long session of the bridge.
    """
    folder = os.path.abspath(folder)
    _check_library(folder)
    spec = load_model(folder)
    if script not in spec["scripts"]:
        raise ModelError("model.json does not name the script {:s}".format(script))
    _reload_library()
    path = os.path.join(folder, script)
    with open(path, encoding="utf-8") as fh:
        code = compile(fh.read(), path, "exec")
    # A guard against a stray call of the random module. Scripts use model.rng.
    random.seed(_script_seed(spec["seed"], script))
    namespace = {"__name__": "__tertius_model_script__", "__file__": path,
                 "model": Model(folder, spec["seed"], spec["parameters"], script)}
    exec(code, namespace)
    result = namespace.get("result", {})
    if not isinstance(result, dict):
        raise TypeError("{:s} assigned a {:s} to result. It must be a dict.".format(script, type(result).__name__))
    return result


def _check_library(folder):
    """Raises when the model folder lies in a checkout whose model library is not the one that this Blender runs.
    Python loads a package once per process, so a bridge that another checkout started runs that checkout's library,
    and a sys.path entry of this checkout changes nothing. A folder outside any checkout passes."""
    checkout = _checkout_of(folder)
    if checkout is None:
        return
    library = os.path.join(checkout, "tools", "blender")
    if os.path.realpath(library) != os.path.realpath(_LIBRARY_ROOT):
        raise RuntimeError("the model folder {:s} lies in the checkout {:s}, but this Blender runs the model library "
                           "of {:s}. Use a Blender of that checkout: stop the bridge of the other checkout, or "
                           "wait for it.".format(folder, checkout, os.path.dirname(os.path.dirname(_LIBRARY_ROOT))))


def _checkout_of(folder):
    """The nearest folder above `folder` that holds a model library, or None."""
    path = folder
    while True:
        if os.path.isfile(os.path.join(path, "tools", "blender", "tertius_blender", "run.py")):
            return path
        parent = os.path.dirname(path)
        if parent == path:
            return None
        path = parent


def _reload_library():
    for name in _LIBRARY:
        module = sys.modules.get("tertius_blender." + name)
        if module is not None:
            importlib.reload(module)


def load_model(folder):
    """Reads model.json of the model folder `folder`, checks it and the files of the folder, and returns it with
    every default filled in. Raises ModelError for a broken rule."""
    path = os.path.join(folder, "model.json")
    try:
        with open(path, encoding="utf-8") as fh:
            spec = json.load(fh)
    except (OSError, ValueError) as ex:
        raise ModelError("cannot read {:s}: {:s}".format(path, str(ex))) from ex
    _check_keys(spec, "model.json", required=("format", "scripts", "seed"),
                optional=("$schema", "parameters", "bake", "views"))
    _require(spec["format"] == 1, "model.json: format must be 1")
    _check_scripts(spec["scripts"])
    _require(_is_int(spec["seed"]), "model.json: seed must be an integer")
    spec.setdefault("parameters", {})
    _require(isinstance(spec["parameters"], dict), "model.json: parameters must be an object")
    spec["bake"] = _bake(spec.get("bake", {}))
    spec["views"] = _views(spec.get("views", {}))
    _check_folder(folder, spec["scripts"])
    return spec


def _check_scripts(scripts):
    _require(isinstance(scripts, list) and scripts, "model.json: scripts must be a list of at least one file name")
    for name in scripts:
        _require(isinstance(name, str) and _SCRIPT_NAME.match(name),
                 "model.json: the script name {!r} breaks the name rules".format(name))
    _require(len(set(scripts)) == len(scripts), "model.json: a script is named twice")


def _check_folder(folder, scripts):
    """A model folder holds model.json, the scripts that it names and optionally README.md (AS1006, AS1007)."""
    present = sorted(name for name in os.listdir(folder) if not name.startswith("."))
    allowed = {"model.json", *scripts, *_OPTIONAL_FILES}
    missing = [name for name in scripts if name not in present]
    _require(not missing, "model.json names missing scripts: {:s}".format(", ".join(missing)))
    stray = [name for name in present if name not in allowed or os.path.isdir(os.path.join(folder, name))]
    _require(not stray, "the model folder holds files that model.json does not name: {:s}".format(", ".join(stray)))


def _bake(bake):
    _check_keys(bake, "bake", optional=("objects", "size", "samples", "margin"))
    bake = {"objects": [], "size": 1024, "samples": 8, "margin": 16, **bake}
    objects = bake["objects"]
    _require(isinstance(objects, list) and all(isinstance(o, str) for o in objects),
             "bake.objects must be a list of object names")
    _require(len(set(objects)) == len(objects), "bake.objects names an object twice")
    size = bake["size"]
    _require(_is_int(size) and 64 <= size <= 8192 and size & (size - 1) == 0,
             "bake.size must be a power of two from 64 to 8192")
    _require(_is_int(bake["samples"]) and bake["samples"] >= 1, "bake.samples must be an integer of at least 1")
    _require(_is_int(bake["margin"]) and bake["margin"] >= 0, "bake.margin must be an integer of at least 0")
    return bake


def _views(views):
    _check_keys(views, "views", optional=("resolution", "samples", "shots"))
    views = {"resolution": [1920, 1080], "samples": 128, "shots": [], **views}
    resolution = views["resolution"]
    _require(isinstance(resolution, list) and len(resolution) == 2 and all(_is_int(v) and v > 0 for v in resolution),
             "views.resolution must be two positive integers")
    _require(_is_int(views["samples"]) and views["samples"] >= 1, "views.samples must be an integer of at least 1")
    _require(isinstance(views["shots"], list), "views.shots must be a list")
    views["shots"] = [_shot(shot) for shot in views["shots"]]
    names = [shot["name"] for shot in views["shots"]]
    _require(len(set(names)) == len(names), "views.shots names a shot twice")
    return views


def _shot(shot):
    _check_keys(shot, "a shot", required=("name", "camera", "target", "sun"), optional=("lens",))
    shot = {"lens": 50, **shot}
    _require(isinstance(shot["name"], str) and _SHOT_NAME.match(shot["name"]),
             "the shot name {!r} breaks the name rules".format(shot["name"]))
    for key in ("camera", "target", "sun"):
        value = shot[key]
        _require(isinstance(value, list) and len(value) == 3 and all(_is_number(v) for v in value),
                 "shot {:s}: {:s} must be three numbers".format(shot["name"], key))
    _require(_is_number(shot["lens"]) and shot["lens"] > 0, "shot {:s}: lens must be positive".format(shot["name"]))
    return shot


def _check_keys(obj, where, required=(), optional=()):
    _require(isinstance(obj, dict), "{:s} must be an object".format(where))
    missing = [key for key in required if key not in obj]
    _require(not missing, "{:s} lacks the keys {:s}".format(where, ", ".join(missing)))
    unknown = sorted(key for key in obj if key not in required and key not in optional)
    _require(not unknown, "{:s} has unknown keys: {:s}".format(where, ", ".join(unknown)))


def _is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


def _is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _require(condition, message):
    if not condition:
        raise ModelError(message)
