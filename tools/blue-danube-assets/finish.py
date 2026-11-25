"""Tool code. The finish of a model's diffuse map: procedural changes with fixed parameters and seeds, which the sidecar's
"finish" block names (tools/README.md, "The Blue Danube art"). The geometry stays as it is.

The finish works in the texture's own pixels. It first rasterizes every triangle of the material into the texture's UV
space, so each texel knows the point of the model that it colors, in the source's axes, and the surface normal there.
Many texels color two places, such as both sides of a mirrored hull. The finish then:

1. pales the hull: it desaturates and lifts every light texel, and leaves dark details and glowing texels alone;
2. draws wear: grime near hatches and gear bays, heat tint near nozzles, each a falloff about a point in the source's
   axes times value noise with a fixed seed; a "mirrorX" spot acts on both sides, so a shared texel gets the same wear;
3. draws markings: hazard stripes in a ring about a hatch, and text in a 5 by 7 pixel font, projected from a plane in the
   source's axes; markings go only on texels that color one place, so no text shows mirrored."""

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
    """Applies the finish `config` to the diffuse map of its material on `pieces`. Returns what it changed."""
    material = bpy.data.materials[config["material"]]
    node = _base_color_node(material)
    rgba = glow.pixels(node.image).copy()
    height, width = rgba.shape[:2]
    position, normal, uses = _rasterize(pieces, material, width, height)
    glow_luminance = _glow_luminance(material, width, height)
    report = {"texels": int((uses > 0).sum()), "sharedTexels": int((uses > 1).sum())}
    report["paled"] = _pale(rgba, config["pale"], glow_luminance)
    report["wear"] = [_wear(rgba, spot, position, uses) for spot in config.get("wear", [])]
    report["markings"] = [_mark(rgba, mark, position, normal, uses) for mark in config.get("markings", [])]
    node.image = glow.new_image(node.image.name + ".finished", rgba)
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


def _wear(rgba, spot, position, uses):
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
    elif spot["kind"] == "heat":
        # Soot at the nozzle, a bronze and blue tint farther out.
        tint = np.where((falloff > 0.55)[..., None], np.array([0.13, 0.12, 0.11]), np.array([0.42, 0.40, 0.52]))
        rgb[:] = rgb + (tint - rgb) * mask[..., None]
    else:
        raise ValueError(f"unknown wear kind {spot['kind']!r}")
    return {"kind": spot["kind"], "texels": int((mask > 0.05).sum())}


def _mark(rgba, mark, position, normal, uses):
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
        return {"kind": "text", "text": text, "texels": int(ink.sum())}
    raise ValueError(f"unknown marking kind {mark['kind']!r}")
