"""Tool code. The glow bake of the converter (tools/README.md, "The Blue Danube art"): the engine's MaterialDesc has a
constant emissive color and no emissive map, so a material's glow map becomes a few emissive copies of the material.

For each triangle of a material with a glow map, the bake takes the mean linear glow color over the triangle's UV
footprint, from a fixed pattern of SAMPLES points. A triangle whose mean luminance is below the threshold gets no
emission. The other triangles go into at most `bins` groups by k-means on their mean colors, with a fixed start, so the
same inputs give the same groups. Each group becomes a copy of the material with the group's mean color as a constant
emission, times the sidecar's strength in W/(m²·sr)."""

import bpy
import bmesh
import numpy as np

# The barycentric sample pattern: the centers of a 4 by 4 grid, folded into the triangle.
_GRID = (np.arange(4) + 0.5) / 4
_A, _B = np.meshgrid(_GRID, _GRID)
_A, _B = _A.ravel(), _B.ravel()
_FOLD = _A + _B > 1
_A[_FOLD], _B[_FOLD] = 1 - _A[_FOLD], 1 - _B[_FOLD]
SAMPLES = np.stack([1 - _A - _B, _A, _B], axis=1)

# Below this coverage, a glowing triangle glows only over a part of its footprint, as a window drawn on a wall does.
PARTIAL_COVERAGE = 0.75

_LUMINANCE = np.array([0.2126, 0.7152, 0.0722])


def pixels(image):
    """The pixels of `image` as an array of rows by columns by RGBA, bottom row first, as Blender stores them."""
    width, height = image.size
    flat = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(flat)
    return flat.reshape(height, width, 4)


def new_image(name, rgba):
    """A packed sRGB image of the array `rgba` (rows by columns by RGBA, bottom row first)."""
    height, width = rgba.shape[:2]
    image = bpy.data.images.new(name, width, height, alpha=True)
    image.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    image.pack()
    return image


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def bake(pieces, config):
    """Bakes the glow of every material of `pieces` that has one. Returns, per material, its triangles, its glowing
    groups and its partly glowing triangles."""
    strength = float(config.get("strength", 60.0))
    threshold = float(config.get("threshold", 0.05))
    bins = int(config.get("bins", 4))
    report = {}
    for piece_name in sorted(pieces):
        obj = pieces[piece_name]
        mesh = obj.data
        for slot, material in enumerate(list(mesh.materials)):
            image = _glow_image(material)
            if image is None:
                continue
            _triangulate(mesh)
            entry = _bake_material(obj, slot, material, image, strength, threshold, bins)
            report[f"{piece_name}/{material.name}"] = entry
    return report


def _glow_image(material):
    """The glow map of a material: a Pioneer material's tertius_glow, or the image that a glTF material links to its
    emission color. The glTF link is cut, because the engine draws no emission map."""
    if material is None:
        return None
    if "tertius_glow" in material:
        return bpy.data.images[material["tertius_glow"]]
    if not material.use_nodes:
        return None
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is None or not bsdf.inputs["Emission Color"].is_linked:
        return None
    node = bsdf.inputs["Emission Color"].links[0].from_node
    while node.type != "TEX_IMAGE" and node.inputs and node.inputs[0].is_linked:
        node = node.inputs[0].links[0].from_node
    for link in list(bsdf.inputs["Emission Color"].links):
        material.node_tree.links.remove(link)
    bsdf.inputs["Emission Strength"].default_value = 0.0
    return node.image if node.type == "TEX_IMAGE" else None


def _triangulate(mesh):
    if all(len(p.vertices) == 3 for p in mesh.polygons):
        return
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()


def _bake_material(obj, slot, material, image, strength, threshold, bins):
    mesh = obj.data
    count = len(mesh.polygons)
    face_slots = np.empty(count, dtype=np.int32)
    mesh.polygons.foreach_get("material_index", face_slots)
    faces = np.flatnonzero(face_slots == slot)
    uv = np.empty(len(mesh.loops) * 2, dtype=np.float64)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    starts = np.empty(count, dtype=np.int32)
    mesh.polygons.foreach_get("loop_start", starts)
    corners = uv[starts[faces][:, None] + np.arange(3)[None, :]]
    texels = pixels(image)[..., :3]
    height, width = texels.shape[:2]
    points = np.einsum("sk,nkd->nsd", SAMPLES, corners)
    x = np.floor(np.mod(points[..., 0], 1.0) * width).astype(np.int64).clip(0, width - 1)
    y = np.floor(np.mod(points[..., 1], 1.0) * height).astype(np.int64).clip(0, height - 1)
    colors = srgb_to_linear(texels[y, x].astype(np.float64))
    lum = colors @ _LUMINANCE
    mean = colors.mean(axis=1)
    mean_lum = mean @ _LUMINANCE
    glowing = mean_lum >= threshold
    coverage = (lum >= threshold).mean(axis=1)
    entry = {"triangles": int(len(faces)), "glowing": int(glowing.sum()), "groups": []}
    if not glowing.any():
        return entry
    labels, centers = kmeans(mean[glowing], min(bins, int(glowing.sum())))
    glow_faces = faces[glowing]
    for group, center in enumerate(centers):
        members = glow_faces[labels == group]
        if len(members) == 0:
            continue
        peak = float(center.max())
        copy = material.copy()
        copy.name = f"{material.name}.glow{group}"
        bsdf = copy.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Emission Color"].default_value = (*(center / peak), 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength * peak
        mesh.materials.append(copy)
        face_slots[members] = len(mesh.materials) - 1
        entry["groups"].append({
            "material": copy.name,
            "triangles": int(len(members)),
            "linearColor": [round(float(v), 4) for v in center],
            "partial": int((coverage[glowing][labels == group] < PARTIAL_COVERAGE).sum()),
        })
    mesh.polygons.foreach_set("material_index", face_slots)
    return entry


def kmeans(colors, k, iterations=12):
    """Groups the rows of `colors` into `k` groups. The start centers are the colors at evenly spaced ranks of
    luminance, so the result depends only on the input."""
    order = np.argsort(colors @ _LUMINANCE, kind="stable")
    ranks = np.linspace(0, len(colors) - 1, k).round().astype(np.int64)
    centers = colors[order[ranks]].copy()
    labels = np.zeros(len(colors), dtype=np.int64)
    for _ in range(iterations):
        distances = ((colors[:, None, :] - centers[None, :, :]) ** 2).sum(axis=2)
        labels = distances.argmin(axis=1)
        for j in range(k):
            members = colors[labels == j]
            if len(members):
                centers[j] = members.mean(axis=0)
    return labels, centers
