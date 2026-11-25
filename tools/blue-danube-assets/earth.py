"""Cook the three aligned Earth maps on the native sphere's north-first, eastward UV grid."""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import zipfile

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates


def surface_maps(color, clouds, elevation):
    """Linear-light cloud compositing, dielectric roughness and metre-scale topographic normals."""
    rows = clouds.shape[0]
    # ETOPO1 has nodes on both poles and both ends of the dateline. The satellite map has cell centres.
    y, x = np.indices(clouds.shape)
    sample = [(y + 0.5) * (elevation.shape[0] - 1) / rows,
              (x + 0.5) * (elevation.shape[1] - 1) / clouds.shape[1]]
    height = map_coordinates(elevation.astype(np.float32), sample, order=1, mode="nearest")
    water = (height <= 0) & (color[..., 2] > color[..., 0])
    roughness = np.where(water, 0.19, 0.78) * (1 - clouds) + 0.94 * clouds

    # cross(normal, east) points north on Primitives.Sphere. A hill rising toward the south tilts north.
    height = np.maximum(height, 0)
    radians = np.pi / rows
    metres = 6371000 * radians
    east_spacing = metres * np.sin((np.arange(rows) + 0.5) * radians)[:, None]
    east = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / (2 * east_spacing)
    south = np.gradient(height, metres, axis=0)
    land = (1 - clouds) * ~water
    normal = np.stack((-east * land, south * land, np.ones_like(height)), axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return {"roughness": roughness, "normal": normal * 0.5 + 0.5}, water


def cloud_color(color, clouds, water):
    """Mix the land mosaic and cloud cover in linear light."""
    # Convert the image to linear reflectance before mixing it with cloud droplets.
    linear = np.where(color <= 0.04045, color / 12.92, ((color + 0.055) / 1.055) ** 2.4)
    # NASA uses a nearly black (2, 5, 20) sRGB offshore swatch. Approximate deep-water diffuse
    # reflectance in linear RGB; coastal water retains the brighter colours of the satellite mosaic.
    water_reflectance = np.array([0.002, 0.012, 0.033], dtype=np.float32)
    linear += np.maximum(0, water_reflectance - linear) * water[..., None]
    linear += (0.76 - linear) * clouds[..., None]
    albedo = np.where(linear <= 0.0031308, 12.92 * linear, 1.055 * linear ** (1 / 2.4) - 0.055)
    return albedo


def polar_border(image, normal):
    """Reflect 1/64 of the map across each pole so repeat filtering samples the same hemisphere."""
    width, height = image.size
    border = height // 64
    extended = Image.new(image.mode, (width, height + 2 * border))
    extended.paste(image, (0, border))
    for y, dest_y in ((0, 0), (height - border, height + border)):
        cap = image.crop((0, y, width, y + border)).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        if normal:
            # Both tangent axes reverse when crossing a pole and continuing on the opposite meridian.
            red, green, blue = cap.split()
            cap = Image.merge("RGB", (red.point(lambda v: 255 - v), green.point(lambda v: 255 - v), blue))
        extended.paste(cap.crop((width // 2, 0, width, border)), (0, dest_y))
        extended.paste(cap.crop((0, 0, width // 2, border)), (width // 2, dest_y))
    return extended


def cook(source_record, cache, dest, staging, fetch, force):
    source = json.loads(source_record.read_text())
    fetch.check_approval(source)
    for name, entry in source["files"].items():
        fetch.ensure_file(entry, cache / name, staging, fetch.http_download)
    digest = hashlib.sha256(source_record.read_bytes() + Path(__file__).read_bytes()).hexdigest().encode()
    if not force and dest.exists():
        try:
            with zipfile.ZipFile(dest) as archive:
                if archive.comment == digest and archive.testzip() is None:
                    return
        except zipfile.BadZipFile:
            pass
    base = Image.open(cache / "june.jpg").convert("RGB").resize((5376, 2688), Image.Resampling.LANCZOS)
    clouds = Image.open(cache / "clouds8.jpg").convert("L")
    def values(image):
        return np.asarray(image, dtype=np.float32) / 255

    # DAP2 encodes Int16 arrays as big-endian 32-bit XDR words after two array counts.
    payload = (cache / "elevation.dods").read_bytes().split(b"Data:\n", 1)[1]
    elevation = np.frombuffer(payload, dtype=">i4", offset=8, count=1081 * 2161).reshape(1081, 2161)[::-1]
    terrain_size = (2048, 1024)
    maps, water = surface_maps(values(base.resize(terrain_size, Image.Resampling.LANCZOS)),
                        values(clouds.resize(terrain_size, Image.Resampling.LANCZOS)), elevation)
    water = Image.fromarray(np.uint8(water) * 255).resize(base.size, Image.Resampling.BILINEAR)
    cover = clouds.resize(base.size, Image.Resampling.LANCZOS)
    # Processing strips caps the peak working set while mixing the 14.6 million colour samples.
    albedo = Image.new("RGB", base.size)
    for top in range(0, base.height, 128):
        box = (0, top, base.width, min(base.height, top + 128))
        mixed = cloud_color(values(base.crop(box)), values(cover.crop(box)), values(water.crop(box)))
        albedo.paste(Image.fromarray(np.rint(mixed * 255).astype(np.uint8)), (0, top))
    images = {name: Image.fromarray(np.rint(values * 255).astype(np.uint8)) for name, values in maps.items()}
    images["color"] = albedo
    dest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=dest.parent) as folder:
        temporary = Path(folder) / dest.name
        with zipfile.ZipFile(temporary, "w") as archive:
            archive.comment = digest
            archive.writestr(zipfile.ZipInfo("sources.json"), source_record.read_bytes())
            for name, image in images.items():
                with archive.open(zipfile.ZipInfo(name + ".png"), "w") as png:
                    polar_border(image, normal=name == "normal").save(png, format="PNG")
        os.replace(temporary, dest)
