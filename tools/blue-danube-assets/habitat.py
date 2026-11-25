"""Build the occupied side galleries on Pioneer's station_ring_2-2k, in its native Z-up metres.

The two flat axial faces of the imported ring run from radius 1885 to 1977 m.
A gallery is 24 m deep radially. Decks are 4 m apart and down is toward the rim.
The facades share one color atlas and its tangent-space normal map. Each actual
pane belongs to one room and takes a constant material, including when unlit.
"""

import math
import random

import bpy
import numpy as np


BAYS = 384
INNER = 1917.0
DEPTH = 24.0
PITCH = math.tau / BAYS
WIDTH = PITCH * (INNER + DEPTH / 2)
# The axial faces of the source mesh lie at +238.32 and -267.49 m.
SIDES = (238.6, -267.8)
TEXELS = 384
DISTRICTS = ("cabins", "concourse", "plant")


def apertures(district):
    """Window centres and sizes in metres along the bay and toward the rim."""
    for deck in range(4):
        y = 4.2 + 4.0 * deck
        if district == "cabins":
            for room in range(5):
                yield 4.6 + room * 5.3, y, 2.35, 1.5
        elif district == "concourse":
            for lounge in range(3):
                yield 6.4 + lounge * 8.8, y, 5.4, 1.85


def _atlas():
    h, w = TEXELS, TEXELS * len(DISTRICTS)
    color = np.empty((h, w, 3), np.float32)
    response = np.empty_like(color)
    height = np.zeros((h, w), np.float32)
    rng = np.random.default_rng(149)
    for i, district in enumerate(DISTRICTS):
        c = color[:, i * TEXELS : (i + 1) * TEXELS]
        mask = response[:, i * TEXELS : (i + 1) * TEXELS]
        z = height[:, i * TEXELS : (i + 1) * TEXELS]

        # Height is in metres. Work in the same metre coordinates as the pane geometry.
        def box(x0, y0, x1, y1, rgb, relief, roughness=0.53, metallic=0):
            x0, x1 = np.clip(
                np.rint(np.array([x0, x1]) * TEXELS / WIDTH).astype(int), 0, TEXELS
            )
            y0, y1 = np.clip(
                np.rint(np.array([y0, y1]) * TEXELS / DEPTH).astype(int), 0, TEXELS
            )
            c[y0:y1, x0:x1] = rgb
            z[y0:y1, x0:x1] = relief
            mask[y0:y1, x0:x1] = (1, roughness, metallic)

        mask[:] = (1, 0.86, 0)  # Recessed joints between panels.
        c[:] = (0.10, 0.12, 0.13)
        for column in range(6):
            for row in range(6):
                x = column * WIDTH / 6
                y = row * DEPTH / 6
                tint = rng.uniform(0.85, 1.13)
                alloy = np.array(
                    (0.34, 0.36, 0.35) if district != "plant" else (0.24, 0.27, 0.28)
                )
                box(
                    x + 0.11,
                    y + 0.08,
                    x + WIDTH / 6 - 0.09,
                    y + 3.92,
                    alloy * tint,
                    0.055,
                )
                box(x + 0.40, y + 0.23, x + 0.52, y + 0.35, (0.12, 0.14, 0.14), 0.018, 0.38, 1)
                box(
                    x + WIDTH / 6 - 0.48,
                    y + 3.61,
                    x + WIDTH / 6 - 0.36,
                    y + 3.73,
                    (0.12, 0.14, 0.14),
                    0.018, 0.38, 1,
                )
        for x, y, width, opening in apertures(district):
            for margin, tint, relief, roughness, metallic in [
                (0.32, (0.17, 0.20, 0.20), 0.16, 0.36, 1),
                (0.15, (0.05, 0.07, 0.075), 0.035, 0.82, 0),
            ]:
                box(
                    x - width / 2 - margin,
                    y - opening / 2 - margin,
                    x + width / 2 + margin,
                    y + opening / 2 + margin,
                    tint,
                    relief, roughness, metallic,
                )
        if district == "plant":
            # The intake louvers, access panels and pipe runs occupy the windowless bays.
            for x in (3.5, 14.7, 24.4):
                box(x - 1, 5, x + 3.6, 16, (0.135, 0.16, 0.17), 0.08)
                for y in np.arange(5.3, 15.9, 0.58):
                    box(x - 0.8, y, x + 3.4, y + 0.20, (0.32, 0.35, 0.34), 0.18, 0.42, 1)
            for y in (18, 18.55):
                box(0.0, y, WIDTH, y + 0.24, (0.37, 0.32, 0.23), 0.20, 0.36, 1)
        # Service covers with hinges, a latch and restrained safety markings.
        for x in (4.0, 18.0):
            box(x, 20, x + 6, 23.1, (0.18, 0.21, 0.22), 0.02)
            box(x + 0.18, 20.18, x + 5.82, 22.92, (0.33, 0.35, 0.34), 0.11)
            box(x + 4.8, 21.2, x + 5.15, 21.75, (0.09, 0.11, 0.12), 0.03)
            for y in (20.6, 22.25):
                box(x + 0.1, y, x + 0.56, y + 0.20, (0.42, 0.43, 0.40), 0.16, 0.32, 1)
        box(0.75, 0.48, WIDTH - 0.75, 0.76, (0.57, 0.39, 0.15), 0.045)

    # A height gradient over distance gives the slope. UV V and image rows
    # both increase toward the rim in Blender's bottom-up image storage.
    dv, du = np.gradient(height, DEPTH / TEXELS, WIDTH / TEXELS)
    normal = np.stack((-du, -dv, np.ones_like(du)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    # Keep atlas boundaries neutral under filtering.
    for i in range(len(DISTRICTS)):
        normal[:, i * TEXELS : i * TEXELS + 2] = (0, 0, 1)
        normal[:, (i + 1) * TEXELS - 2 : (i + 1) * TEXELS] = (0, 0, 1)
    return color, normal * 0.5 + 0.5, response


def _packed_image(name, rgb, color_space):
    h, w, _ = rgb.shape
    image = bpy.data.images.new(name, width=w, height=h, alpha=False)
    image.colorspace_settings.name = color_space
    rgba = np.ones((h, w, 4), np.float32)
    rgba[:, :, :3] = rgb
    image.pixels.foreach_set(rgba.ravel())
    image.pack()
    return image


def _response_map(material, name, response):
    tree = material.node_tree
    shader = tree.nodes["Principled BSDF"]
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.image = _packed_image(name, response, "Non-Color")
    split = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(texture.outputs["Color"], split.inputs["Color"])
    tree.links.new(split.outputs["Green"], shader.inputs["Roughness"])
    tree.links.new(split.outputs["Blue"], shader.inputs["Metallic"])


def finish_ring(material, source):
    """Replace Pioneer's green and yellow atlas swatches and keep the source truss outlines and UV layout."""
    atlas = source.copy()
    atlas.scale(2048, 2048)
    pixels = np.empty(2048 * 2048 * 4, dtype=np.float32)
    atlas.pixels.foreach_get(pixels)
    rgb = pixels.reshape(2048, 2048, 4)[..., :3].copy()
    bpy.data.images.remove(atlas)
    red, green, blue = np.moveaxis(rgb, 2, 0)
    cladding = (green > 1.5 * red + 0.06) & (green > 1.5 * blue + 0.06)
    primer = (red > blue + 0.08) & (green > blue + 0.08)
    gasket = (red > green + 0.015) & (blue > green + 0.015)
    # Blue-black bays lie behind light truss members in the source. Keep that silhouette.
    insulated = (blue > red * 1.2) & ~gasket
    finish = np.empty_like(rgb)
    finish[:] = (1, 0.43, 0.95)  # Exposed alloy on the braces.
    finish[insulated | gasket] = (1, 0.82, 0)
    finish[primer | cladding] = (1, 0.58, 0)
    rgb[cladding] = (0.58, 0.61, 0.59)
    rgb[primer] = (0.47, 0.34, 0.16)
    rgb[gasket] = (0.064, 0.072, 0.076)
    _response_map(material, "Ring surface finishes", finish)
    return _packed_image("Ring cladding", rgb, "sRGB")


def _materials():
    panels = bpy.data.materials.new("Gallery panels")
    panels.use_nodes = True
    tree = panels.node_tree
    shader = tree.nodes.get("Principled BSDF")
    colors, normals, response = _atlas()
    _response_map(panels, "Gallery finishes", response)
    albedo = tree.nodes.new("ShaderNodeTexImage")
    albedo.image = _packed_image("Gallery cladding", colors, "sRGB")
    tree.links.new(albedo.outputs["Color"], shader.inputs["Base Color"])
    relief = tree.nodes.new("ShaderNodeTexImage")
    relief.image = _packed_image("Gallery relief", normals, "Non-Color")
    tangent_map = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(relief.outputs["Color"], tangent_map.inputs["Color"])
    tree.links.new(tangent_map.outputs["Normal"], shader.inputs["Normal"])
    # Match cabin lighting to the other station facades (peak channel about 42 W/(m²·sr)).
    window_radiance = 60.0
    panes = []
    for label, glow in [
        ("Unlit", (0.0, 0.0, 0.0)),
        ("Curtained", (0.18, 0.12, 0.065)),
        ("Cabin", (0.92, 0.65, 0.36)),
        ("Galley", (0.67, 0.80, 0.83)),
    ]:
        pane = bpy.data.materials.new("Window " + label)
        pane.use_nodes = True
        shader = pane.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = (0.036, 0.062, 0.075, 1)
        shader.inputs["Roughness"].default_value = 0.24
        shader.inputs["Emission Color"].default_value = (*glow, 1)
        shader.inputs["Emission Strength"].default_value = window_radiance
        panes.append(pane)
    return panels, panes


def _point(bay, along, radial, z):
    theta = (bay + along / WIDTH) * PITCH
    radius = INNER + radial
    return (radius * math.cos(theta), radius * math.sin(theta), z)


def _rectangle(bay, x0, y0, x1, y1, z):
    corners = [(x0, y0), (x0, y1), (x1, y1), (x1, y0)]
    if z < 0:
        corners.reverse()
    return [_point(bay, x, y, z) for x, y in corners]


def _quads(name, points, materials, indices, uvs=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(
        points, [], [tuple(range(i, i + 4)) for i in range(0, len(points), 4)]
    )
    for mat in materials:
        mesh.materials.append(mat)
    mesh.polygons.foreach_set("material_index", indices)
    if uvs is not None:
        mesh.uv_layers.new().uv.foreach_set(
            "vector", np.asarray(uvs, dtype=np.float32).ravel()
        )
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def build():
    """Add the two meter-scale meshes before the importer's station axis conversion."""
    panels, panes = _materials()
    panel_points, pane_points, uvs, occupancy = [], [], [], []
    trim = []
    frame = bpy.data.materials.new("Pressure frames")
    frame.diffuse_color = (0.10, 0.13, 0.14, 1)
    frame.use_nodes = True
    frame.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (
        0.10,
        0.13,
        0.14,
        1,
    )
    for side, z in enumerate(SIDES):
        for bay in range(BAYS):
            rng = random.Random(1013 * bay + 587 * side)
            # Group neighbors into streets. The plant and freight sectors break up the lights.
            street = (bay // 8 + 3 * side) % 12
            district = (
                "plant"
                if street in (0, 1, 7)
                else "concourse"
                if street in (4, 10)
                else "cabins"
            )
            # Raised arcades bridge three neighboring bays. Their sloping end walls
            # and projecting roofs catch the sun in the long approach shot.
            arcade = district == "concourse" and 2 <= bay % 8 <= 4
            sign = math.copysign(1.0, z)
            face_z = z + (16 * sign if arcade else 0)
            panel_points.extend(_rectangle(bay, 0, 0, WIDTH, DEPTH, face_z))
            if arcade:
                low = _rectangle(bay, 0, -3, WIDTH, DEPTH + 3, z)
                high = _rectangle(bay, 0, 0, WIDTH, DEPTH, face_z)
                for j in range(4):
                    k = (j + 1) % 4
                    # Ends between adjacent bays share a wall inside the arcade.
                    if (
                        j in (1, 3)
                        or (j == (0 if z > 0 else 2) and bay % 8 == 2)
                        or (j == (2 if z > 0 else 0) and bay % 8 == 4)
                    ):
                        trim.extend((low[j], low[k], high[k], high[j]))
            tile = DISTRICTS.index(district)
            u0, u1 = (tile + 0.002) / 3, (tile + 0.998) / 3
            corners = [(u0, 0.002), (u0, 0.998), (u1, 0.998), (u1, 0.002)]
            uvs.extend(corners if z > 0 else reversed(corners))
            occupancy_rate = 0.36 + 0.42 * rng.random()
            for x, y, w, h in apertures(district):
                pane_z = face_z + 0.075 * sign
                pane_points.extend(
                    _rectangle(bay, x - w / 2, y - h / 2, x + w / 2, y + h / 2, pane_z)
                )
                # The transom and partly closed blinds interrupt a room's light.
                # The room behind a blind keeps its ordinary glass material.
                front = pane_z + 0.055 * sign
                if district == "concourse":
                    trim.extend(
                        _rectangle(
                            bay, x - 0.075, y - h / 2, x + 0.075, y + h / 2, front
                        )
                    )
                if rng.random() < 0.38:
                    blind = rng.uniform(0.18, 0.62) * h
                    trim.extend(
                        _rectangle(
                            bay,
                            x - w / 2,
                            y - h / 2,
                            x + w / 2,
                            y - h / 2 + blind,
                            front,
                        )
                    )
                lit = rng.random() < occupancy_rate
                occupancy.append(
                    (3 if district == "concourse" else rng.choice((1, 2, 2)))
                    if lit
                    else 0
                )
    skin = _quads(
        "Residential galleries",
        panel_points,
        [panels],
        [0] * (len(panel_points) // 4),
        uvs,
    )
    glass = _quads("Cabin glazing", pane_points, panes, occupancy)
    sills = _quads("Arcade walls and shades", trim, [frame], [0] * (len(trim) // 4))
    return skin, glass, sills
