"""CPU-only quadrature of the lit-block-back face's diffuse bounce."""

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import time

import numpy as np

LUMINANCE = np.array([0.2126, 0.7152, 0.0722])
EYE = np.array([1.0, 1.5, 4.5])
NORMAL = np.array([0.0, 0.0, -1.0])
ALBEDO = 0.7
ROOT = Path(__file__).resolve().parents[4]
ARCHIVE = ROOT / "docs/research/back-face-depth-layer/stage-2"


def segment_visibility(sources, receivers, boxes):
    """Return [receiver, sender] visibility through open segments and box interiors."""
    direction = receivers[:, None, :] - sources[None, :, :]
    visible = np.ones(direction.shape[:2], dtype=bool)
    for lower, upper in boxes:
        entry = np.full(direction.shape[:2], -np.inf)
        exit_at = np.full(direction.shape[:2], np.inf)
        possible = np.ones(direction.shape[:2], dtype=bool)
        for axis in range(3):
            delta = direction[:, :, axis]
            parallel = np.abs(delta) < 1e-12
            origin = sources[None, :, axis]
            possible &= ~(parallel & ((origin <= lower[axis] + 1e-8) | (origin >= upper[axis] - 1e-8)))
            first = np.divide(lower[axis] - origin, delta, out=np.zeros_like(delta), where=~parallel)
            second = np.divide(upper[axis] - origin, delta, out=np.zeros_like(delta), where=~parallel)
            entry = np.maximum(entry, np.where(parallel, -np.inf, np.minimum(first, second)))
            exit_at = np.minimum(exit_at, np.where(parallel, np.inf, np.maximum(first, second)))
        visible &= ~(possible & (np.minimum(exit_at, 1 - 1e-6) > np.maximum(entry, 1e-6)))
    return visible


def patch_irradiance(sources, normal, radiance_area, receivers, normals, boxes=()):
    """Return each sender's incident irradiance at each receiver, in W/m²."""
    displacement = receivers[:, None, :] - sources[None, :, :]
    distance_squared = np.sum(displacement * displacement, axis=-1)
    direction = displacement / np.sqrt(distance_squared[:, :, None])
    emission_cosine = np.maximum(direction @ normal, 0)
    receiving_cosine = np.maximum(-np.sum(direction * normals[:, None, :], axis=-1), 0)
    contribution = radiance_area[None, :] * emission_cosine * receiving_cosine / distance_squared
    if len(boxes):
        contribution *= segment_visibility(sources, receivers, boxes)
    return contribution


def spot_irradiance(points, normals, light):
    """Point-spot irradiance using the engine's cosine-domain smoothstep cone."""
    displacement = points - np.array(light["position"])
    distance_squared = np.sum(displacement * displacement, axis=-1)
    direction = displacement / np.sqrt(distance_squared[:, None])
    cone_cosine = direction @ np.array(light["axis"])
    ramp = np.clip((cone_cosine - light["cosOuter"]) / (light["cosInner"] - light["cosOuter"]), 0, 1)
    falloff = ramp * ramp * (3 - 2 * ramp)
    receiving_cosine = np.maximum(-np.sum(direction * normals, axis=-1), 0)
    return float(np.array(light["intensity"]) @ LUMINANCE) * falloff * receiving_cosine / distance_squared


def exported_boxes(folder, scene):
    boxes = []
    for instance in scene["instances"]:
        if not instance["flags"] & 2:
            continue
        if instance["rotation"] != [0, 0, 0, 1] or instance["scale"] != [1, 1, 1]:
            raise ValueError("The experiment requires axis-aligned unscaled boxes")
        data = (folder / f"mesh-{instance['mesh']}.bin").read_bytes()
        vertex_count, _ = struct.unpack_from("<ii", data)
        vertices = np.frombuffer(data, dtype="<f4", count=3 * vertex_count, offset=8).reshape(-1, 3)
        position = np.array(instance["position"]) + EYE
        boxes.append((vertices.min(axis=0) + position, vertices.max(axis=0) + position))
    if len(boxes) != 7:
        raise ValueError("Expected six room slabs and one block")
    return boxes


def senders(count, block, light):
    lower, upper = block
    radius = (lower[2] - light["position"][2]) * math.tan(light["outerAngle"])
    left = max(lower[0], light["position"][0] - radius)
    right = min(upper[0], light["position"][0] + radius)
    bottom = max(lower[1], light["position"][1] - radius)
    top = min(upper[1], light["position"][1] + radius)
    horizontal, vertical = np.meshgrid(
        left + (np.arange(count) + 0.5) * (right - left) / count,
        bottom + (np.arange(count) + 0.5) * (top - bottom) / count)
    points = np.column_stack([horizontal.ravel(), vertical.ravel(), np.full(count * count, lower[2])])
    area = (right - left) * (top - bottom) / (count * count)
    incident = spot_irradiance(points, np.tile(NORMAL, (len(points), 1)), light)
    radiance_area = ALBEDO * incident * area / math.pi
    return points, radiance_area, {
        "grid": count, "active_patches": int(np.count_nonzero(incident)), "patch_area_m2": area,
        "reflected_flux_W": float(np.sum(radiance_area) * math.pi),
        "largest_reflected_patch_flux_W": float(np.max(radiance_area) * math.pi)}


def sum_region(points, normals, patches, radiance_area, boxes):
    unoccluded = patch_irradiance(patches, NORMAL, radiance_area, points, normals)
    visible = unoccluded * segment_visibility(patches, points, boxes)
    totals = visible.sum(axis=1)
    mean = float(totals.mean())
    mean_without = float(unoccluded.sum(axis=1).mean())
    return {
        "irradiance_visible_W_m2": mean, "irradiance_unoccluded_W_m2": mean_without,
        "radiance_visible_W_m2_sr": mean * ALBEDO / math.pi,
        "radiance_unoccluded_W_m2_sr": mean_without * ALBEDO / math.pi,
        "largest_patch_share_of_region_mean": float(visible.mean(axis=0).max() / mean) if mean else 0,
        "largest_patch_share_at_any_receiver": float(np.max(np.divide(
            visible.max(axis=1), totals, out=np.zeros_like(totals), where=totals > 0))),
        "max_blocked_irradiance_W_m2": float(np.max(unoccluded.sum(axis=1) - totals))}


def load_regions(view, evidence):
    spec = importlib.util.spec_from_file_location("archived_regions", ARCHIVE / "tools/lit-block/regions.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    normal = np.load(evidence / "ref/geo-normal.npy", mmap_mode="r")
    position = np.load(evidence / "ref/geo-position.npy", mmap_mode="r")
    masks = {name: module.region_mask(region, normal, position) for name, region in view["regions"].items()}
    return normal, position, masks


def reference_values(evidence, masks):
    result = {}
    for lighting in ("front", "back"):
        means = []
        for seed in (1, 2):
            image = np.load(evidence / f"ref/{lighting}-b1-s{seed}.npy", mmap_mode="r")
            means.append({name: float(np.mean(image[mask] @ LUMINANCE)) for name, mask in masks.items()})
        result[lighting] = means
    return result


def receiver_samples(indices, position, normal, count):
    selected = indices[((np.arange(min(count, len(indices))) + 0.5) * len(indices) / min(count, len(indices))).astype(int)]
    return (position.reshape(-1, 3)[selected].astype(float) + EYE,
            normal.reshape(-1, 3)[selected].astype(float))


def main():
    started = time.perf_counter()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("results.json"))
    parser.add_argument("--samples", type=int, default=2048)
    args = parser.parse_args()
    if args.samples < 1:
        parser.error("--samples must be positive")
    view = json.loads((ARCHIVE / "tools/lit-block/views-lit-block.json").read_text())["lit-block"]
    evidence = args.evidence or Path(view["geometry"]["normal"]).parents[1]
    folder = evidence / "export/back"
    scene_file = folder / "scene.json"
    scene = json.loads(scene_file.read_text())
    boxes = exported_boxes(folder, scene)
    light = dict(scene["lights"][0])
    light["position"] = (np.array(light["position"]) + EYE).tolist()
    normal, position, masks = load_regions(view, evidence)
    references = reference_values(evidence, masks)
    denominator = float(np.mean([seed[view["neutral"]] for seed in references["front"]]))
    names = ["back wall above the block", "floor behind, right gap"]
    samples = {}
    dense_samples = {}
    for name in names:
        indices = np.flatnonzero(masks[name])
        samples[name] = receiver_samples(indices, position, normal, args.samples)
        dense_samples[name] = receiver_samples(indices, position, normal, args.samples * 4)
    report = {
        "evidence": str(evidence), "scene_sha256": hashlib.sha256(scene_file.read_bytes()).hexdigest(),
        "camera_world_m": EYE.tolist(), "camera": scene["camera"], "key_world": light,
        "boxes_world_m": [[lower.tolist(), upper.tolist()] for lower, upper in boxes],
        "neutral_radiance_W_m2_sr": denominator, "receiver_samples": args.samples,
        "receiver_sampling": "Midpoints of equal-count strata of row-major masked pixels",
        "region_definitions": {name: view["regions"][name] for name in names},
        "reference": {}, "convergence": [], "receiver_convergence": {}}
    for name in names:
        seed_means = [seed[name] for seed in references["back"]]
        mean = float(np.mean(seed_means))
        all_points = position[masks[name]].astype(float) + EYE
        report["reference"][name] = {
            "pixel_count": int(masks[name].sum()), "world_min_m": all_points.min(axis=0).tolist(),
            "world_max_m": all_points.max(axis=0).tolist(), "seed_radiances_W_m2_sr": seed_means,
            "radiance_W_m2_sr": mean, "normalized": mean / denominator}
    for count in (8, 16, 32):
        patches, radiance_area, flux = senders(count, boxes[-1], light)
        row = {"senders": flux, "regions": {}}
        for name, (points, normals) in samples.items():
            values = sum_region(points[:args.samples], normals[:args.samples], patches, radiance_area, boxes)
            values["normalized_visible"] = values["radiance_visible_W_m2_sr"] / denominator
            values["normalized_unoccluded"] = values["radiance_unoccluded_W_m2_sr"] / denominator
            values["ratio_to_reference"] = values["normalized_visible"] / report["reference"][name]["normalized"]
            row["regions"][name] = values
        report["convergence"].append(row)
    for name, (points, normals) in dense_samples.items():
        report["receiver_convergence"][name] = sum_region(points, normals, patches, radiance_area, boxes)
    translation = np.array([0.0, 0.0, -10.2])
    two_rooms = boxes + [(lower + translation, upper + translation) for lower, upper in boxes[:6]]
    probes = {
        "dark_floor_center": ([-2, 0, -9.2], [0, 1, 0]),
        "dark_far_wall_center": ([-2, 2, -14.2], [0, 0, 1]),
        "lit_floor_center": ([-2, 0, 1], [0, 1, 0]),
        "lit_rear_half_floor_center": ([-2, 0, -2.375], [0, 1, 0]),
        "lit_back_wall_center": ([-2, 2, -4], [0, 0, 1]),
        "camera_side_floor": ([0, 0, 2], [0, 1, 0])}
    report["probes"] = {}
    for name, (point, facing) in probes.items():
        values = sum_region(np.array([point]), np.array([facing]), patches, radiance_area, two_rooms)
        report["probes"][name] = {"position_m": point, "normal": facing, **values}
    report["leak_ratios"] = {}
    for dark, lit in (("dark_floor_center", "lit_rear_half_floor_center"),
                      ("dark_far_wall_center", "lit_back_wall_center")):
        if report["probes"][dark]["irradiance_visible_W_m2"] != 0:
            raise AssertionError("A closed room must block all senders")
        report["leak_ratios"][dark] = (report["probes"][dark]["irradiance_unoccluded_W_m2"] /
                                      report["probes"][lit]["irradiance_unoccluded_W_m2"])
    report["elapsed_seconds"] = time.perf_counter() - started
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({"seconds": report["elapsed_seconds"], "neutral": denominator,
                      "regions": report["convergence"][-1]["regions"], "leaks": report["leak_ratios"]}, indent=2))


if __name__ == "__main__":
    main()
