"""Tool code. The finish of a model's diffuse and PBR maps: procedural changes with fixed parameters and seeds, which the
sidecar's "finish" block names (tools/README.md, "The Blue Danube art"). The geometry stays as it is.

The finish works in the texture's own pixels. It first rasterizes every triangle of the material into the texture's UV
space, so each texel knows the point of the model that it colors, in the source's axes, and the surface normal there.
Many texels color two places, such as both sides of a mirrored hull. The finish then:

1. pales the hull: it desaturates and lifts every light texel, and leaves dark details and glowing texels alone;
2. clears painted window glow on real cut openings when configured, so cut window frames carry no yellow artifact;
3. draws wear: grime near hatches and gear bays, heat tint near nozzles, each a falloff about a point in the source's
   axes times value noise with a fixed seed; a "mirrorX" spot acts on both sides, so a shared texel gets the same wear;
4. draws markings: hazard stripes in a ring about a hatch, and text in a 5 by 7 pixel font, projected from a plane in
   the source's axes; markings go only on texels that color one place, so no text shows mirrored;
5. when "pbr" is enabled, generates spatially varied MetallicRoughness (G=roughness, B=metallic) and tangent-space
   Normal maps tied to real material zones (painted ceramic hull, exposed bare metal gear/nozzles/trims, radiator
   louvers, elastomer seals, wear grime, 3D aerospace skin joins, fastener rows, and detected source panel seams)."""

import math

import bpy
import numpy as np

import glow

# The 5 by 7 font of the markings, top row first.
FONT = {
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    " ": ["00000"] * 7,
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "D": ["11100", "10010", "10001", "10001", "10001", "10010", "11100"],
}


def apply(config, pieces, sources):
    """Applies the finish `config` to the diffuse (and optional PBR) maps of its material on `pieces`."""
    material = bpy.data.materials[config["material"]]
    node = _base_color_node(material)
    source_rgba = glow.pixels(node.image).copy()
    rgba = source_rgba.copy()
    height, width = rgba.shape[:2]
    position, normal, uses = _rasterize(pieces, material, width, height)
    glow_luminance = _glow_luminance(material, width, height)
    report = {"texels": int((uses > 0).sum()), "sharedTexels": int((uses > 1).sum())}
    report["paled"] = _pale(rgba, config["pale"], glow_luminance)
    if config.get("clearWindowGlow"):
        report["clearedWindowGlow"] = _clear_window_glow(material, rgba)
    grime_mask = np.zeros((height, width), dtype=np.float32)
    heat_mask = np.zeros((height, width), dtype=np.float32)
    report["wear"] = [_wear(rgba, spot, position, uses, grime_mask, heat_mask) for spot in config.get("wear", [])]
    marking_mask = np.zeros((height, width), dtype=bool)
    report["markings"] = [
        _mark(rgba, mark, position, normal, uses, marking_mask) for mark in config.get("markings", [])
    ]
    node.image = glow.new_image(node.image.name + ".finished", rgba)
    if config.get("pbr"):
        report["pbr"] = _apply_pbr(
            material=material,
            source_rgba=source_rgba,
            position=position,
            normal=normal,
            uses=uses,
            grime_mask=grime_mask,
            heat_mask=heat_mask,
            marking_mask=marking_mask,
        )
    return report


def _base_color_node(material):
    bsdf = material.node_tree.nodes["Principled BSDF"]
    return bsdf.inputs["Base Color"].links[0].from_node


def _glow_luminance(material, width, height):
    """The glow map's luminance at the diffuse map's size, or zeros when the material has no glow."""
    if "tertius_glow" not in material:
        return np.zeros((height, width))
    glow_rgba = glow.pixels(bpy.data.images[material["tertius_glow"]])
    rows = (np.arange(height) * glow_rgba.shape[0] // height)[:, None]
    columns = (np.arange(width) * glow_rgba.shape[1] // width)[None, :]
    return glow_rgba[rows, columns, :3] @ np.array([0.2126, 0.7152, 0.0722])


def _clear_window_glow(material, rgba):
    """Clears painted yellow window glow on the cockpit and passenger window regions, replacing the fake yellow window
    paint with dark anodized frame charcoal so cut window bezels carry no yellow bleed."""
    if "tertius_glow" not in material:
        return 0
    glow_img = bpy.data.images[material["tertius_glow"]]
    g_rgba = glow.pixels(glow_img).copy()
    gh, gw = g_rgba.shape[:2]
    # Yellow window glow has high R and G and low B; keep orange/red radiator vents (R >> G) and white lamps (high B).
    g_rgb = g_rgba[..., :3]
    yellow_glow = (g_rgb[..., 0] > 0.12) & (g_rgb[..., 1] > 0.12) & (g_rgb[..., 2] < 0.22) & (
        g_rgb[..., 1] > 0.65 * g_rgb[..., 0]
    )
    g_rgba[yellow_glow, :3] = 0.0
    clean_glow = glow.new_image(glow_img.name + ".nowindows", g_rgba)
    material["tertius_glow"] = clean_glow.name

    # Also darken the corresponding yellow window paint in the diffuse map so frame edges read as dark metal bezels.
    dh, dw = rgba.shape[:2]
    rows = (np.arange(dh) * gh // dh)[:, None]
    cols = (np.arange(dw) * gw // dw)[None, :]
    yellow_diff = yellow_glow[rows, cols]
    # Dilate by 2 texels to catch yellow mip/compression fringes right on window bezel edges.
    dilated = yellow_diff.copy()
    for dy in (-2, -1, 0, 1, 2):
        for dx in (-2, -1, 0, 1, 2):
            dilated |= np.roll(np.roll(yellow_diff, dy, axis=0), dx, axis=1)
    rgb = rgba[..., :3]
    yellow_tint = (rgb[..., 0] > 0.35) & (rgb[..., 1] > 0.35) & (rgb[..., 2] < 0.30) & dilated
    rgb[yellow_tint] = np.array([0.11, 0.12, 0.14], dtype=np.float32)
    return int(yellow_glow.sum())


def _rasterize(pieces, material, width, height):
    """For each texel: the point that it colors (the last triangle's, in the source's axes), the face normal there, and
    how many triangles cover it."""
    position = np.zeros((height, width, 3))
    normal = np.zeros((height, width, 3))
    uses = np.zeros((height, width), dtype=np.int32)
    for obj in pieces.values():
        mesh = obj.data
        slot = next((i for i, m in enumerate(mesh.materials) if m == material), None)
        if slot is None:
            continue
        mesh.calc_loop_triangles()
        world = np.array(obj.matrix_world)
        uv_layer = mesh.uv_layers.active.data
        for triangle in mesh.loop_triangles:
            if triangle.material_index != slot:
                continue
            corners = np.array([world[:3, :3] @ np.array(mesh.vertices[v].co) + world[:3, 3] for v in triangle.vertices])
            uv = np.array([uv_layer[loop].uv for loop in triangle.loops]) * (width, height)
            face = np.cross(corners[1] - corners[0], corners[2] - corners[0])
            length = np.linalg.norm(face)
            if length == 0:
                continue
            _fill(position, normal, uses, uv, corners, face / length)
    return position, normal, uses


def _fill(position, normal, uses, uv, corners, face):
    height, width = uses.shape
    low = np.floor(uv.min(axis=0)).astype(int)
    high = np.ceil(uv.max(axis=0)).astype(int)
    xs = np.arange(max(low[0], 0), min(high[0], width))
    ys = np.arange(max(low[1], 0), min(high[1], height))
    if len(xs) == 0 or len(ys) == 0:
        return
    gx, gy = np.meshgrid(xs + 0.5, ys + 0.5)
    a, b, c = uv
    det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    if abs(det) < 1e-12:
        return
    w0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / det
    w1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / det
    w2 = 1 - w0 - w1
    inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
    rows, columns = gy[inside].astype(int), gx[inside].astype(int)
    point = w0[inside, None] * corners[0] + w1[inside, None] * corners[1] + w2[inside, None] * corners[2]
    position[rows, columns] = point
    normal[rows, columns] = face
    uses[rows, columns] += 1


def _luminance(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722])


def _smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _pale(rgba, config, glow_luminance):
    """Desaturates and lifts the light texels. Texels darker than keepBelow and glowing texels keep their color."""
    rgb = rgba[..., :3]
    luminance = _luminance(rgb)
    light = _smoothstep(config["keepBelow"], config["keepBelow"] + 0.2, luminance)
    light = light * (glow_luminance < config.get("glowThreshold", 0.05))
    gray = luminance[..., None]
    rgb[:] = rgb + (gray - rgb) * (config["desaturate"] * light)[..., None]
    rgb[:] = 1 - (1 - rgb) * (1 - config["lift"] * light)[..., None]
    return int((light > 0.5).sum())


def _noise(shape, seed, cells):
    """Value noise in [0, 1]: a grid of random values every `cells` pixels with a fixed seed, smoothly interpolated."""
    height, width = shape
    rng = np.random.default_rng(seed)
    grid = rng.random((height // cells + 2, width // cells + 2))
    y = np.arange(height) / cells
    x = np.arange(width) / cells
    y0, x0 = np.floor(y).astype(int), np.floor(x).astype(int)
    ty, tx = _smoothstep(0, 1, y - y0)[:, None], _smoothstep(0, 1, x - x0)[None, :]
    top = grid[y0][:, x0] * (1 - tx) + grid[y0][:, x0 + 1] * tx
    bottom = grid[y0 + 1][:, x0] * (1 - tx) + grid[y0 + 1][:, x0 + 1] * tx
    return top * (1 - ty) + bottom * ty


def _wear(rgba, spot, position, uses, grime_mask, heat_mask):
    """One spot of wear: grime ("service") or heat tint ("heat") about a point, faded out at its radius, broken up by
    noise of two scales with the spot's seed."""
    point = position.copy()
    if spot.get("mirrorX"):
        point[..., 0] = np.abs(point[..., 0])
    distance = np.linalg.norm(point - np.array(spot["center"]), axis=-1)
    falloff = np.clip(1 - distance / spot["radius"], 0, 1) ** 1.5
    shape = uses.shape
    noise = 0.65 * _noise(shape, spot["seed"], 48) + 0.35 * _noise(shape, spot["seed"] + 1, 9)
    mask = falloff * _smoothstep(0.35, 0.75, noise) * spot["strength"] * (uses > 0)
    rgb = rgba[..., :3]
    if spot["kind"] == "service":
        grime = np.array([0.24, 0.21, 0.17])
        rgb[:] = rgb + (grime * rgb * 2.2 - rgb) * mask[..., None]
        np.maximum(grime_mask, mask, out=grime_mask)
    elif spot["kind"] == "heat":
        # Soot at the nozzle, a bronze and blue tint farther out.
        tint = np.where((falloff > 0.55)[..., None], np.array([0.13, 0.12, 0.11]), np.array([0.42, 0.40, 0.52]))
        rgb[:] = rgb + (tint - rgb) * mask[..., None]
        np.maximum(heat_mask, mask, out=heat_mask)
    else:
        raise ValueError(f"unknown wear kind {spot['kind']!r}")
    return {"kind": spot["kind"], "texels": int((mask > 0.05).sum())}


def _mark(rgba, mark, position, normal, uses, marking_mask):
    """One marking, drawn only on texels that color one place and that face the marking's normal."""
    center = np.array(mark["center"], dtype=float)
    facing = normal @ np.array(mark["normal"], dtype=float) > 0.6
    near = np.abs((position - center) @ np.array(mark["normal"], dtype=float)) < mark.get("depth", 0.6)
    usable = (uses == 1) & facing & near
    rgb = rgba[..., :3]
    if mark["kind"] == "hazardRing":
        offset = position - center
        radius = np.linalg.norm(offset[..., :2], axis=-1)
        ring = usable & (radius >= mark["inner"]) & (radius <= mark["outer"])
        angle = np.arctan2(offset[..., 1], offset[..., 0])
        stripe = np.floor((angle * (mark["inner"] + mark["outer"]) / 2 + radius) / mark["stripe"]).astype(int) % 2 == 0
        rgb[ring & stripe] = np.array(mark["colors"][0])
        rgb[ring & ~stripe] = np.array(mark["colors"][1])
        marking_mask |= ring
        return {"kind": "hazardRing", "texels": int(ring.sum())}
    if mark["kind"] == "text":
        right = np.array(mark["right"], dtype=float)
        up = np.array(mark["up"], dtype=float)
        glyph_height = mark["height"] / 7
        s = ((position - center) @ right) / glyph_height
        t = ((position - center) @ up) / glyph_height
        text = mark["text"]
        width = len(text) * 6 - 1
        column = np.floor(s + width / 2).astype(int)
        row = np.floor(3.5 - t).astype(int)
        inside = usable & (column >= 0) & (column < width) & (row >= 0) & (row < 7) & (column % 6 != 5)
        bitmap = np.array([[[int(bit) for bit in line] for line in FONT[ch]] for ch in text])
        ink = np.zeros(inside.shape, dtype=bool)
        ink[inside] = bitmap[column[inside] // 6, row[inside], column[inside] % 6] == 1
        rgb[ink] = np.array(mark["color"])
        marking_mask |= ink
        return {"kind": "text", "text": text, "texels": int(ink.sum())}
    raise ValueError(f"unknown marking kind {mark['kind']!r}")


def _box_blur(arr, radius=2):
    """Separable box filter for extracting local panel-line contrast without scipy."""
    out = np.zeros_like(arr, dtype=np.float32)
    count = 2 * radius + 1
    for d in range(-radius, radius + 1):
        out += np.roll(arr, d, axis=1)
    out /= count
    res = np.zeros_like(out)
    for d in range(-radius, radius + 1):
        res += np.roll(out, d, axis=0)
    return res / count


def _apply_pbr(material, source_rgba, position, normal, uses, grime_mask, heat_mask, marking_mask):
    """Generates spatially varied MetallicRoughness (G=roughness, B=metallic) and tangent-space Normal maps from the
    source diffuse texture, 3D surface zones, and wear masks, and links them to the material's Principled BSDF."""
    height, width = uses.shape
    src_rgb = source_rgba[..., :3].astype(np.float32)
    lum = _luminance(src_rgb).astype(np.float32)
    covered = uses > 0

    # 3D structural zones in Pioneer source axes (+X starboard, +Y nose, +Z up):
    px, py, pz = position[..., 0], position[..., 1], position[..., 2]
    # 1. Landing gear, struts, bays and lower hydraulics (z < -1.35 m)
    gear_zone = covered & (pz < -1.35)
    # 2. Aft engine section, thruster nozzles and rear machinery (y < -3.0 m)
    engine_zone = covered & (py < -3.0)
    # 3. Dorsal & ventral antenna dishes and masts
    antenna_zone = covered & (py < -1.2) & (py > -3.2) & (np.abs(px) < 0.95) & (np.abs(pz) > 1.05)
    # 4. Dorsal docking collar ring
    dock_ring = covered & (pz > 1.25) & (np.hypot(px - 0.0, py - (-0.14)) < 1.55)
    # 5. Radiator copper/orange louvered heat exchangers (distinct warm chroma R > G + 0.05)
    radiator = covered & ((src_rgb[..., 0] - src_rgb[..., 2]) > 0.10) & (src_rgb[..., 0] > 0.25)

    # Local darkness detects recessed panel seams, latches and fastener holes in the original diffuse texture.
    local_mean_fine = _box_blur(lum, radius=2)
    local_mean_wide = _box_blur(lum, radius=5)
    seam_depth = np.clip((local_mean_fine - lum) * 3.2 + (local_mean_wide - lum) * 1.8, 0.0, 1.0)
    dark_seal = covered & (lum < 0.14) & ~gear_zone & ~engine_zone & ~antenna_zone

    # Subtle panel-to-panel variation and fine micro-grain with fixed seeds.
    macro_var = _noise((height, width), 101, 40).astype(np.float32)
    micro_var = _noise((height, width), 102, 7).astype(np.float32)

    # Physically scaled 3D hull station frames, longitudinal stringer seams, fastener rows, and radiator louver fins
    # (Pioneer source meters; at ArtScale = 1.6, 1.0 source meter = 1.6 m on the rendered shuttle).
    # Circumferential station frames perpendicular to the hull axis (~1.45 m real spacing) + axis-aligned stringers
    # (~1.1 m spacing across X on dorsal/ventral faces and Z on side walls), never cutting diagonally across the nose.
    frame_u = (py / 0.90) % 1.0
    is_side = np.abs(normal[..., 0]) > 0.55
    long_coord = np.where(is_side, pz, px)
    frame_v = (long_coord / 0.72) % 1.0
    join_u = np.clip(1.0 - np.minimum(frame_u, 1.0 - frame_u) / 0.020, 0.0, 1.0)
    join_v = np.clip(1.0 - np.minimum(frame_v, 1.0 - frame_v) / 0.022, 0.0, 1.0)
    # Keep station frames subtle on the sloped nose glazing surround (py > 5.0) so they complement Pioneer's nose lines.
    nose_fade = np.clip((5.8 - py) / 1.4, 0.25, 1.0)
    panel_join = np.maximum(join_u, join_v) * nose_fade * (covered & ~radiator).astype(np.float32)
    # Countersunk fastener/rivet rows inset ~6 cm from station frames, spaced ~20 cm apart in real meters.
    near_edge_u = (np.abs(np.minimum(frame_u, 1.0 - frame_u) - 0.052) < 0.015) & (join_v < 0.1)
    near_edge_v = (np.abs(np.minimum(frame_v, 1.0 - frame_v) - 0.058) < 0.016) & (join_u < 0.1)
    rivet_phase_u = ((py / 0.13) % 1.0) - 0.5
    rivet_phase_v = ((long_coord / 0.13) % 1.0) - 0.5
    rivet_u = np.clip(1.0 - np.abs(rivet_phase_u) / 0.20, 0.0, 1.0) * near_edge_v.astype(np.float32)
    rivet_v = np.clip(1.0 - np.abs(rivet_phase_v) / 0.20, 0.0, 1.0) * near_edge_u.astype(np.float32)
    fasteners = np.maximum(rivet_u, rivet_v) * nose_fade * (covered & ~radiator & ~marking_mask).astype(np.float32)
    # Radiator louver fins (~6 cm physical pitch along the hull length).
    louver_wave = (0.5 + 0.5 * np.sin((py / 0.055) * (2.0 * math.pi))) * radiator.astype(np.float32)
    # Subtle skin-plate pillowing ("oil-canning") across each aerospace skin panel.
    oil_can = (np.sin(frame_u * math.pi) * np.sin(frame_v * math.pi)).astype(np.float32) * (covered & ~radiator)

    # Base: satin painted ceramic/thermal hull (dielectric).
    metallic = np.zeros((height, width), dtype=np.float32)
    roughness = (0.44 + 0.11 * macro_var + 0.05 * micro_var).astype(np.float32)

    # Bare metal regions: gear, nozzles, engines, antennas, dorsal docking collar, mid-grey metallic structural parts.
    # Within engine_zone, bright white ceramic cowlings (lum > 0.62) stay painted dielectric, while mid/dark alloy is metal.
    engine_metal = engine_zone & (lum < 0.58) & ~radiator
    gear_metal = gear_zone & (lum < 0.72)
    collar_metal = dock_ring & (lum < 0.55)
    bare_metal = engine_metal | gear_metal | antenna_zone | collar_metal
    metallic[bare_metal] = 0.84 + 0.08 * macro_var[bare_metal]
    roughness[bare_metal] = 0.24 + 0.14 * macro_var[bare_metal] + 0.06 * micro_var[bare_metal]

    # Polished hydraulic pistons / bright gear metal
    polished = gear_zone & (lum >= 0.42) & (lum < 0.72)
    metallic[polished] = 0.94
    roughness[polished] = 0.16 + 0.06 * micro_var[polished]

    # Heat-exchanger radiator fins: anodized metallic bronze/copper sheen
    metallic[radiator] = 0.62
    roughness[radiator] = 0.36 + 0.10 * micro_var[radiator]

    # Elastomer seals, dark recessed trim, intakes: matte dielectric
    metallic[dark_seal] = 0.04
    roughness[dark_seal] = 0.76 + 0.10 * micro_var[dark_seal]

    # Panel seams and fasteners carry slightly rougher crevices and reduced metallic specular inside grooves.
    seam_total = np.clip(seam_depth + 0.65 * panel_join, 0.0, 1.0)
    roughness = np.clip(roughness + 0.26 * seam_total + 0.10 * fasteners, 0.12, 0.96)
    metallic = np.clip(metallic * (1.0 - 0.55 * seam_total) + 0.25 * fasteners * (~bare_metal), 0.0, 1.0)

    # Service grime & nozzle soot roughen painted/metal surfaces and damp metallic response where soot coats the hull.
    roughness = np.clip(roughness + 0.34 * grime_mask + 0.24 * heat_mask, 0.12, 0.96)
    metallic = np.clip(metallic * (1.0 - 0.65 * grime_mask) * (1.0 - 0.35 * heat_mask), 0.0, 1.0)

    # Painted markings (BD-07, 07, hazard stripes) are satin paint over the hull.
    metallic[marking_mask] = 0.0
    roughness[marking_mask] = 0.50 + 0.05 * micro_var[marking_mask]

    # Pack glTF MetallicRoughness texture: R=1 (unused/AO), G=Roughness, B=Metallic, A=1 (Linear / Non-Color).
    mr_rgba = np.ones((height, width, 4), dtype=np.float32)
    mr_rgba[..., 1] = roughness
    mr_rgba[..., 2] = metallic
    mr_img = glow.new_image(material.name + ".mr", mr_rgba)
    mr_img.colorspace_settings.name = "Non-Color"

    # Tangent-space normal map combining painted source panel seams, 3D aerospace metal joins, countersunk fastener rows,
    # radiator louver fins, panel oil-canning, and micro-surface grain.
    plate_height = (
        0.52 * (1.0 - seam_depth)
        - 0.24 * panel_join
        - 0.16 * fasteners
        + 0.28 * louver_wave
        + 0.22 * np.clip(lum, 0.0, 1.0)
        + 0.06 * oil_can
        + 0.04 * macro_var
        + 0.015 * micro_var
    ).astype(np.float32)
    # Light 3x3 smoothing suppresses DDS block artifacts while preserving crisp V-grooves, louvers, and rivet dimples.
    plate_smooth = _box_blur(plate_height, radius=1)
    # Central differences; Blender bottom-up rows mean +dv points upward along +V.
    du = (np.roll(plate_smooth, -1, axis=1) - np.roll(plate_smooth, 1, axis=1)) * 2.3
    dv = (np.roll(plate_smooth, -1, axis=0) - np.roll(plate_smooth, 1, axis=0)) * 2.3
    nx = -du
    ny = -dv
    nz = np.ones_like(du, dtype=np.float32)
    inv_len = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    n_rgba = np.ones((height, width, 4), dtype=np.float32)
    n_rgba[..., 0] = nx * inv_len * 0.5 + 0.5
    n_rgba[..., 1] = ny * inv_len * 0.5 + 0.5
    n_rgba[..., 2] = nz * inv_len * 0.5 + 0.5
    # Keep uncovered border texels flat neutral normal (0.5, 0.5, 1.0).
    n_rgba[~covered, 0] = 0.5
    n_rgba[~covered, 1] = 0.5
    n_rgba[~covered, 2] = 1.0
    n_img = glow.new_image(material.name + ".normal", n_rgba)
    n_img.colorspace_settings.name = "Non-Color"

    tree = material.node_tree
    bsdf = tree.nodes["Principled BSDF"]
    mr_node = tree.nodes.new("ShaderNodeTexImage")
    mr_node.image = mr_img
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(mr_node.outputs["Color"], sep.inputs["Color"])
    tree.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    tree.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])

    n_node = tree.nodes.new("ShaderNodeTexImage")
    n_node.image = n_img
    n_map = tree.nodes.new("ShaderNodeNormalMap")
    n_map.inputs["Strength"].default_value = 1.0
    tree.links.new(n_node.outputs["Color"], n_map.inputs["Color"])
    tree.links.new(n_map.outputs["Normal"], bsdf.inputs["Normal"])

    return {
        "metalTexels": int((metallic > 0.7).sum()),
        "dielectricTexels": int((covered & (metallic < 0.1)).sum()),
        "roughnessRange": [round(float(roughness[covered].min()), 3), round(float(roughness[covered].max()), 3)],
        "tiltedNormalTexels": int((covered & ((np.abs(n_rgba[..., 0] - 0.5) > 0.03) | (np.abs(n_rgba[..., 1] - 0.5) > 0.03))).sum()),
    }
