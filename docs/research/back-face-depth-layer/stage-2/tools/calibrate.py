"""Throwaway (t1-g4p1): the calibration of the light units in Cycles. It lights a white Lambert plate from 1 m with
each kind of light, at 1 W/sr (point and spot, power 4 pi), 1 W/sr of a 5 cm rectangle (power pi) and 1 W/m^2 (sun), and
prints the plate's mean radiance. The exact value is 1/pi.

  $TERTIUS_BLENDER -b --factory-startup --python calibrate.py

It writes one EXR per light into /private/tmp/t1-g4p1/cal.
"""
import bpy, math, numpy as np
sc = bpy.context.scene
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 256; sc.cycles.use_denoising = False
sc.world = bpy.data.worlds.new("w"); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0
sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x = 16; sc.render.resolution_y = 16
bpy.ops.mesh.primitive_plane_add(size=0.02)
pl = bpy.context.object
mat = bpy.data.materials.new("m"); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear()
d = nt.nodes.new('ShaderNodeBsdfDiffuse'); d.inputs['Color'].default_value = (1,1,1,1)
o = nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(d.outputs[0], o.inputs[0])
pl.data.materials.append(mat)
cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = 0.01
co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); co.location = (0,0,0.5); sc.camera = co
def measure(kind):
    for ob in [x for x in bpy.data.objects if x.type == 'LIGHT']: bpy.data.objects.remove(ob)
    if kind == 'point':
        l = bpy.data.lights.new("l", 'POINT'); l.energy = 4*math.pi; l.shadow_soft_size = 0
    elif kind == 'spot':
        l = bpy.data.lights.new("l", 'SPOT'); l.energy = 4*math.pi; l.shadow_soft_size = 0; l.spot_size = 1.0; l.spot_blend = 0
    elif kind == 'area':
        l = bpy.data.lights.new("l", 'AREA'); l.shape = 'RECTANGLE'; l.size = 0.05; l.size_y = 0.05; l.energy = math.pi
    elif kind == 'sun':
        l = bpy.data.lights.new("l", 'SUN'); l.energy = 1.0; l.angle = 0
    lo = bpy.data.objects.new("l", l); sc.collection.objects.link(lo); lo.location = (0,0,1.0)
    sc.render.filepath = f"/private/tmp/t1-g4p1/cal/{kind}.exr"
    sc.render.image_settings.file_format = 'OPEN_EXR'
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(sc.render.filepath)
    px = np.array(img.pixels[:]).reshape(-1,4)
    print("CAL", kind, px[:, :3].mean(), "expected I=1 W/sr or E=1 at 1 m: L = 1/pi =", 1/math.pi)
for k in ['point','spot','area','sun']: measure(k)
