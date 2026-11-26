"""Independent numerical models of the diagnostic RGB exchanges."""
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


def tangent_pair(first, second, first_normal, second_normal, first_radius, second_radius):
    """Whether both surface planes accept a pair inside their common footprint."""
    apart = second - first
    first_tangent = apart - np.sum(apart * first_normal, axis=-1)[...,None] * first_normal
    second_tangent = apart - np.sum(apart * second_normal, axis=-1)[...,None] * second_normal
    distance_squared = np.maximum(np.sum(first_tangent**2, axis=-1), np.sum(second_tangent**2, axis=-1))
    radius = np.minimum(first_radius, second_radius)
    return distance_squared <= radius**2


def irregular(image, surface, edge_open, footprint=None):
    """Independent RGB exchange model; edge_open is indexed by the shared positive endpoint."""
    offsets = [(3,1),(-1,3),(4,-3),(5,1),(-2,6),(7,4),(-5,7),(9,-2),
               (3,10),(-8,8),(11,4),(0,-12)]
    result = image.copy()
    height, width = surface.shape

    def exchange(dx, dy, share, mask=None, side=1):
        p = np.s_[max(0,-dy):min(height,height-dy), max(0,-dx):min(width,width-dx)]
        q = np.s_[max(0,dy):min(height,height+dy), max(0,dx):min(width,width+dx)]
        agrees = surface[p] == surface[q]
        if footprint is not None:
            points, normals, radii = footprint
            agrees &= tangent_pair(points[p], points[q], normals[p], normals[q], radii[p], radii[q])
        if mask is not None:
            agrees &= mask[p if side > 0 else q]
        result[p] += share * agrees[...,None] * (image[q] - image[p])

    for dy in range(-1,2):
        for dx in range(-1,2):
            if dx or dy:
                exchange(dx,dy,1/16)
    for index,(dx,dy) in enumerate(offsets):
        for side in (-1,1):
            exchange(side*dx,side*dy,1/48,edge_open[index],side)
    return result
