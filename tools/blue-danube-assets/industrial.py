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
