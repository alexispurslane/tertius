"""Throwaway (t1-g4p1): builds a Cycles scene from the probe's scene export and renders a reference of the engine's view.

  blender -b --factory-startup --python cycles_scene.py -- <export folder> <out.exr> <samples> <seed> <device>
      [<x0> <y0> <x1> <y1>]

The export is `scene.json` and `mesh-<id>.bin` of SceneExport (the probe's patch `reference-switch-and-export.patch`):
the frame's placed instances, materials, lights and camera, camera-relative in the engine's world axes, in m. The
optional box is in output pixels of 1920 x 1080 from the top left, x1 and y1 excluded. The EXR holds the scene-linear
radiance in W/(m^2 sr), before the engine's exposure and tone curve. The device is CPU or METAL.

What the scene keeps of the engine, by construction:
- Every placed instance with its mesh, its vertex normals and its transform.
- The material model of lighting.glsl: Lambert diffuse with c_diff = b (1 - m), plus GGX with Schlick's Fresnel from
  F0 = (1 - m) 0.16 rho^2 + m b, alpha = roughness^2, roughness at least 0.045. Cycles' Metallic BSDF with the F82 tint at
  white is Schlick's Fresnel with multiple scattering.
- Each light in W/sr: a point or spot light of power 4 pi I, a rectangle light of power pi I, both one-sided as the
  engine's. A point light farther than 1e9 m is a sun lamp of strength I / d^2 and the angle of its sphere.
- An instance without CastsShadow blocks no shadow ray, and one without Visible no camera ray. A box around the eye is
  hidden from the camera, as the engine's culling of its back faces hides it.
- A glow of an entity that holds a light shows to camera rays only: the light itself brings that light, as in the engine.
- A glass pane shows to camera rays only, as a blend of its color at its alpha. Every other ray passes it, because the
  engine's glass casts no shadow and is not in the G-buffer.
- The world is black: the camera's air has no painted sky in both views.
"""
import json
import math
import os
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
folder = Path(argv[0])
out = argv[1]
samples = int(argv[2])
seed = int(argv[3])
device = argv[4]
box = list(map(int, argv[5:9])) if len(argv) >= 9 else None
W, H = 1920, 1080
FAR_LIGHT = 1e9
# The area-weighted mean of EarthGrid (DemoScenes.cs) in linear light: 86.5 % deep blue (35, 85, 170) and 13.5 % lines
# (190, 210, 240), sRGB. The export holds no texture coordinates, so Earth takes its mean.
EARTH_GRID_MEAN = (0.0840, 0.1655, 0.4655)

scene_json = json.loads((folder / "scene.json").read_text())
# surface() treats every textured material as Earth's. Refuse a scene where a textured material sits on an instance
# that is not celestial (RenderFlags.Celestial, 1 << 4), because its texture would become Earth's mean color.
CELESTIAL = 1 << 4
for inst in scene_json["instances"]:
    if scene_json["materials"][inst["material"]]["baseColorMap"] != 0 and not inst["flags"] & CELESTIAL:
        raise SystemExit(f"instance with mesh {inst['mesh']}: a textured material that is not Earth's")
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "GPU" if device == "METAL" else "CPU"
if device == "METAL":
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "METAL"
    prefs.refresh_devices()
    for d in prefs.devices:
        d.use = d.type == "METAL"
sc.cycles.samples = samples
sc.cycles.seed = seed
sc.cycles.use_adaptive_sampling = False
sc.cycles.use_denoising = False
sc.cycles.max_bounces = 32
sc.cycles.diffuse_bounces = 32
sc.cycles.glossy_bounces = 16
sc.cycles.transmission_bounces = 16
sc.cycles.transparent_max_bounces = 64
sc.cycles.sample_clamp_direct = 0
sc.cycles.sample_clamp_indirect = 0
sc.cycles.blur_glossy = 0
sc.cycles.caustics_reflective = True
sc.cycles.caustics_refractive = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
sc.render.image_settings.file_format = "OPEN_EXR"
sc.render.image_settings.color_depth = "32"
sc.render.image_settings.exr_codec = "ZIP"
sc.render.filepath = out
if box:
    x0, y0, x1, y1 = box
    sc.render.use_border = True
    sc.render.use_crop_to_border = True
    sc.render.border_min_x, sc.render.border_max_x = x0 / W, x1 / W
    sc.render.border_min_y, sc.render.border_max_y = 1 - y1 / H, 1 - y0 / H
world = bpy.data.worlds.new("black")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
sc.world = world

# The camera: the eye at the origin, Blender's camera looks along its -Z with +Y up.
cam_json = scene_json["camera"]
right, up, forward = (Vector(cam_json[k]) for k in ("right", "up", "forward"))
cam = bpy.data.cameras.new("camera")
cam.sensor_fit = "VERTICAL"
cam.angle_y = cam_json["verticalFov"]
cam.clip_start = 0.01
cam.clip_end = 1e12
cam_obj = bpy.data.objects.new("camera", cam)
sc.collection.objects.link(cam_obj)
rot = Matrix((right, up, -forward)).transposed()
cam_obj.matrix_world = rot.to_4x4()
sc.camera = cam_obj


def node_material(name, build):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    output = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(build(nt), output.inputs["Surface"])
    return m


# The sun: the point light farther than FAR_LIGHT, as its unit direction from the scene and its irradiance in W/m^2.
sun = None
skip_lights = {int(x) for x in os.environ.get("REF_SKIP_LIGHTS", "").split(",") if x}
for n, light in enumerate(scene_json["lights"]):
    if n not in skip_lights and light["kind"] == "point" and light["distance"] > FAR_LIGHT:
        sun = (Vector(light["position"]).normalized(), max(light["intensity"]) / light["distance"] ** 2)


def earth_glow(nt, albedo):
    """Earth as an emitter of its own Lambert reflection of the sun, albedo E max(n . s, 0) / pi, with no BSDF. A path
    tracer then samples Earth as a light. Without it, the light that Earth sends through the station's openings reaches
    the cabin only by paths that hit Earth at random, which leaves the image noisy at any sample count that fits."""
    direction, irradiance = sun
    geometry = nt.nodes.new("ShaderNodeNewGeometry")
    dot = nt.nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    dot.inputs[1].default_value = direction
    nt.links.new(geometry.outputs["Normal"], dot.inputs[0])
    lit = nt.nodes.new("ShaderNodeMath")
    lit.operation = "MAXIMUM"
    lit.inputs[1].default_value = 0.0
    nt.links.new(dot.outputs["Value"], lit.inputs[0])
    scale = nt.nodes.new("ShaderNodeMath")
    scale.operation = "MULTIPLY"
    scale.inputs[1].default_value = irradiance / math.pi
    nt.links.new(lit.outputs[0], scale.inputs[0])
    emission = nt.nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*albedo, 1)
    nt.links.new(scale.outputs[0], emission.inputs["Strength"])
    return emission.outputs[0]


def surface(nt, mat):
    """The engine's BRDF: Lambert plus Schlick GGX, added without layering."""
    b = mat["baseColor"][:3]
    if mat["baseColorMap"] != 0:
        b = [c * e for c, e in zip(b, EARTH_GRID_MEAN)]
        if os.environ.get("REF_EARTH_BLACK") == "1":
            black = nt.nodes.new("ShaderNodeBsdfDiffuse")
            black.inputs["Color"].default_value = (0, 0, 0, 1)
            return black.outputs[0]
        if sun is not None:
            return earth_glow(nt, b)
    m = mat["metallic"]
    rough = max(mat["roughness"], 0.045)
    rho = mat["reflectance"]
    diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
    diffuse.inputs["Color"].default_value = (*[c * (1 - m) for c in b], 1)
    diffuse.inputs["Roughness"].default_value = 0.0
    f0 = [(1 - m) * 0.16 * rho * rho + m * c for c in b]
    glossy = nt.nodes.new("ShaderNodeBsdfMetallic")
    glossy.fresnel_type = "F82"
    glossy.distribution = "MULTI_GGX"
    glossy.inputs["Base Color"].default_value = (*f0, 1)
    glossy.inputs["Edge Tint"].default_value = (1, 1, 1, 1)
    glossy.inputs["Roughness"].default_value = rough
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(diffuse.outputs[0], add.inputs[0])
    nt.links.new(glossy.outputs[0], add.inputs[1])
    return add.outputs[0]


def camera_only(nt, camera_shader, other_shader):
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs["Fac"])
    nt.links.new(other_shader, mix.inputs[1])
    nt.links.new(camera_shader, mix.inputs[2])
    return mix.outputs[0]


materials = {}


def material_for(slot, holds_light):
    key = (slot, holds_light)
    if key in materials:
        return materials[key]
    mat = scene_json["materials"][slot]
    glass = "Glass" in mat["alpha"]
    emissive = mat["emissive"]

    def build(nt):
        if glass:
            # Camera rays see the blend of the pane's color at its alpha over what lies behind it. Every other ray passes.
            transparent = nt.nodes.new("ShaderNodeBsdfTransparent")
            pane = nt.nodes.new("ShaderNodeMixShader")
            pane.inputs["Fac"].default_value = mat["baseColor"][3]
            nt.links.new(transparent.outputs[0], pane.inputs[1])
            nt.links.new(surface(nt, mat), pane.inputs[2])
            other = nt.nodes.new("ShaderNodeBsdfTransparent")
            return camera_only(nt, pane.outputs[0], other.outputs[0])
        shader = surface(nt, mat)
        if max(emissive) > 0:
            emission = nt.nodes.new("ShaderNodeEmission")
            emission.inputs["Color"].default_value = (*emissive, 1)
            emission.inputs["Strength"].default_value = 1.0
            add = nt.nodes.new("ShaderNodeAddShader")
            nt.links.new(shader, add.inputs[0])
            nt.links.new(emission.outputs[0], add.inputs[1])
            if holds_light:
                return camera_only(nt, add.outputs[0], shader)
            return add.outputs[0]
        return shader

    materials[key] = node_material(f"m{slot}{'L' if holds_light else ''}", build)
    return materials[key]


meshes = {}


def mesh_for(mesh_id):
    if mesh_id in meshes:
        return meshes[mesh_id]
    data = (folder / f"mesh-{mesh_id}.bin").read_bytes()
    nv, ni = struct.unpack_from("<ii", data, 0)
    off = 8
    pos = struct.unpack_from(f"<{3 * nv}f", data, off)
    off += 12 * nv
    nrm = struct.unpack_from(f"<{3 * nv}f", data, off)
    off += 12 * nv
    idx = struct.unpack_from(f"<{ni}I", data, off)
    me = bpy.data.meshes.new(f"mesh{mesh_id}")
    verts = [pos[3 * i:3 * i + 3] for i in range(nv)]
    faces = [idx[3 * t:3 * t + 3] for t in range(ni // 3)]
    me.from_pydata(verts, [], faces)
    me.validate(clean_customdata=False)
    me.shade_smooth()
    me.normals_split_custom_set_from_vertices([nrm[3 * i:3 * i + 3] for i in range(nv)])
    meshes[mesh_id] = (me, verts)
    return meshes[mesh_id]


# REF_BLACK=<instance indices> (t1-3j2j): those instances reflect nothing, a black diffuse surface, and still block light.
black_instances = {int(x) for x in os.environ.get("REF_BLACK", "").split(",") if x}


def black_material():
    def build(nt):
        black = nt.nodes.new("ShaderNodeBsdfDiffuse")
        black.inputs["Color"].default_value = (0, 0, 0, 1)
        return black.outputs[0]
    return node_material("black", build)


log = []
for n, inst in enumerate(scene_json["instances"]):
    position = Vector(inst["position"])
    if position.length > FAR_LIGHT:
        log.append(f"instance {n} (mesh {inst['mesh']}) skipped: {position.length:.3g} m away, the sun lamp stands in")
        continue
    me, verts = mesh_for(inst["mesh"])
    obj = bpy.data.objects.new(f"i{n}", me)
    sc.collection.objects.link(obj)
    x, y, z, w = inst["rotation"]
    obj.matrix_world = (Matrix.Translation(position) @ Quaternion((w, x, y, z)).to_matrix().to_4x4()
                        @ Matrix.Diagonal((*inst["scale"], 1)))
    if n in black_instances:
        obj.data.materials.append(black_material())
        log.append(f"instance {n} (mesh {inst['mesh']}) drawn black by REF_BLACK")
    else:
        obj.data.materials.append(material_for(inst["material"], inst["holdsLight"]))
    flags = inst["flags"]
    if not flags & 1:
        obj.visible_camera = False
    if not flags & 2:
        obj.visible_shadow = False
    # A closed box around the eye: the engine culls its back faces, so the camera does not see it.
    if len(verts) == 24:
        local = obj.matrix_world.inverted() @ Vector((0, 0, 0))
        lo = [min(v[k] for v in verts) for k in range(3)]
        hi = [max(v[k] for v in verts) for k in range(3)]
        if all(lo[k] < local[k] < hi[k] for k in range(3)):
            obj.visible_camera = False
            log.append(f"instance {n} (mesh {inst['mesh']}) holds the eye: hidden from the camera")

for n, light in enumerate(scene_json["lights"]):
    if n in skip_lights:
        log.append(f"light {n} skipped by REF_SKIP_LIGHTS")
        continue
    intensity = light["intensity"]
    peak = max(intensity)
    if peak <= 0:
        continue
    color = [c / peak for c in intensity]
    position = Vector(light["position"])
    if light["kind"] == "point" and light["distance"] > FAR_LIGHT:
        data = bpy.data.lights.new(f"l{n}", "SUN")
        data.energy = peak / light["distance"] ** 2
        data.angle = 2 * math.asin(min(light["radius"] / light["distance"], 1))
        obj = bpy.data.objects.new(f"l{n}", data)
        obj.matrix_world = (-position.normalized()).to_track_quat("-Z", "Y").to_matrix().to_4x4()
        log.append(f"light {n}: sun of {data.energy:.1f} W/m^2 toward {tuple(round(c, 4) for c in -position.normalized())}")
    elif light["kind"] == "point":
        data = bpy.data.lights.new(f"l{n}", "POINT")
        data.energy = 4 * math.pi * peak
        data.shadow_soft_size = light["radius"]
        obj = bpy.data.objects.new(f"l{n}", data)
        obj.location = position
    elif light["kind"] == "rect":
        hw, hh = Vector(light["halfWidth"]), Vector(light["halfHeight"])
        data = bpy.data.lights.new(f"l{n}", "AREA")
        data.shape = "RECTANGLE"
        data.size, data.size_y = 2 * hw.length, 2 * hh.length
        data.energy = math.pi * peak
        obj = bpy.data.objects.new(f"l{n}", data)
        # The engine's face emits along hw x hh, Blender's area light along its -Z.
        front = hw.cross(hh).normalized()
        axes = Matrix((hw.normalized(), -hh.normalized(), -front)).transposed()
        obj.matrix_world = Matrix.Translation(position) @ axes.to_4x4()
    elif light["kind"] == "spot":
        data = bpy.data.lights.new(f"l{n}", "SPOT")
        data.energy = 4 * math.pi * peak
        data.shadow_soft_size = light["radius"]
        data.spot_size = 2 * light["outerAngle"]
        inner = math.acos(min(light["cosInner"], 1))
        data.spot_blend = max(0.0, min(1.0, 1 - inner / light["outerAngle"]))
        obj = bpy.data.objects.new(f"l{n}", data)
        obj.matrix_world = Matrix.Translation(position) @ Vector(light["axis"]).to_track_quat("-Z", "Y").to_matrix().to_4x4()
    else:
        raise SystemExit(f"light {n}: kind {light['kind']} is not built")
    data.color = color
    data.use_shadow = light["castsShadow"]
    sc.collection.objects.link(obj)

# REF_SUN_CAP=<x0,x1,y,z0,z1> (t1-3j2j): a horizontal rectangle at height y, camera-relative in m, that only shadow rays
# see. Above the hull's roof it keeps the sun from every surface of the hull, outside and in, and from nothing else.
# Several rectangles go apart by semicolons.
for rect in filter(None, os.environ.get("REF_SUN_CAP", "").split(";")):
    x0c, x1c, yc, z0c, z1c = map(float, rect.split(","))
    cap = bpy.data.meshes.new("cap")
    cap.from_pydata([(x0c, yc, z0c), (x1c, yc, z0c), (x1c, yc, z1c), (x0c, yc, z1c)], [], [(0, 1, 2, 3)])
    cap_obj = bpy.data.objects.new("cap", cap)
    sc.collection.objects.link(cap_obj)
    cap_obj.visible_camera = cap_obj.visible_diffuse = cap_obj.visible_glossy = False
    cap_obj.visible_transmission = cap_obj.visible_volume_scatter = False
    cap_obj.visible_shadow = True
    log.append(f"sun cap at y {yc} over x {x0c}..{x1c}, z {z0c}..{z1c}")

# REF_OVERRIDE=normal or position: every surface emits its geometric normal or its position in m, to camera rays, with
# one sample and a pixel filter of a hundredth of a pixel. The images name each pixel's face for the regions.
override = os.environ.get("REF_OVERRIDE")
if override:
    def build_override(nt):
        geometry = nt.nodes.new("ShaderNodeNewGeometry")
        emission = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(geometry.outputs["Normal" if override == "normal" else "Position"], emission.inputs["Color"])
        return emission.outputs[0]
    sc.view_layers[0].material_override = node_material("override", build_override)
    sc.cycles.samples = 1
    sc.cycles.filter_width = 0.01
    sc.cycles.max_bounces = 0
    for ob in list(bpy.data.objects):
        if ob.type == "LIGHT":
            bpy.data.objects.remove(ob)
    log.append(f"override {override}")

for line in log:
    print("SCENE", line)
print("SCENE", len(scene_json["instances"]), "instances,", len(meshes), "meshes,", len(scene_json["lights"]), "lights")
bpy.ops.render.render(write_still=True)
print("SCENE wrote", out)
