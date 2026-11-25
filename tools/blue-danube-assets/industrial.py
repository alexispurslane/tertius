"""Metric height fields for the Pioneer station and cargo pod. Array row zero is Blender's UV bottom."""
import math

import numpy as np


def surface_normal(height, spacing):
    """Encode a periodic height field in metres as an OpenGL tangent normal."""
    du = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / (2 * spacing)
    dv = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) / (2 * spacing)
    normal = np.stack((-du, -dv, np.ones_like(height)), axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return normal * 0.5 + 0.5


def _ramp(distance, bevel):
    t = np.clip(distance / bevel, 0, 1)
    return t * t * (3 - 2 * t)


def spoke_uvs(points):
    """Wrap a 32 m axial repeat and 22 repeats around the 56.7 m radius, in Pioneer's Z-up coordinates."""
    p = np.asarray(points)
    face = np.cross(p[1] - p[0], p[2] - p[0])
    if abs(face[0]) > 0.85 * np.linalg.norm(face):
        return [(y / 32, z / 16) for _, y, z in points]
    angle = np.unwrap(np.arctan2(p[:, 2] + 14.6, p[:, 1]))
    return list(zip(p[:, 0] / 32, angle * (22 / math.tau)))


def spoke_skin():
    """A 32 × 16 m repeat. Paint covers formed panels, with bare retaining plates and a recessed service hatch."""
    v, u = (np.indices((512, 1024), dtype=np.float32) + 0.5) / 32
    row = (v // 4).astype(int)
    x = u + 2 * (row % 2)
    seam = np.minimum(3.2 - abs(x % 6.4 - 3.2), 2 - abs(v % 4 - 2))
    panel = _ramp(seam - 0.045, 0.095)
    level = ((x // 6.4 + row * 3) % 5) * 0.009
    color = np.array([0.075, 0.085, 0.09]) + panel[..., None] * (
        np.array([0.52, 0.53, 0.51]) + level[..., None])
    height = panel * 0.032
    rough = 0.82 - 0.29 * panel
    metal = np.zeros_like(height)

    def feature(inside, bevel, depth, rgb, roughness, metallic):
        nonlocal color, height, rough, metal
        mix = _ramp(inside, bevel)
        color += (rgb - color) * mix[..., None]
        height += depth * mix
        rough += (roughness - rough) * mix
        metal += (metallic - metal) * mix

    # The maintenance hatch has a recessed gasket, a raised lid and two steel hinges.
    for width, depth, rise, rgb, r, m in [
        (2.6, 1.6, -0.025, (0.045, 0.055, 0.06), 0.87, 0),
        (2.4, 1.4, 0.065, (0.39, 0.43, 0.43), 0.47, 0),
    ]:
        feature(np.minimum(width / 2 - abs(u - 13), depth / 2 - abs(v - 6)), 0.08, rise, rgb, r, m)
    for cx in (12.25, 13.75):
        feature(np.minimum(0.21 - abs(u - cx), 0.12 - abs(v - 6.73)), 0.065, 0.045,
                (0.56, 0.59, 0.59), 0.36, 1)
    # Each panel has a pair of retaining fasteners, 15 cm across, inset from the joint.
    for cx, cy in ((0.30, 0.28), (6.10, 3.72)):
        feature(0.075 - np.hypot(x % 6.4 - cx, v % 4 - cy), 0.045, 0.020,
                (0.40, 0.42, 0.42), 0.34, 1)
    response = np.stack((np.ones_like(height), rough, metal), axis=-1)
    return color, surface_normal(height, 1 / 32), response


def cargo_skin():
    """Match the octagonal door, four hinges and locking wheel drawn on Pioneer's 1024 px cargo atlas."""
    size = 1024
    v, u = (np.indices((size, size), dtype=np.float32) + 0.5) / size
    # The measured access face spans 4.479 m per full UV interval in either direction.
    metres = 4.479
    x, y = (u - 0.694) * metres, (v - 0.311) * metres
    def octagon(half, diagonal):
        return np.minimum(half - np.maximum(abs(x), abs(y)), (diagonal - abs(x) - abs(y)) / math.sqrt(2))
    collar = _ramp(octagon(1.14, 1.73), 0.026)
    lid = _ramp(octagon(0.95, 1.43), 0.018)
    height = 0.019 * collar - 0.027 * lid
    hinges = np.zeros_like(height)
    for cx in (-0.162, 0.158):
        for cy in (-1.10, 1.13):
            hinges += _ramp(np.minimum(0.048 - abs(x - cx), 0.106 - abs(y - cy)), 0.016)
    wheel = _ramp(0.10 - np.hypot(x, y), 0.015)
    rib = _ramp(np.minimum(0.061 - np.minimum(abs(x), abs(y)), 0.61 - np.maximum(abs(x), abs(y))), 0.016)
    lock = np.maximum(wheel, rib)
    height += 0.035 * hinges + 0.034 * lock
    metal = np.maximum(lock, hinges)
    roughness = 0.65 + 0.15 * collar - 0.24 * lid
    roughness += (0.36 - roughness) * metal
    response = np.stack((np.ones_like(height), roughness, metal), axis=-1)
    return surface_normal(height, metres / size), response


def station_atlas_normal(material):
    """Recesses traced on 480-unit, top-down views of Pioneer's square atlases."""
    # Median metres per UV interval on the exported non-emitting triangles.
    metres = {"facade": 104, "floor": 212, "tunnels": 132, "strut": 91}[material]
    size = 1024
    y, x = (np.indices((size, size), dtype=np.float32) + 0.5) * (480 / size)
    height = np.zeros_like(x)

    def recess(depth, *corners):
        # Signed distance inside each clockwise edge. Bevel the lip over two texels.
        cols, rows = zip(*corners)
        region = np.s_[int(min(rows) * size / 480):math.ceil(max(rows) * size / 480),
                       int(min(cols) * size / 480):math.ceil(max(cols) * size / 480)]
        distance = np.full_like(x[region], np.inf)
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            dx, dy = bx - ax, by - ay
            edge = (dx * (y[region] - ay) - dy * (x[region] - ax)) / math.hypot(dx, dy)
            np.minimum(distance, edge, out=distance)
        np.minimum(height[region], -depth * _ramp(distance, 2 * 480 / size), out=height[region])

    if material == "facade":
        # Each window sits behind its reveal. Trace the first row of the curved wall.
        # Lower rows widen with the wall. The last two rows occupy its left half.
        bays = [(24, 37, 26.5, 28), (39, 52, 28.5, 30), (57, 71, 30.5, 31.5),
                (73, 87, 32, 32), (95, 109, 32, 32), (111, 125, 31.5, 30.5),
                (130, 143, 30, 28.5), (145, 158, 28, 26.5)]
        for row in range(5):
            for left, right, y1, y2 in (bays if row < 3 else bays[:4]):
                left += (left - 88) * 0.044 * row
                right += (right - 88) * 0.044 * row
                y1 += 14.7 * row
                y2 += 14.7 * row
                recess(0.075, (left, y1), (right, y2), (right, y2 + 4.4), (left, y1 + 4.4))
        recess(0.055, (156, 406), (164, 406), (162, 417), (156, 417))
        # Cladding inside the tapered girders and the three framed service covers below.
        for left, right, top in ((73, 113, 224), (119, 159, 245), (165, 198, 263),
                                 (204, 235, 279), (241, 272, 291)):
            shift = (309 - top) * 0.56
            recess(0.065, (left + shift, top), (right + shift * 0.84, top + 9),
                   (right, 309), (left, 309))
        for top, bottom in ((389, 414), (420, 445), (451, 474)):
            recess(0.05, (176, top), (207, top - 5), (207, bottom), (176, bottom + 1))
        recess(0.07, (219, 372), (295, 362), (299, 399), (222, 411))
        recess(0.07, (58, 126), (102, 126), (102, 151), (57, 151))
    elif material == "floor":
        # The two landing pads have the same square plate pitch. The yellow circles,
        # checkerboards and safety stripes cross those plates as paint.
        for left, top, right, bottom, origin_x, origin_y in (
            (269, 10, 477, 213, 278, 5), (198, 227, 477, 476, 194, 229),
        ):
            pitch = 23.5
            joint = np.minimum(abs((x - origin_x + pitch / 2) % pitch - pitch / 2),
                               abs((y - origin_y + pitch / 2) % pitch - pitch / 2))
            pad = np.minimum(np.minimum(x - left, right - x), np.minimum(y - top, bottom - y))
            height -= 0.035 * _ramp(pad, 1) * _ramp(0.85 - joint, 0.65)
        for top in (121, 145, 168):
            recess(0.08, (66, top), (106, top), (106, top + 9), (66, top + 9))
        recess(0.08, (120, 257), (159, 257), (159, 460), (120, 460))
    elif material == "tunnels":
        # Recess the six triangular bays between the diagonal braces.
        for top, bottom, ascending in ((33, 168, True), (184, 300, False), (311, 444, True)):
            left, right = 390, 464
            if ascending:
                recess(0.11, (left, top), (right - 5, top), (left, bottom - 8))
                recess(0.11, (right, top + 8), (right, bottom), (left + 5, bottom))
            else:
                recess(0.11, (left + 5, top), (right, top), (right, bottom - 8))
                recess(0.11, (left, top + 8), (right - 5, bottom), (left, bottom))
    else:
        # Sleeve joints on the alloy sections and the inset rectangular service socket.
        for seam in (285, 339, 387, 421):
            recess(0.045, (184, seam), (236, seam), (236, seam + 2), (184, seam + 2))
        recess(0.08, (79, 65), (111, 65), (111, 98), (79, 98))
        recess(0.08, (142, 339), (153, 330), (175, 330), (181, 338),
               (181, 371), (172, 379), (153, 379), (142, 368))
    return surface_normal(height[::-1], metres / size)
