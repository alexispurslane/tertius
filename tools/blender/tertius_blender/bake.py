"""The bake: turns the procedural material of an object into three texture maps that glTF can hold
(docs/plans/asset-pipeline.md, "The bake").

The contract of a bake object: a mesh with one material and an active UV map. The material has one Principled BSDF
node, linked to the material output, and its metallic input holds a constant.
"""

import glob
import os
import time

import bpy

# The order of the passes, and the color space of each map.
PASSES = (("basecolor", "sRGB"), ("roughness", "Non-Color"), ("normal", "Non-Color"))

# The GPU kinds of Cycles, in the order that use_device tries them.
_GPU_KINDS = ("METAL", "OPTIX", "CUDA", "HIP", "ONEAPI")

# The names of the nodes that the bake adds to a material for the time of the bake.
_TARGET = "TERTIUS_BAKE_TARGET"
_EMIT = "TERTIUS_BAKE_EMIT"


def use_device(device):
    """Sets Cycles to render on a GPU when `device` is "auto" and Cycles finds one, otherwise on the CPU.
    Returns the device kind in use: "CPU" or one of METAL, OPTIX, CUDA, HIP and ONEAPI."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    if device == "auto":
        for kind in _GPU_KINDS:
            try:
                prefs.compute_device_type = kind
            except TypeError:
                continue  # This Blender has no support for this kind.
            prefs.refresh_devices()
            gpus = [d for d in prefs.devices if d.type == kind]
            if gpus:
                # GPU only: the CPU is shared with other work on the machine.
                for d in prefs.devices:
                    d.use = d.type == kind
                scene.cycles.device = "GPU"
                return kind
    prefs.compute_device_type = "NONE"
    scene.cycles.device = "CPU"
    return "CPU"


def bake_object(name, folder, size, samples, margin):
    """Bakes the three maps of the object `name` into folder/<name>_<pass>.png, then gives the object a material
    made from them. The procedural material stays in the file with a fake user. Returns one report per map."""
    ob = bpy.data.objects[name]
    material = _procedural_material(ob)
    tree = material.node_tree
    principled = _principled(material)
    output = tree.get_output_node("CYCLES") or tree.get_output_node("ALL")
    if output is None:
        raise ValueError("material {:s} has no material output".format(material.name))
    scene = bpy.context.scene
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    settings = scene.render.bake
    settings.margin = margin
    settings.margin_type = "EXTEND"
    settings.target = "IMAGE_TEXTURES"
    settings.normal_space = "TANGENT"
    settings.use_selected_to_active = False
    _select_only(ob)
    os.makedirs(folder, exist_ok=True)
    reports = []
    maps = {}
    try:
        for pass_name, color_space in PASSES:
            start = time.time()
            image = _bake_pass(ob, tree, principled, output, pass_name, color_space, size, margin)
            path = os.path.join(folder, "{:s}_{:s}.png".format(name, pass_name))
            image.filepath_raw = path
            image.file_format = "PNG"
            image.save()
            maps[pass_name] = image
            reports.append({"object": name, "pass": pass_name, "file": path, "seconds": round(time.time() - start, 2)})
    finally:
        tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])
        for node_name in (_TARGET, _EMIT):
            node = tree.nodes.get(node_name)
            if node is not None:
                tree.nodes.remove(node)
        _delete_tile_buffers()
    material.use_fake_user = True
    metallic = principled.inputs["Metallic"].default_value
    ob.material_slots[0].material = _baked_material(material.name + "_Baked", ob, maps, metallic)
    return reports


def _procedural_material(ob):
    if ob.type != "MESH" or len(ob.material_slots) != 1 or ob.material_slots[0].material is None:
        raise ValueError("bake object {:s} must be a mesh with exactly one material".format(ob.name))
    if ob.data.uv_layers.active is None:
        raise ValueError("bake object {:s} has no UV map".format(ob.name))
    return ob.material_slots[0].material


def _principled(material):
    nodes = [n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"]
    if len(nodes) != 1:
        raise ValueError("material {:s} must have one Principled BSDF node, not {:d}".format(material.name, len(nodes)))
    if nodes[0].inputs["Metallic"].is_linked:
        raise ValueError("material {:s}: the metallic input must hold a constant to be baked".format(material.name))
    return nodes[0]


def _select_only(ob):
    view = bpy.context.view_layer
    for other in view.objects:
        other.select_set(False)
    ob.select_set(True)
    view.objects.active = ob


def _bake_pass(ob, tree, principled, output, pass_name, color_space, size, margin):
    """Bakes one pass into a new image. Base color and roughness bake as emission of the value that feeds the
    Principled input, so metallic and lighting cannot leak into them."""
    image_name = "{:s}_{:s}".format(ob.name, pass_name)
    old = bpy.data.images.get(image_name)
    if old is not None:
        bpy.data.images.remove(old)
    image = bpy.data.images.new(image_name, size, size, alpha=False)
    image.colorspace_settings.name = color_space
    target = tree.nodes.get(_TARGET) or tree.nodes.new("ShaderNodeTexImage")
    target.name = _TARGET
    target.image = image
    for node in tree.nodes:
        node.select = False
    target.select = True
    tree.nodes.active = target
    if pass_name == "normal":
        tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])
        bake_type = "NORMAL"
    else:
        emit = tree.nodes.get(_EMIT) or tree.nodes.new("ShaderNodeEmission")
        emit.name = _EMIT
        emit.inputs["Strength"].default_value = 1.0
        _feed(tree, principled.inputs["Base Color" if pass_name == "basecolor" else "Roughness"], emit.inputs["Color"])
        tree.links.new(emit.outputs["Emission"], output.inputs["Surface"])
        bake_type = "EMIT"
    bpy.ops.object.bake(type=bake_type, use_clear=True, margin=margin)
    return image


def _feed(tree, source_input, target_input):
    """Links what feeds `source_input` into `target_input`, or copies its constant."""
    for link in list(target_input.links):
        tree.links.remove(link)
    if source_input.is_linked:
        tree.links.new(source_input.links[0].from_socket, target_input)
        return
    value = source_input.default_value
    target_input.default_value = tuple(value) if hasattr(value, "__len__") else (value, value, value, 1.0)


def _baked_material(name, ob, maps, metallic):
    """A material made from the three baked maps, which the glTF exporter writes as textures."""
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    tree = material.node_tree
    tree.nodes.clear()
    uv_name = ob.data.uv_layers.active.name
    uv = tree.nodes.new("ShaderNodeUVMap")
    uv.uv_map = uv_name
    textures = {}
    for pass_name, _color_space in PASSES:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = maps[pass_name]
        node.interpolation = "Linear"
        tree.links.new(uv.outputs["UV"], node.inputs["Vector"])
        textures[pass_name] = node
    normal_map = tree.nodes.new("ShaderNodeNormalMap")
    normal_map.space = "TANGENT"
    normal_map.uv_map = uv_name
    principled = tree.nodes.new("ShaderNodeBsdfPrincipled")
    principled.inputs["Metallic"].default_value = metallic
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(textures["basecolor"].outputs["Color"], principled.inputs["Base Color"])
    # glTF reads roughness from the green channel of a metallic-roughness texture. The exporter builds that
    # texture from this link.
    tree.links.new(textures["roughness"].outputs["Color"], principled.inputs["Roughness"])
    tree.links.new(textures["normal"].outputs["Color"], normal_map.inputs["Color"])
    tree.links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    for k, node in enumerate((uv, *textures.values(), normal_map, principled, output)):
        node.location = (k * 260, 0)
    return material


def _delete_tile_buffers():
    """Cycles writes tile buffers of large images to Blender's temporary folder. Delete them after each bake."""
    for path in glob.glob(os.path.join(bpy.app.tempdir, "cycles-tile-buffer-*")):
        os.remove(path)
