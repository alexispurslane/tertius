"""Tool code. Measure retained native renderer frames without averaging display images.

    nix develop --command python3 -I tools/reflection_history_report.py <config.json> <new-output>

The config holds cases with name, frames, reference and width. Frame files are little-endian
scene-linear red radiance; reference files are achromatic RGB PFM images. No third-party packages.
"""

import argparse
import array
import hashlib
import json
import math
import struct
import sys
import zlib
from pathlib import Path


def central_width(values, width):
    columns = [sum(values[column::width]) for column in range(width)]
    total = sum(columns)
    if total <= 0:
        return None

    def crossing(fraction):
        cumulative = 0.0
        for column, value in enumerate(columns):
            if value > 0 and cumulative + value >= fraction * total:
                return column + (fraction * total - cumulative) / value
            cumulative += value
        return float(width)

    return crossing(0.95) - crossing(0.05)


def measure(frames, reference, width):
    count = len(reference)
    if len(frames) < 2 or count == 0 or width <= 0 or count % width:
        raise ValueError("Measurements need two complete frames and a nonempty rectangular reference.")
    for values in [reference, *frames]:
        if len(values) != count or any(not math.isfinite(value) or value < 0 for value in values):
            raise ValueError("Every frame must hold matching, finite, nonnegative scene-linear radiance.")
    reference_sum = sum(reference)
    if reference_sum <= 0:
        raise ValueError("Reference radiance must be nonzero for normalized light fractions.")
    mean = [sum(values) / len(frames) for values in zip(*frames)]
    variance = [
        sum((value - average) ** 2 for value in values) / (len(frames) - 1)
        for average, values in zip(mean, zip(*frames))
    ]
    error = [value - target for value, target in zip(mean, reference)]
    return {
        "frames": len(frames),
        "pixels": count,
        "noise_rms": math.sqrt(sum(variance) / count),
        "mean_error_rms": math.sqrt(sum(value * value for value in error) / count),
        "missing_fraction": sum(max(-value, 0) for value in error) / reference_sum,
        "extra_fraction": sum(max(value, 0) for value in error) / reference_sum,
        "total_fraction": sum(mean) / reference_sum,
        "reference_sum": reference_sum,
        "central_90_width_pixels": central_width(mean, width),
        "reference_central_90_width_pixels": central_width(reference, width),
        "mean": mean,
        "standard_deviation": [math.sqrt(value) for value in variance],
    }


def response(values, event_frame, target):
    if not 0 < event_frame < len(values):
        raise ValueError("The event must have both preceding and following measured draws.")
    before = sum(values[max(0, event_frame - 60):event_frame]) / min(event_frame, 60)
    step = abs(before - target)
    if step == 0:
        raise ValueError("Response requires a nonzero lighting change.")
    fractions = [abs(value - target) / step for value in values[event_frame:]]
    crossing = next((index + 1 for index, value in enumerate(fractions) if value <= 0.1), None)
    last_outside = max((index for index, value in enumerate(fractions) if value > 0.1), default=-1)
    persistent = last_outside + 2 if last_outside + 1 < len(fractions) else None
    return {
        "before_mean": before,
        "target": target,
        "first_90_percent_update": crossing,
        "persistent_90_percent_update": persistent,
        "remaining_step_fraction_after_ten_updates": fractions[9] if len(fractions) >= 10 else None,
        "observed_updates": len(fractions),
    }


def floats(data):
    values = array.array("f")
    values.frombytes(data)
    if sys.byteorder != "little":
        values.byteswap()
    return values


def read_reference(path, width):
    with path.open("rb") as source:
        if source.readline().strip() != b"PF":
            raise ValueError("Reference must be an RGB PFM image.")
        actual_width, height = map(int, source.readline().split())
        if actual_width != width or source.readline().strip() != b"-1.0":
            raise ValueError("Reference must have the declared width and little-endian unit scale.")
        values = floats(source.read())
    if len(values) != width * height * 3:
        raise ValueError("Reference does not contain complete RGB pixels.")
    red = []
    for row in range(height - 1, -1, -1):
        for column in range(width):
            index = (row * width + column) * 3
            if values[index] != values[index + 1] or values[index] != values[index + 2]:
                raise ValueError("Reference must be achromatic; this experiment measures red radiance.")
            red.append(values[index])
    return red, height


def write_png(path, values, width, exposure=-6.8):
    def display(value):
        exposed = max(0.0, value) * 2 ** exposure
        mapped = exposed / (1 + exposed)
        srgb = 12.92 * mapped if mapped <= 0.0031308 else 1.055 * mapped ** (1 / 2.4) - 0.055
        return max(0, min(255, round(srgb * 255)))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    pixels = bytes(display(value) for value in values)
    rows = b"".join(b"\0" + pixels[start:start + width] for start in range(0, len(pixels), width))
    header = struct.pack(">IIBBBBB", width, len(values) // width, 8, 0, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    arguments.output.mkdir(parents=True, exist_ok=False)
    config = json.loads(arguments.config.read_text())
    results = {
        "radiance_units": "W/(m^2 sr), scene-linear visible-plate outgoing specular red",
        "noise_definition": "RMS across pixels of sample standard deviation across correlated measured draws",
        "error_definition": "RMS across pixels of measured temporal mean minus saved one-bounce reference",
        "fraction_denominator": "Fixed whole-crop reference radiance sum; missing and extra do not cancel",
        "width_definition": "Crop-limited column-sum 5th-to-95th percentile pixel-cell width, native pixels",
        "display": "Common exposure -6.8 stops, Reinhard then sRGB; display does not affect measurements",
        "limits": ["Frames are temporally correlated", "Live source feedback differs from one-bounce reference"],
        "cases": {},
    }
    for case in config["cases"]:
        frame_path = Path(case["frames"])
        reference_path = Path(case["reference"])
        reference, height = read_reference(reference_path, case["width"])
        raw = floats(frame_path.read_bytes())
        count = len(reference)
        if len(raw) % count:
            raise ValueError(f"Incomplete measured frame in {frame_path}")
        frames = [raw[start:start + count] for start in range(0, len(raw), count)]
        result = measure(frames, reference, case["width"])
        write_png(arguments.output / f'{case["name"]}-mean.png', result.pop("mean"), case["width"])
        write_png(arguments.output / f'{case["name"]}-actual-last.png', frames[-1], case["width"])
        result.pop("standard_deviation")
        result["frames_sha256"] = hashlib.sha256(frame_path.read_bytes()).hexdigest()
        result["reference_sha256"] = hashlib.sha256(reference_path.read_bytes()).hexdigest()
        result["height"] = height
        results["cases"][case["name"]] = result
    (arguments.output / "metrics.json").write_text(json.dumps(results, indent=2) + "\n")


if __name__ == "__main__":
    main()
