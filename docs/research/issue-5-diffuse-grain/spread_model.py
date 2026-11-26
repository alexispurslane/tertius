"""Reference of the diagnostic's fixed 5x5, uniform, symmetric exchange."""
import numpy as np


def spread(image, surface, step):
    result = image.copy()
    height, width = surface.shape
    for y in range(-2, 3):
        for x in range(-2, 3):
            if x == 0 and y == 0:
                continue
            dy, dx = y * step, x * step
            p = np.s_[max(0, -dy):min(height, height - dy), max(0, -dx):min(width, width - dx)]
            q = np.s_[max(0, dy):min(height, height + dy), max(0, dx):min(width, width + dx)]
            agrees = (surface[p] == surface[q])[..., None]
            result[p] += agrees * (image[q] - image[p]) / 25
    return result
