"""Throwaway (t1-g4p1): the engine's exposure, tone curve and sRGB encoding (tonemap.glsl, srgb.glsl), and their inverse.

display(linear, stops) gives the 8-bit image that the engine writes for scene-linear radiance at a fixed exposure of
`stops`, with bloom off. scene_from_display(rgb8, stops) inverts it: the 8-bit sRGB image back to scene-linear radiance.
The ACES fit clamps to [0, 1], so an 8-bit 255 or 0 inverts to the edge of the range only.
"""
import numpy as np

ACES_IN = np.array([[0.59719, 0.35458, 0.04823], [0.07600, 0.90834, 0.01566], [0.02840, 0.13383, 0.83777]])
ACES_OUT = np.array([[1.60475, -0.53108, -0.07367], [-0.10208, 1.10813, -0.00605], [-0.00327, -0.07276, 1.07602]])


def rrt_odt(v):
    return (v * (v + 0.0245786) - 0.000090537) / (v * (0.983729 * v + 0.4329510) + 0.238081)


def rrt_odt_inverse(y):
    # Solve y (0.983729 v^2 + 0.432951 v + 0.238081) = v^2 + 0.0245786 v - 0.000090537 for the root v >= 0.
    a = 0.983729 * y - 1.0
    b = 0.4329510 * y - 0.0245786
    c = 0.238081 * y + 0.000090537
    disc = np.sqrt(np.maximum(b * b - 4 * a * c, 0.0))
    return (-b - disc) / (2 * a)


def linear_to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def srgb_to_linear(s):
    return np.where(s <= 0.04045, s / 12.92, np.power((s + 0.055) / 1.055, 2.4))


def display(linear, stops):
    hdr = np.maximum(linear * 2.0 ** stops, 0.0)
    ldr = np.clip(rrt_odt(hdr @ ACES_IN.T) @ ACES_OUT.T, 0.0, 1.0)
    return np.round(linear_to_srgb(ldr) * 255.0).astype(np.uint8)


def scene_from_display(rgb8, stops):
    ldr = srgb_to_linear(rgb8.astype(np.float64) / 255.0)
    fitted = ldr @ np.linalg.inv(ACES_OUT).T
    hdr = rrt_odt_inverse(fitted) @ np.linalg.inv(ACES_IN).T
    return np.maximum(hdr, 0.0) / 2.0 ** stops


def luminance(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722
