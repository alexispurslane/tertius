"""The stage of the views: a dark world with sparse stars, a key sun, a fill sun and one camera per shot
(docs/plans/asset-pipeline.md, "The build tool"). The build adds the stage after it saved model.blend, so the stage
never reaches model.blend or model.glb. Its objects live in the collection "Stage".

build saves every scene setting that the stage changes, and remove restores them. So a check that builds and removes
the stage before the bake leaves the bake and model.blend with the settings that they had before."""

import json
import math
import os

import bpy
from mathutils import Vector

from . import bake

COLLECTION = "Stage"

# The scene settings that the stage changes, as attribute paths from the scene. The scene camera and the world are
# saved by name.
_SETTINGS = (
    "render.engine", "render.resolution_x", "render.resolution_y", "render.resolution_percentage",
    "render.film_transparent", "render.filepath", "render.image_settings.file_format",
    "render.image_settings.color_depth", "cycles.device", "cycles.samples", "cycles.use_adaptive_sampling",
    "cycles.adaptive_threshold", "cycles.use_denoising", "cycles.max_bounces", "view_settings.view_transform",
    "view_settings.look",
)
# The custom property of the scene that holds the saved settings while the stage exists. It survives a reload of
# this module in the bridge.
_SAVED = "tertius_stage_saved"


def camera_name(shot):
    """The name of the camera object of a shot."""
    return "VIEW_" + shot["name"]


def build(views, device="auto"):
    """Adds the stage for the views of model.json (the "views" object, with its defaults filled in). Renders run on
    `device` as bake.use_device picks it: "auto" for a GPU if Cycles finds one, or "cpu"."""
    scene = bpy.context.scene
    if _SAVED not in scene:
        scene[_SAVED] = json.dumps(_save(scene))
    bake.use_device(device)
    collection = bpy.data.collections.get(COLLECTION) or bpy.data.collections.new(COLLECTION)
    if COLLECTION not in scene.collection.children:
        scene.collection.children.link(collection)
    scene.world = _space()
    _sun("Sun", collection, energy=5.0, angle=0.6, color=(1.0, 0.96, 0.9))
    # A weak, cool fill from below, so the faces that point down do not vanish against space.
    _sun("Fill", collection, energy=1.0, angle=10.0, color=(0.75, 0.82, 1.0))
    for shot in views["shots"]:
        _camera(camera_name(shot), collection, shot)
    scene.render.engine = "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = views["resolution"]
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_depth = "8"
    scene.cycles.samples = views["samples"]
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"


def use_shot(shot):
    """Makes the camera of `shot` the scene camera and turns the suns for it."""
    scene = bpy.context.scene
    camera = bpy.data.objects[camera_name(shot)]
    scene.camera = camera
    _shine_from(bpy.data.objects["Sun"], shot["sun"])
    # The fill shines from below, tilted toward the side of the camera.
    side = Vector((camera.location.x, camera.location.y, 0.0))
    side = side.normalized() if side.length > 1e-6 else Vector((1.0, 0.0, 0.0))
    _shine_from(bpy.data.objects["Fill"], side * 0.6 + Vector((0.0, 0.0, -0.8)))


def render(views, folder):
    """Renders every shot into folder/<name>.png and returns the paths."""
    os.makedirs(folder, exist_ok=True)
    paths = []
    for shot in views["shots"]:
        use_shot(shot)
        path = os.path.join(folder, shot["name"] + ".png")
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        paths.append(path)
    return paths


def remove():
    """Deletes the stage and restores the scene settings that build changed, so model scripts can run again on the
    scene."""
    collection = bpy.data.collections.get(COLLECTION)
    if collection is not None:
        for ob in list(collection.objects):
            data = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if data is not None and data.users == 0:
                (bpy.data.cameras if isinstance(data, bpy.types.Camera) else bpy.data.lights).remove(data)
        bpy.data.collections.remove(collection)
    world = bpy.data.worlds.get("Space")
    if world is not None:
        bpy.data.worlds.remove(world)
    scene = bpy.context.scene
    if _SAVED in scene:
        _restore(scene, json.loads(scene[_SAVED]))
        del scene[_SAVED]


def _save(scene):
    saved = {path: getattr(*_owner(scene, path)) for path in _SETTINGS}
    saved["camera"] = scene.camera.name if scene.camera else None
    saved["world"] = scene.world.name if scene.world else None
    return saved


def _restore(scene, saved):
    for path in _SETTINGS:
        owner, name = _owner(scene, path)
        setattr(owner, name, saved[path])
    scene.camera = bpy.data.objects.get(saved["camera"]) if saved["camera"] else None
    scene.world = bpy.data.worlds.get(saved["world"]) if saved["world"] else None


def _owner(scene, path):
    """The object that holds the setting `path` of the scene, and the name of the setting."""
    *parents, name = path.split(".")
    owner = scene
    for parent in parents:
        owner = getattr(owner, parent)
    return owner, name


def _space():
    """A near-black world with a sparse star field: a small disc at a few Voronoi cell points."""
    world = bpy.data.worlds.get("Space") or bpy.data.worlds.new("Space")
    tree = world.node_tree
    tree.nodes.clear()
    out = tree.nodes.new("ShaderNodeOutputWorld")
    background = tree.nodes.new("ShaderNodeBackground")
    coords = tree.nodes.new("ShaderNodeTexCoord")
    cells = tree.nodes.new("ShaderNodeTexVoronoi")
    cells.inputs["Scale"].default_value = 420.0
    cells.inputs["Randomness"].default_value = 1.0
    disc = tree.nodes.new("ShaderNodeMapRange")
    disc.inputs["From Min"].default_value = 0.0
    disc.inputs["From Max"].default_value = 0.06
    disc.inputs["To Min"].default_value = 1.0
    disc.inputs["To Max"].default_value = 0.0
    keep = _math(tree, "GREATER_THAN", 0.93)
    split = tree.nodes.new("ShaderNodeSeparateColor")
    star = _math(tree, "MULTIPLY", None)
    bright = _math(tree, "MULTIPLY", 2.5)
    floor = _math(tree, "ADD", 0.0006)
    links = tree.links
    links.new(coords.outputs["Generated"], cells.inputs["Vector"])
    links.new(cells.outputs["Distance"], disc.inputs["Value"])
    links.new(cells.outputs["Color"], split.inputs["Color"])
    links.new(split.outputs["Red"], keep.inputs[0])
    links.new(disc.outputs["Result"], star.inputs[0])
    links.new(keep.outputs["Value"], star.inputs[1])
    links.new(star.outputs["Value"], bright.inputs[0])
    links.new(bright.outputs["Value"], floor.inputs[0])
    links.new(floor.outputs["Value"], background.inputs["Strength"])
    background.inputs["Color"].default_value = (0.8, 0.85, 1.0, 1.0)
    links.new(background.outputs["Background"], out.inputs["Surface"])
    return world


def _math(tree, operation, second):
    node = tree.nodes.new("ShaderNodeMath")
    node.operation = operation
    if second is not None:
        node.inputs[1].default_value = second
    return node


def _sun(name, collection, energy, angle, color):
    ob = bpy.data.objects.get(name)
    if ob is None:
        ob = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        collection.objects.link(ob)
    ob.data.energy = energy
    ob.data.angle = math.radians(angle)
    ob.data.color = color
    return ob


def _shine_from(sun, direction):
    """Turns the sun so that its light comes from `direction` and travels along -direction."""
    sun.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()


def _camera(name, collection, shot):
    ob = bpy.data.objects.get(name)
    if ob is None:
        ob = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        collection.objects.link(ob)
    ob.location = shot["camera"]
    ob.data.lens = shot["lens"]
    ob.data.sensor_width = 36.0
    ob.data.clip_start = 0.05
    ob.data.clip_end = 5000.0
    ob.rotation_euler = (Vector(shot["target"]) - ob.location).to_track_quat("-Z", "Y").to_euler()
    return ob
