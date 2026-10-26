"""MPFB 2, the MakeHuman add-on for Blender, for model scripts (docs/plans/asset-pipeline.md, "Humans from MPFB").

A model script makes a human with MPFB's own code:

    from tertius_blender import mpfb
    S = mpfb.services()
    body = S.HumanService.deserialize_from_dict(info, mpfb.deserialization_settings())

The assets shell gives Blender MPFB as a system extension (BLENDER_SYSTEM_EXTENSIONS, native/blender-extensions.nix),
and the MakeHuman system assets without clothes as TERTIUS_MAKEHUMAN_ASSETS (native/makehuman-assets.nix). services()
enables the add-on for the running Blender only and makes the asset folder MPFB's second root of assets. Nothing is
saved into the preferences.

MPFB writes logs and settings into Blender's user resource folder while it loads and works. So services() refuses
to run unless BLENDER_USER_RESOURCES names a folder: the build tool and the bridge point it at a temporary folder of
their own, which they delete, and never at the user's own Blender folder.
"""

import importlib
import math
import os

import addon_utils
import bmesh
import bpy

PACKAGE = "bl_ext.system.mpfb"


class MpfbError(RuntimeError):
    """MPFB cannot run in this Blender."""


def services():
    """The module of MPFB's services, with HumanService, TargetService, RigService and the others. Enables MPFB the
    first time."""
    _check_environment()
    if PACKAGE not in bpy.context.preferences.addons:
        module = addon_utils.enable(PACKAGE, default_set=True, handle_error=_raise)
        if module is None:
            raise MpfbError("Blender did not enable {:s}.".format(PACKAGE))
    root = importlib.import_module(PACKAGE)
    assets = os.environ["TERTIUS_MAKEHUMAN_ASSETS"]
    if root.get_preference("mpfb_second_root") != assets:
        root.set_preference("mpfb_second_root", assets)
        # set_preference marks the preferences as changed. They are never saved: the build and the bridge start
        # Blender with --factory-startup, and a background Blender saves no preferences.
        bpy.context.preferences.is_dirty = False
    module = importlib.import_module(PACKAGE + ".services")
    module.LocationService.update_second_root()
    return module


# The parts of a human, by MPFB's object_type, and the names that freeze gives them.
PART_NAMES = {"Basemesh": "Body", "Eyes": "Eyes", "Eyebrows": "Eyebrows", "Eyelashes": "Eyelashes",
              "Teeth": "Teeth", "Tongue": "Tongue", "Hair": "Hair"}
# The parts whose texture alpha cuts the shape: hair cards, eyebrows and eyelashes. Every other part is opaque.
ALPHA_PARTS = ("Hair", "Eyebrows", "Eyelashes")
# The parts that lose the faces that their texture leaves transparent. The eyes of MakeHuman have a clear outer shell,
# the cornea, whose texels have alpha 0. Without it the eyes are opaque, which a renderer draws more simply.
CLEAR_FACES_REMOVED = ("Eyes",)


def human(info, name="Human"):
    """Makes a human with MPFB's HumanService.deserialize_from_dict and returns its body. `info` is MPFB's own
    human preset format (what MPFB saves as a .json human): "phenotype" holds the macro sliders, and "rig", "eyes",
    "eyebrows", "eyelashes", "teeth", "tongue", "hair" and "skin_mhmat" name assets such as "short02/short02.mhclo".
    A key that `info` leaves out keeps MPFB's default, and phenotype values merge over MPFB's default sliders.

    The body is a child of the rig when info names one. It keeps MPFB's shape keys and modifiers until freeze."""
    S = services()
    full = {"phenotype": S.TargetService.get_default_macro_info_dict(), "rig": "", "eyes": "", "eyebrows": "",
            "eyelashes": "", "teeth": "", "tongue": "", "hair": "", "proxy": "", "targets": [], "clothes": [],
            "skin_mhmat": "", "skin_material_type": "NONE", "eyes_material_type": "MAKESKIN",
            "skin_material_settings": {}, "eyes_material_settings": {}, "expressions": [],
            "alternative_materials": {}}
    for key, value in info.items():
        if key == "phenotype":
            race = {**full["phenotype"]["race"], **value.get("race", {})}
            full["phenotype"] = {**full["phenotype"], **value, "race": race}
        elif key not in full:
            raise ValueError("MPFB's human presets have no key {!r}".format(key))
        else:
            full[key] = value
    full["name"] = name
    missing = [full[k] for k in ("eyes", "eyebrows", "eyelashes", "teeth", "tongue", "hair", "skin_mhmat")
               if full[k] and S.AssetService.find_asset_absolute_path(full[k], _subdir(k)) is None]
    if missing:
        raise MpfbError("MPFB finds no asset {:s}".format(", ".join(missing)))
    return S.HumanService.deserialize_from_dict(full, deserialization_settings())


def _subdir(key):
    return "skins" if key == "skin_mhmat" else key


def pose(rig, degrees):
    """Poses the rig with MPFB's RigService.set_pose_from_dict. `degrees` maps a bone name to its XYZ Euler rotation
    in degrees, relative to the rest pose of the rig."""
    S = services()
    unknown = [b for b in degrees if b not in rig.pose.bones]
    if unknown:
        raise ValueError("the rig has no bones {:s}".format(", ".join(unknown)))
    view_layer = bpy.context.view_layer
    view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    try:
        S.RigService.set_pose_from_dict(rig, {
            "skeleton_type": rig.name, "bone_translations": {},
            "bone_rotations": {b: [math.radians(a) for a in r] for b, r in sorted(degrees.items())}})
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer.update()


def joints(body):
    """The joints of the posed body, in meters: joint name (such as "l-elbow") -> (x, y, z), the center of the
    vertex group "joint-<name>" of MPFB's base mesh. The joint cubes are helper geometry, so this reads the body with
    its helper mask turned off, before freeze removes the helpers."""
    masks = [m for m in body.modifiers if m.type == "MASK"]
    for m in masks:
        m.show_viewport = False
    try:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        mesh = body.evaluated_get(depsgraph).to_mesh()
        groups = {g.index: g.name[6:] for g in body.vertex_groups if g.name.startswith("joint-")}
        sums = {}
        for v in mesh.vertices:
            for g in v.groups:
                if g.group in groups and g.weight > 0.5:
                    total = sums.setdefault(groups[g.group], [0.0, 0.0, 0.0, 0])
                    co = body.matrix_world @ v.co
                    total[0] += co.x
                    total[1] += co.y
                    total[2] += co.z
                    total[3] += 1
        body.evaluated_get(depsgraph).to_mesh_clear()
    finally:
        for m in masks:
            m.show_viewport = True
    return {name: (x / n, y / n, z / n) for name, (x, y, z, n) in sorted(sums.items())}


def freeze(body):
    """Turns the human of `body` into plain static meshes and returns them by part name (PART_NAMES): the shape
    keys, the pose and the helper mask are applied, the rig is removed, and each mesh keeps only the vertex groups of
    the rig's bones. glTF then carries the human as it stands, and the importer, which ignores skins, sees the pose.

    Each part keeps MPFB's MakeSkin material, rewired as an image texture into one Principled BSDF, which glTF
    holds: the image gives the base color, and only the parts of ALPHA_PARTS keep its alpha, drawn blended."""
    rig = body.parent if body.parent is not None and body.parent.type == "ARMATURE" else None
    objects = [body] if rig is None else sorted(rig.children, key=lambda o: o.name)
    bones = set(b.name for b in rig.data.bones) if rig is not None else set()
    S = services()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    parts = {}
    for ob in objects:
        kind = S.ObjectService.get_object_type(ob)
        if ob.type != "MESH" or kind not in PART_NAMES:
            continue
        frozen = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph), preserve_all_data_layers=True,
                                                 depsgraph=depsgraph)
        old = ob.data
        ob.modifiers.clear()
        if ob.data.shape_keys is not None:
            ob.shape_key_clear()
        ob.data = frozen
        bpy.data.meshes.remove(old)
        for group in list(ob.vertex_groups):
            if group.name not in bones:
                ob.vertex_groups.remove(group)
        world = ob.matrix_world.copy()
        ob.parent = None
        ob.matrix_world = world
        name = PART_NAMES[kind]
        ob.name = name
        ob.data.name = name
        for material in ob.data.materials:
            _plain_material(material, "M_" + name, name in ALPHA_PARTS)
        if name in CLEAR_FACES_REMOVED:
            _remove_clear_faces(ob)
        parts[name] = ob
    if rig is not None:
        armature = rig.data
        bpy.data.objects.remove(rig)
        bpy.data.armatures.remove(armature)
    return parts


def _remove_clear_faces(ob):
    """Deletes the faces of `ob` whose texture alpha at the center of their UV face is below one half."""
    image = next(n for n in ob.data.materials[0].node_tree.nodes if n.bl_idname == "ShaderNodeTexImage").image
    width, height = image.size
    pixels = image.pixels[:]
    uv = ob.data.uv_layers.active.data
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    clear = []
    for face in bm.faces:
        loops = [uv[loop.index].uv for loop in face.loops]
        u = sum(c.x for c in loops) / len(loops) % 1.0
        v = sum(c.y for c in loops) / len(loops) % 1.0
        x, y = min(int(u * width), width - 1), min(int(v * height), height - 1)
        if pixels[(y * width + x) * 4 + 3] < 0.5:
            clear.append(face)
    bmesh.ops.delete(bm, geom=clear, context="FACES")
    bm.to_mesh(ob.data)
    bm.free()


def _plain_material(material, name, alpha):
    """Rewires a MakeSkin material: MPFB puts a Mix node of factor 1 between the image and the base color, and a Bump
    node fed by the image color on eyebrows. glTF holds neither, so the image feeds the base color directly."""
    material.name = name
    tree = material.node_tree
    principled = next(n for n in tree.nodes if n.bl_idname == "ShaderNodeBsdfPrincipled")
    image = next(n for n in tree.nodes if n.bl_idname == "ShaderNodeTexImage")
    for node in [n for n in tree.nodes if n.bl_idname in ("ShaderNodeMixRGB", "ShaderNodeBump")]:
        tree.nodes.remove(node)
    tree.links.new(image.outputs["Color"], principled.inputs["Base Color"])
    for link in list(principled.inputs["Alpha"].links):
        tree.links.remove(link)
    if alpha:
        tree.links.new(image.outputs["Alpha"], principled.inputs["Alpha"])
        material.surface_render_method = "BLENDED"
        material.use_backface_culling = False
    else:
        material.surface_render_method = "DITHERED"


def deserialization_settings(subdiv_levels=0):
    """MPFB's settings for HumanService.deserialize_from_dict, fixed for models: helpers masked, feet on the ground,
    meters, no subdivision unless asked, no clothes from the preset, and one material per asset."""
    return {
        "mask_helpers": True,
        "detailed_helpers": True,
        "extra_vertex_groups": True,
        "feet_on_ground": True,
        "scale": 0.1,
        "subdiv_levels": subdiv_levels,
        "load_clothes": False,
        "override_skin_model": "PRESET",
        "override_rig": "PRESET",
        "material_instances": "NEVER",
    }


def _check_environment():
    missing = [name for name in ("BLENDER_SYSTEM_EXTENSIONS", "TERTIUS_MAKEHUMAN_ASSETS", "BLENDER_USER_RESOURCES")
               if not os.environ.get(name)]
    if missing:
        raise MpfbError("MPFB needs {:s}. Run Blender through tools/blender/build.sh or tools/blender/bridge.sh, "
                        "which set them in the assets shell.".format(", ".join(missing)))


def _raise(ex):
    raise MpfbError("MPFB failed to load: {:s}".format(str(ex))) from ex
