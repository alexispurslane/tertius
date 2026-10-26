# Procedural materials. The build bakes the hull and engine materials into texture maps (bake.objects of
# model.json). The small parts keep materials with constant inputs, which glTF holds as they are.
# Each baked material has one Principled BSDF with a constant metallic input, and a Bump node into its normal
# input: the bake contract of the asset pipeline. The materials read object coordinates, in the build frame.
import bpy

from tertius_blender.nodes import NB


def fresh(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.node_tree.nodes.clear()
    return m


def principled_out(b, base, rough, metallic, normal=None):
    """The Principled BSDF and the output. The bake reads what feeds the base color and roughness inputs."""
    p = b.n("ShaderNodeBsdfPrincipled", {"Base Color": base, "Roughness": rough, "Metallic": metallic})
    if normal is not None:
        b.set(p.inputs["Normal"], normal)
    o = b.n("ShaderNodeOutputMaterial", {"Surface": p.outputs[0]})
    o.is_active_output = True
    return p


def projected(b, P, N, fn):
    """Box projection: evaluate fn(u, v) in the plane of each face's dominant axis and blend.
    X faces use (y, z), Y faces use (x, z), Z faces use (y, x). fn returns a dict of floats."""
    px, py, pz = b.sep(P)
    ax, ay, az = b.sep(b.vmath("ABSOLUTE", N))
    mx = b.math("MULTIPLY", b.math("GREATER_THAN", ax, ay), b.math("GREATER_THAN", ax, az))
    my = b.math("MULTIPLY", b.math("GREATER_THAN", ay, ax), b.math("GREATER_THAN", ay, az))
    mz = b.math("SUBTRACT", 1.0, b.math("ADD", mx, my), clamp=True)
    rx = fn(b.comb(py, pz, 0.0))
    ry = fn(b.comb(px, pz, 0.0))
    rz = fn(b.comb(py, px, 0.0))
    out = {}
    for k in rx:
        out[k] = b.math("ADD", b.math("ADD", b.math("MULTIPLY", rx[k], mx), b.math("MULTIPLY", ry[k], my)),
                        b.math("MULTIPLY", rz[k], mz))
    return out


def panels(b, vec):
    seam, rnd = b.brick(vec, 4.8, 2.4, 0.10, 0.45)
    seam_big, rnd_big = b.brick(vec, 14.4, 7.2, 0.18, 0.35, offset=0.25)
    band, _ = b.brick(vec, 4.8, 2.4, 0.9, 0.0)
    dots, _ = b.brick(vec, 0.6, 0.6, 0.4, 0.7, offset=0.0)
    rivet = b.math("MULTIPLY", b.math("MULTIPLY", band, b.math("SUBTRACT", 1.0, dots)),
                   b.math("SUBTRACT", 1.0, seam))
    # Hatch-like inset plates on a few panels: a smaller rectangle inside each 4.8 x 2.4 panel.
    inset, _ = b.brick(vec, 2.4, 1.2, 0.10, 0.4, offset=0.0)
    return {"seam": seam, "seam_big": seam_big, "rivet": rivet, "rnd": rnd, "rnd_big": rnd_big,
            "band": band, "inset": inset}


def hull_material(name, hazard_boxes, noise_offset):
    m = fresh(name)
    b = NB(m.node_tree)
    tc = b.n("ShaderNodeTexCoord")
    P = tc.outputs["Object"]
    N = tc.outputs["Normal"]
    Pn = b.vmath("ADD", P, noise_offset)
    px, py, pz = b.sep(P)
    pp = projected(b, P, N, lambda v: panels(b, v))
    seam, seam_big, rivet = pp["seam"], pp["seam_big"], pp["rivet"]
    rnd, rnd_big = pp["rnd"], pp["rnd_big"]
    # Inset plates only on panels whose random value is high.
    inset_on = b.math("GREATER_THAN", rnd, 0.78)
    inset = b.math("MULTIPLY", pp["inset"], inset_on)

    # --- Paint and panel variation --------------------------------------------
    paint = (0.56, 0.545, 0.50)
    c = b.mixc(b.math("MULTIPLY", rnd, 0.55), paint, (0.47, 0.455, 0.41))
    gray_on = b.math("GREATER_THAN", rnd_big, 0.86)
    c = b.mixc(gray_on, c, (0.31, 0.31, 0.32))
    yel = b.math("MULTIPLY", b.math("GREATER_THAN", rnd, 0.93), b.math("SUBTRACT", 1.0, gray_on))
    c = b.mixc(b.math("MULTIPLY", yel, 0.75), c, (0.60, 0.50, 0.33))

    # --- Grime ----------------------------------------------------------------
    g1 = b.maprange(b.noise(Pn, 0.035, 6.0, 0.62), 0.47, 0.72)
    streak_vec = b.vmath("MULTIPLY", Pn, (0.45, 0.45, 0.035))
    g2 = b.maprange(b.noise(streak_vec, 1.0, 4.0, 0.55), 0.52, 0.78)
    speck = b.maprange(b.noise(Pn, 3.0, 3.0, 0.7), 0.58, 0.72)
    ao = b.n("ShaderNodeAmbientOcclusion", {"Distance": 2.5}, samples=16, inside=False, only_local=False)
    cav = b.maprange(ao.outputs["AO"], 0.95, 0.35)
    grime = b.math("MAXIMUM", b.math("MULTIPLY", g1, 0.75), b.math("MULTIPLY", g2, 0.5))
    grime = b.math("ADD", grime, b.math("MULTIPLY", seam, 0.3))
    grime = b.math("ADD", grime, b.math("MULTIPLY", seam_big, 0.3))
    grime = b.math("ADD", grime, b.math("MULTIPLY", cav, 0.65))
    grime = b.math("ADD", grime, b.math("MULTIPLY", speck, 0.18), clamp=True)
    dirt = (0.16, 0.14, 0.11)
    col = b.mixc(grime, c, dirt)

    # --- Hazard stripes ---------------------------------------------------------
    haz = 0.0
    for x0, x1, y0, y1, z0, z1 in hazard_boxes:
        mk = b.math("MULTIPLY", b.math("MULTIPLY", b.inrange(px, x0, x1), b.inrange(py, y0, y1)),
                    b.inrange(pz, z0, z1))
        haz = mk if haz == 0.0 else b.math("MAXIMUM", haz, mk)
    diag = b.math("ADD", b.math("ADD", px, py), pz)
    stripe = b.math("GREATER_THAN", b.math("FRACT", b.math("MULTIPLY", diag, 1.0 / 2.6)), 0.5)
    hazcol = b.mixc(stripe, (0.78, 0.56, 0.04), (0.03, 0.03, 0.03))
    hazcol = b.mixc(b.math("MULTIPLY", grime, 0.7), hazcol, dirt)
    col = b.mixc(haz, col, hazcol)

    # --- Roughness ----------------------------------------------------------------
    r = b.maprange(rnd, 0.0, 1.0, 0.40, 0.56)
    r = b.mixf(gray_on, r, 0.5)
    r = b.mixf(grime, r, 0.84)
    r = b.mixf(haz, r, 0.42)

    # --- Height for the bump: panel steps, seams, rivets, inset plates, paint texture ----
    h = b.math("MULTIPLY", rnd, 0.15)
    h = b.math("SUBTRACT", h, b.math("MULTIPLY", seam, 0.7))
    h = b.math("SUBTRACT", h, b.math("MULTIPLY", seam_big, 1.0))
    h = b.math("ADD", h, b.math("MULTIPLY", rivet, 0.55))
    h = b.math("SUBTRACT", h, b.math("MULTIPLY", inset, 0.45))
    h = b.math("ADD", h, b.math("MULTIPLY", b.noise(Pn, 6.0, 2.0, 0.5), 0.05))
    bump = b.n("ShaderNodeBump", {"Height": h, "Strength": 1.0, "Distance": 0.045})
    principled_out(b, col, r, 0.0, bump.outputs["Normal"])
    return m


def engine_material(name):
    m = fresh(name)
    b = NB(m.node_tree)
    tc = b.n("ShaderNodeTexCoord")
    P = tc.outputs["Object"]
    px, py, pz = b.sep(P)
    base = (0.055, 0.052, 0.055)
    n1 = b.noise(P, 0.6, 6.0, 0.6)
    c = b.mixc(b.maprange(n1, 0.4, 0.7), base, (0.09, 0.085, 0.08))
    # Heat tint near the throats (y from -105 to -112) fading toward the exits.
    heat = b.maprange(py, -105.0, -113.0, 1.0, 0.0, "SMOOTHSTEP")
    heat = b.math("MULTIPLY", heat, b.maprange(py, -104.0, -106.0, 0.0, 1.0, "SMOOTHSTEP"))
    c = b.mixc(b.math("MULTIPLY", heat, 0.8), c, (0.22, 0.12, 0.06))
    blue = b.maprange(py, -112.0, -116.0, 0.0, 1.0, "SMOOTHSTEP")
    blue = b.math("MULTIPLY", blue, b.maprange(py, -117.0, -121.0, 1.0, 0.0, "SMOOTHSTEP"))
    c = b.mixc(b.math("MULTIPLY", blue, 0.6), c, (0.06, 0.07, 0.13))
    # Soot toward the bell lips.
    soot = b.maprange(py, -116.0, -122.0, 0.0, 1.0)
    c = b.mixc(b.math("MULTIPLY", soot, 0.7), c, (0.015, 0.015, 0.015))
    r = b.maprange(b.noise(P, 1.5, 4.0, 0.6), 0.3, 0.7, 0.28, 0.55)
    r = b.mixf(soot, r, 0.75)
    # Circumferential cooling-channel ridges: bands along Y.
    wave = b.n("ShaderNodeTexWave", {"Vector": P, "Scale": 1.0, "Distortion": 0.0, "Detail": 0.0},
               wave_type="BANDS", bands_direction="Y", wave_profile="SIN")
    wv = b.n("ShaderNodeVectorMath", {0: P, 1: (1.0, 4.0, 1.0)}, operation="MULTIPLY")
    b.set(wave.inputs["Vector"], wv.outputs[0])
    h = b.math("MULTIPLY", wave.outputs["Fac"], 0.5)
    h = b.math("ADD", h, b.math("MULTIPLY", b.noise(P, 8.0, 3.0, 0.6), 0.25))
    bump = b.n("ShaderNodeBump", {"Height": h, "Strength": 0.6, "Distance": 0.03})
    principled_out(b, c, r, 0.85, bump.outputs["Normal"])
    return m


def factor_material(name, color, metallic, roughness):
    m = fresh(name)
    b = NB(m.node_tree)
    p = b.n("ShaderNodeBsdfPrincipled", {"Base Color": color, "Metallic": metallic, "Roughness": roughness})
    b.n("ShaderNodeOutputMaterial", {"Surface": p.outputs[0]})
    return m


def emissive_material(name, color, strength):
    m = fresh(name)
    b = NB(m.node_tree)
    p = b.n("ShaderNodeBsdfPrincipled", {"Base Color": (0.02, 0.02, 0.02), "Roughness": 0.3,
                                         "Emission Color": color, "Emission Strength": strength})
    b.n("ShaderNodeOutputMaterial", {"Surface": p.outputs[0]})
    return m


hull_material("M_Hull_Fore", [(-30, 30, 95.4, 97.4, -30, 30)], (11.0, 3.0, 7.0))
hull_material("M_Hull_Mid", [(-7.6, 7.6, -46, 46, -30, -13.85)], (0.0, 0.0, 0.0))
hull_material("M_Hull_Aft", [(-40, 40, -59.6, -57.6, -40, 40), (-40, 40, -104, -100.3, -40, 40)],
              (-5.0, 9.0, 2.0))
engine_material("M_Engine")
factor_material("M_FrameMetal", (0.045, 0.045, 0.05), 0.85, 0.5)
factor_material("M_PipeMetal", (0.20, 0.19, 0.17), 0.9, 0.35)
factor_material("M_PaintLight", (0.52, 0.51, 0.47), 0.0, 0.58)
factor_material("M_PaintGray", (0.24, 0.24, 0.25), 0.0, 0.55)
emissive_material("M_Window", (1.0, 0.78, 0.5), 8.0)
emissive_material("M_NavRed", (1.0, 0.05, 0.03), 60.0)
emissive_material("M_NavGreen", (0.1, 1.0, 0.2), 60.0)
emissive_material("M_NavWhite", (1.0, 1.0, 1.0), 80.0)
emissive_material("M_EngineGlow", (1.0, 0.42, 0.12), 12.0)
result = {m.name: len(m.node_tree.nodes) for m in bpy.data.materials}
