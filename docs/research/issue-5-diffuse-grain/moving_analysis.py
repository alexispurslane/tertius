"""Analyze continuous-motion cockpit renders, fixed-pose multi-phase references,
and physical-sheet isolated-source accumulation vs area-aware no-cutoff heat flow.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter

from measure import grain, compare, color_wash
from multiscale import fluctuations
from physical_sheet import (
    load_cockpit_panel_sheet,
    build_physical_grid,
    build_heat_operator,
    area_aware_heat,
    unproject_to_sheet,
    accumulate_on_sheet,
    batch_convergence,
)

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float64)


def srgb_to_linear(img_u8):
    x = img_u8.astype(np.float64) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def load_png_linear(path: Path):
    return srgb_to_linear(np.asarray(Image.open(path).convert('RGB')))


def load_draws(folder: Path):
    return [json.loads(line) for line in (folder / 'draws.jsonl').read_text().splitlines() if line.strip()]


def load_raw_draw(raw_dir: Path, global_draw: int, width: int, height: int):
    stem = raw_dir / f'{global_draw:06d}'
    src = np.fromfile(str(stem) + '.source.f32', dtype='<f4').reshape(height, width, 4)
    dist = np.fromfile(str(stem) + '.distance.f32', dtype='<f4').reshape(height, width)
    return src[..., :3].astype(np.float64), src[..., 3].astype(np.float64), dist.astype(np.float64)


def cam_dict(rec):
    return dict(
        eye=rec['eye'],
        forward=rec['forward'],
        right_tan=rec['rightTan'],
        up_tan=rec['upTan'],
        jitter=rec['jitter'],
        width=rec['width'],
        height=rec['height'],
    )


def sheet_local_patches(grid, mask):
    """Split the observed physical sheet bounding box into 4 physical quadrants (warm/cool regions)."""
    ys, xs = np.where(mask)
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    ym, xm = (y0 + y1) // 2, (x0 + x1) // 2
    boxes = {
        'sheet-q00': (y0, ym, x0, xm),
        'sheet-q01': (y0, ym, xm, x1),
        'sheet-q10': (ym, y1, x0, xm),
        'sheet-q11': (ym, y1, xm, x1),
    }
    return boxes


def evaluate_against_ref(est, ref, mask, areas, sigma_cells=4.0):
    """Compare a physical-sheet RGB estimate against the 128-draw converged reference on mask."""
    est = np.asarray(est, dtype=np.float64)
    ref = np.asarray(ref, dtype=np.float64)
    mask = np.asarray(mask, dtype=bool)
    w = areas[mask]
    w_sum = float(w.sum())
    mean_est = (est[mask] * w[:, None]).sum(axis=0) / max(w_sum, 1e-20)
    mean_ref = (ref[mask] * w[:, None]).sum(axis=0) / max(w_sum, 1e-20)
    rel_rms = float(
        100.0
        * np.sqrt(
            np.sum(((est[mask] - ref[mask]) ** 2) * w[:, None])
            / max(np.sum((ref[mask] ** 2) * w[:, None]), 1e-20)
        )
    )
    w_mask = gaussian_filter(mask.astype(np.float64), sigma=sigma_cells, mode='constant')
    safe = mask & (w_mask > 1e-6)
    we = gaussian_filter(np.where(mask[..., None], est, 0.0), sigma=(sigma_cells, sigma_cells, 0.0), mode='constant')
    wr = gaussian_filter(np.where(mask[..., None], ref, 0.0), sigma=(sigma_cells, sigma_cells, 0.0), mode='constant')
    we = np.divide(we, w_mask[..., None], out=np.zeros_like(we), where=safe[..., None])
    wr = np.divide(wr, w_mask[..., None], out=np.zeros_like(wr), where=safe[..., None])
    ws = areas[safe]
    wash_err = float(
        100.0
        * np.sqrt(
            np.sum(((we[safe] - wr[safe]) ** 2) * ws[:, None])
            / max(np.sum((wr[safe] ** 2) * ws[:, None]), 1e-20)
        )
    )
    patches = {}
    for pname, (r0, r1, c0, c1) in sheet_local_patches(None, safe).items():
        pm = safe[r0:r1, c0:c1]
        if not np.any(pm):
            continue
        pw = areas[r0:r1, c0:c1][pm]
        pe = we[r0:r1, c0:c1][pm]
        pr = wr[r0:r1, c0:c1][pm]
        patches[pname] = dict(
            cells=int(pm.sum()),
            ref_mean_rgb=((ref[r0:r1, c0:c1][pm] * pw[:, None]).sum(axis=0) / pw.sum()).tolist(),
            est_mean_rgb=((est[r0:r1, c0:c1][pm] * pw[:, None]).sum(axis=0) / pw.sum()).tolist(),
            wash_relative_rms_percent=float(
                100.0 * np.sqrt(np.sum(((pe - pr) ** 2) * pw[:, None]) / max(np.sum((pr ** 2) * pw[:, None]), 1e-20))
            ),
        )
    return dict(
        area_weighted_mean_rgb_ref=mean_ref.tolist(),
        area_weighted_mean_rgb_est=mean_est.tolist(),
        area_weighted_relative_rms_percent=rel_rms,
        area_weighted_wash_relative_rms_percent=wash_err,
        patches=patches,
    )


def sheet_bands(frames, mask, scales=(1, 2, 4, 8)):
    """Masked Gaussian difference-of-Gaussians RGB fluctuation bands on the physical sheet."""
    frames = np.asarray(frames, dtype=np.float64)
    if frames.ndim == 3:
        frames = frames[None, ...]
    mask = np.asarray(mask, dtype=bool)
    out = {}
    for s in scales:
        w1 = gaussian_filter(mask.astype(np.float64), sigma=s, mode='constant')
        w2 = gaussian_filter(mask.astype(np.float64), sigma=2 * s, mode='constant')
        safe = mask & (w1 > 0.5) & (w2 > 0.5)
        if not np.any(safe):
            continue
        b1 = gaussian_filter(np.where(mask[None, ..., None], frames, 0.0), sigma=(0, s, s, 0), mode='constant')
        b2 = gaussian_filter(np.where(mask[None, ..., None], frames, 0.0), sigma=(0, 2 * s, 2 * s, 0), mode='constant')
        b1 = np.divide(b1, w1[None, ..., None], out=np.zeros_like(b1), where=safe[None, ..., None])
        b2 = np.divide(b2, w2[None, ..., None], out=np.zeros_like(b2), where=safe[None, ..., None])
        vals = (b1 - b2)[:, safe, :]
        persistent = vals.mean(axis=0)
        temporal = vals - persistent
        out[str(s)] = dict(
            cells=int(safe.sum()),
            rgb_rms=float(np.sqrt(np.mean(vals ** 2))),
            persistent_rgb_rms=float(np.sqrt(np.mean(persistent ** 2))),
            temporal_rgb_rms=float(np.sqrt(np.mean(temporal ** 2))),
        )
    return out


def encode_log(rgb):
    return np.uint8(np.clip(np.log1p(np.maximum(rgb, 0.0) * 100.0) / np.log(11.0), 0.0, 1.0) * 255.0)


def analyze_fixed_references(capture_root: Path, sheet, grid):
    refs = {}
    summary = {}
    for pose_name, folder_name in [('P0', 'fixed-ref-P0'), ('P1', 'fixed-ref-P1'), ('P2', 'fixed-ref-P2')]:
        folder = capture_root / folder_name
        draws = load_draws(folder)
        settled = [d for d in draws if d['sampleDraw'] >= 32]
        resets_after_0 = sum(1 for d in draws[1:] if d['reset'])
        assert resets_after_0 == 0, f'{pose_name} had unexpected reset'
        raw_dir = folder / 'raw'
        w, h = map(int, (raw_dir / 'size.txt').read_text().split())

        cell_frames = []
        obs_frames = []
        pix_areas_all = []
        for rec in settled:
            rgb, _ho, dist = load_raw_draw(raw_dir, rec['globalDraw'], w, h)
            c_rgb, c_obs, _c_den, p_mask, _p_rc, p_area = unproject_to_sheet(rgb, dist, cam_dict(rec), sheet, grid)
            cell_frames.append(c_rgb)
            obs_frames.append(c_obs)
            if len(pix_areas_all) == 0:
                pix_areas_all = p_area[p_mask]

        cell_frames = np.stack(cell_frames)
        obs_frames = np.stack(obs_frames)
        stable_mask = obs_frames.mean(axis=0) >= 0.95

        n = len(cell_frames)
        half = n // 2
        mean_all = np.divide(
            (cell_frames * obs_frames[..., None]).sum(axis=0),
            obs_frames.sum(axis=0)[..., None],
            out=np.zeros_like(cell_frames[0]),
            where=obs_frames.sum(axis=0)[..., None] > 0,
        )
        mean_early = np.divide(
            (cell_frames[:half] * obs_frames[:half, ..., None]).sum(axis=0),
            obs_frames[:half].sum(axis=0)[..., None],
            out=np.zeros_like(cell_frames[0]),
            where=obs_frames[:half].sum(axis=0)[..., None] > 0,
        )
        mean_late = np.divide(
            (cell_frames[half:] * obs_frames[half:, ..., None]).sum(axis=0),
            obs_frames[half:].sum(axis=0)[..., None],
            out=np.zeros_like(cell_frames[0]),
            where=obs_frames[half:].sum(axis=0)[..., None] > 0,
        )
        mean_even = np.divide(
            (cell_frames[0::2] * obs_frames[0::2, ..., None]).sum(axis=0),
            obs_frames[0::2].sum(axis=0)[..., None],
            out=np.zeros_like(cell_frames[0]),
            where=obs_frames[0::2].sum(axis=0)[..., None] > 0,
        )
        mean_odd = np.divide(
            (cell_frames[1::2] * obs_frames[1::2, ..., None]).sum(axis=0),
            obs_frames[1::2].sum(axis=0)[..., None],
            out=np.zeros_like(cell_frames[0]),
            where=obs_frames[1::2].sum(axis=0)[..., None] > 0,
        )

        refs[pose_name] = dict(
            mean=mean_all,
            stable_mask=stable_mask,
            mean_early=mean_early,
            mean_late=mean_late,
            mean_even=mean_even,
            mean_odd=mean_odd,
            tick=settled[0]['tick'],
            eye=settled[0]['eye'],
            sample_draws=[settled[0]['sampleDraw'], settled[-1]['sampleDraw']],
        )
        summary[pose_name] = dict(
            tick=settled[0]['tick'],
            eye=settled[0]['eye'],
            total_draws=len(draws),
            settled_draws=n,
            sample_draw_range=[settled[0]['sampleDraw'], settled[-1]['sampleDraw']],
            resets_after_draw_0=resets_after_0,
            stable_sheet_cells=int(stable_mask.sum()),
            observed_sheet_area_m2=float(grid.areas[stable_mask].sum()),
            total_panel_sheet_area_m2=float(grid.areas[grid.active].sum()),
            pixel_physical_area_cm2_min_median_max=[
                float(np.quantile(pix_areas_all, 0.01) * 1e4),
                float(np.median(pix_areas_all) * 1e4),
                float(np.quantile(pix_areas_all, 0.99) * 1e4),
            ],
            pixel_area_p99_over_p01_ratio=float(
                np.quantile(pix_areas_all, 0.99) / max(np.quantile(pix_areas_all, 0.01), 1e-12)
            ),
            early_vs_late_64_draws=batch_convergence(mean_early, mean_late, stable_mask),
            even_vs_odd_64_draws=batch_convergence(mean_even, mean_odd, stable_mask),
            reference_bands_physical_cells=sheet_bands(mean_all, stable_mask),
        )

    # Cross-pose view-dependence check on common observed sheet cells
    common_mask = refs['P0']['stable_mask'] & refs['P1']['stable_mask'] & refs['P2']['stable_mask']
    summary['cross_pose_view_change'] = dict(
        common_cells=int(common_mask.sum()),
        common_area_m2=float(grid.areas[common_mask].sum()),
        P0_vs_P1=batch_convergence(refs['P0']['mean'], refs['P1']['mean'], common_mask),
        P1_vs_P2=batch_convergence(refs['P1']['mean'], refs['P2']['mean'], common_mask),
        P0_vs_P2=batch_convergence(refs['P0']['mean'], refs['P2']['mean'], common_mask),
    )
    return refs, summary


def analyze_moving_sheet(capture_root: Path, out_dir: Path, sheet, grid, refs):
    folder = capture_root / 'moving-source'
    draws = load_draws(folder)
    cap_draws = [d for d in draws if d['captured']]
    resets_after_0 = sum(1 for d in draws[1:] if d['reset'])
    assert resets_after_0 == 0
    raw_dir = folder / 'raw'
    w, h = map(int, (raw_dir / 'size.txt').read_text().split())

    cell_frames = []
    obs_frames = []
    pixel_masks = []
    pixel_rcs = []
    for rec in cap_draws:
        rgb, _ho, dist = load_raw_draw(raw_dir, rec['globalDraw'], w, h)
        c_rgb, c_obs, _c_den, p_mask, p_rc, _p_area = unproject_to_sheet(rgb, dist, cam_dict(rec), sheet, grid)
        cell_frames.append(c_rgb)
        obs_frames.append(c_obs)
        pixel_masks.append(p_mask)
        pixel_rcs.append(p_rc)

    cell_frames = np.stack(cell_frames)
    obs_frames = np.stack(obs_frames)
    n_frames = len(cell_frames)

    # Build per-draw area-aware no-cutoff heat flow on each frame's observed connected sheet
    # (insulating at true windshield boundary AND at Cabin_Liner occlusion edge so energy never drains into unobserved zero cells).
    controls = {'raw-per-draw': cell_frames}
    conservation_drifts = {}

    # Pre-build operators for observed masks? Wait: observed mask moves slightly frame to frame;
    # building csr_matrix for ~2500 observed cells takes ~1 ms per frame!
    for width_cm in (4, 8, 16):
        tau = (width_cm * 0.01) ** 2 / 4.0
        out = np.zeros_like(cell_frames)
        max_drift = 0.0
        for t in range(n_frames):
            obs = obs_frames[t]
            op = build_heat_operator(obs, grid.areas, grid.spacing)
            diffused = area_aware_heat(cell_frames[t], obs, grid.areas, grid.spacing, tau, operator=op)
            before = np.sum(cell_frames[t, obs] * grid.areas[obs, None], axis=0)
            after = np.sum(diffused[obs] * grid.areas[obs, None], axis=0)
            max_drift = max(max_drift, float(np.max(np.abs(after - before))))
            out[t] = diffused
        name = f'per-draw-heat-{width_cm}cm'
        controls[name] = out
        conservation_drifts[name] = max_drift

    for win in (16, 32, 64):
        controls[f'physical-accum-{win}'] = accumulate_on_sheet(cell_frames, obs_frames, window=win)

    for win in (32, 64):
        base = controls[f'physical-accum-{win}']
        tau = (4 * 0.01) ** 2 / 4.0
        out = np.zeros_like(base)
        max_drift = 0.0
        for t in range(n_frames):
            obs = obs_frames[t]
            op = build_heat_operator(obs, grid.areas, grid.spacing)
            diffused = area_aware_heat(base[t], obs, grid.areas, grid.spacing, tau, operator=op)
            before = np.sum(base[t, obs] * grid.areas[obs, None], axis=0)
            after = np.sum(diffused[obs] * grid.areas[obs, None], axis=0)
            max_drift = max(max_drift, float(np.max(np.abs(after - before))))
            out[t] = diffused
        name = f'physical-accum{win}-heat4cm'
        controls[name] = out
        conservation_drifts[name] = max_drift

    # Evaluate every control on:
    # 1) Moving sequence frames 32..95 on stably observed sheet cells across those frames
    # 2) Direct comparison against the 128-draw converged fixed references at P1 (frame 48) and P2 (frame 95)
    seq_mask = obs_frames[32:].mean(axis=0) >= 0.95
    sheet_results = dict(
        grid_spacing_m=grid.spacing,
        total_active_cells=int(grid.active.sum()),
        total_panel_area_m2=float(grid.areas[grid.active].sum()),
        moving_sequence_frames=[32, n_frames - 1],
        moving_sequence_common_cells=int(seq_mask.sum()),
        moving_sequence_common_area_m2=float(grid.areas[seq_mask].sum()),
        resets_after_draw_0=resets_after_0,
        captured_sample_draw_range=[cap_draws[0]['sampleDraw'], cap_draws[-1]['sampleDraw']],
        controls={},
    )
    for name, arr in controls.items():
        m_p1 = refs['P1']['stable_mask'] & obs_frames[48]
        m_p2 = refs['P2']['stable_mask'] & obs_frames[95]
        sheet_results['controls'][name] = dict(
            max_physical_area_weighted_rgb_drift=conservation_drifts.get(name, 0.0),
            bands_frames_32_to_95=sheet_bands(arr[32:], seq_mask),
            vs_fixed_ref_P1_frame48=evaluate_against_ref(arr[48], refs['P1']['mean'], m_p1, grid.areas),
            vs_fixed_ref_P2_frame95=evaluate_against_ref(arr[95], refs['P2']['mean'], m_p2, grid.areas),
        )

    # Render physical-sheet visual comparison at P2 (frame 95) — zoomed to active observed bounding box
    ys, xs = np.where(obs_frames[95] | refs['P2']['stable_mask'])
    r0, r1 = max(0, int(ys.min()) - 2), min(grid.active.shape[0], int(ys.max()) + 3)
    c0, c1 = max(0, int(xs.min()) - 2), min(grid.active.shape[1], int(xs.max()) + 3)
    tile_w = (c1 - c0) * 3
    tile_h = (r1 - r0) * 3
    panels = [
        ('raw-per-draw (f95)', controls['raw-per-draw'][95]),
        ('per-draw-heat-8cm', controls['per-draw-heat-8cm'][95]),
        ('per-draw-heat-16cm', controls['per-draw-heat-16cm'][95]),
        ('physical-accum-64', controls['physical-accum-64'][95]),
        ('accum64 + heat4cm', controls['physical-accum64-heat4cm'][95]),
        ('128-draw ref (P2)', refs['P2']['mean']),
    ]
    sheet_img = Image.new('RGB', (tile_w * 3 + 16, (tile_h + 24) * 2 + 8), '#17191c')
    draw = ImageDraw.Draw(sheet_img)
    for k, (label, data) in enumerate(panels):
        col, row = k % 3, k // 3
        x = col * (tile_w + 8)
        y = row * (tile_h + 24)
        crop = data[r0:r1, c0:c1]
        vis = Image.fromarray(encode_log(crop)).resize((tile_w, tile_h), Image.Resampling.NEAREST)
        sheet_img.paste(vis, (x, y + 20))
        draw.text((x + 4, y + 4), label, fill='white')
    sheet_img.save(out_dir / 'physical-sheet-P2-comparison.png')

    # Render moving reprojected render-space video of isolated source controls (render box [460, 0, 848, 300])
    # Map sheet values back to screen pixels at each frame t so motion anchoring is visually inspectable!
    rx0, ry0, rx1, ry1 = 460, 0, 848, 300
    rw, rh = rx1 - rx0, ry1 - ry0
    vid_names = [
        ('raw-per-draw', controls['raw-per-draw']),
        ('per-draw-heat-8cm', controls['per-draw-heat-8cm']),
        ('physical-accum-64', controls['physical-accum-64']),
        ('accum64 + heat4cm', controls['physical-accum64-heat4cm']),
    ]
    vdir = out_dir / 'sheet-video-frames'
    vdir.mkdir(exist_ok=True)
    for t in range(16, n_frames):
        pm = pixel_masks[t]
        rc = pixel_rcs[t]
        py, px = np.where(pm)
        tile_img = Image.new('RGB', (rw * 2 + 8, (rh + 22) * 2), '#17191c')
        dr = ImageDraw.Draw(tile_img)
        for k, (label, arr) in enumerate(vid_names):
            screen = np.zeros((h, w, 3), dtype=np.float64)
            screen[py, px] = arr[t, rc[py, px, 0], rc[py, px, 1]]
            crop = screen[ry0:ry1, rx0:rx1]
            col, row = k % 2, k // 2
            ox, oy = col * (rw + 8), row * (rh + 22)
            tile_img.paste(Image.fromarray(encode_log(crop)), (ox, oy + 20))
            dr.text((ox + 4, oy + 4), f'{label}  f={t:02d} draw={cap_draws[t]["sampleDraw"]}', fill='white')
        tile_img.save(vdir / f'frame-{t - 16:04d}.png')

    subprocess.run(
        [
            'ffmpeg', '-y', '-loglevel', 'error', '-framerate', '10',
            '-i', str(vdir / 'frame-%04d.png'),
            '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '18',
            str(out_dir / 'physical-sheet-moving-slow.mp4'),
        ],
        check=True,
    )
    return sheet_results


def analyze_live_moving(capture_root: Path, out_dir: Path, variants):
    # Load all display-linear RGB sequences (frames 16..95 = 80 continuous moving frames)
    start_f = 16
    raw_u8 = {}
    lin_rgb = {}
    draw_checks = {}
    for v in variants:
        folder = capture_root / v
        draws = load_draws(folder)
        cap = [d for d in draws if d['captured']]
        draw_checks[v] = dict(
            total_draws=len(draws),
            captured_frames=len(cap),
            resets_after_draw_0=sum(1 for d in draws[1:] if d['reset']),
            sample_draw_range=[cap[0]['sampleDraw'], cap[-1]['sampleDraw']],
        )
        pngs = sorted((folder / 'frames').glob('cockpit-*.png'))
        u8 = np.stack([np.asarray(Image.open(p).convert('RGB')) for p in pngs])
        raw_u8[v] = u8
        lin_rgb[v] = srgb_to_linear(u8)

    # Verify moving-source vs moving-normal byte identity if present
    src_folder = capture_root / 'moving-source'
    probe_identity = None
    if src_folder.exists():
        n_pngs = sorted((capture_root / 'moving-normal' / 'frames').glob('cockpit-*.png'))
        s_pngs = sorted((src_folder / 'frames').glob('cockpit-*.png'))
        same = len(n_pngs) == len(s_pngs) and all(
            hashlib.sha256(a.read_bytes()).digest() == hashlib.sha256(b.read_bytes()).digest()
            for a, b in zip(n_pngs, s_pngs)
        )
        probe_identity = dict(byte_identical_all_frames=same, frame_count=len(n_pngs))

    # Fixed output-space crops that stay well inside the moving panel / features across frames 16..95
    # (Also verified visually in the contact sheet!)
    crops = {
        'sloped-panel': (860, 40, 1080, 155),
        'panel-warm': (740, 35, 855, 130),
        'panel-cool': (855, 225, 955, 315),
        'windshield-edge': (900, 260, 1050, 440),
        'window-interior': (1120, 360, 1240, 435),
        'helmet-detail': (505, 315, 595, 385),
    }

    # Build per-frame edge-free mask for multiscale clump bands from moving-no-diffuse-ray-specular
    # (edges where the clean non-diffuse-ray-specular luminance has gradient >= 0.0015 per output pixel)
    clean_luma = lin_rgb['moving-no-diffuse-ray-specular'][start_f:] @ LUMA
    # Use time-mean clean luminance on a moving shot? Wait: because the camera moves, edges move across pixels!
    # Exclude any pixel whose MAX luminance gradient across the 80 frames exceeds 0.002,
    # dilated by 6*s? Even better: compute per-frame gradient and take the union of edge pixels!
    gy, gx = np.gradient(clean_luma, axis=(1, 2))
    grad_max = np.hypot(gx, gy).max(axis=0)

    baseline_rgb = lin_rgb['moving-normal'][start_f:]
    baseline_luma = baseline_rgb @ LUMA

    live_results = dict(
        measured_frames=[start_f, baseline_rgb.shape[0] + start_f - 1],
        draw_checks=draw_checks,
        probe_identity=probe_identity,
        crops={},
        multiscale_sloped_panel={},
    )

    # Multiscale bands on the sloped-panel crop with 128px context padding
    px0, py0, px1, py1 = crops['sloped-panel']
    pad = 128
    cx0, cy0 = max(0, px0 - pad), max(0, py0 - pad)
    cx1, cy1 = min(1280, px1 + pad), min(720, py1 + pad)
    ox0, oy0 = px0 - cx0, py0 - cy0
    ox1, oy1 = px1 - cx0, py1 - cy0

    # Per-scale edge mask using clean max-gradient (rejecting moving geometry/material edges)
    grad_crop = grad_max[py0:py1, px0:px1]
    scales = [1, 2, 4, 8, 16]
    from scipy.ndimage import binary_dilation
    scale_masks = {}
    for s in scales:
        # Exclude pixels within 3*s of strong clean edges (keeps valid interior on the moving panel)
        edge = grad_crop >= 0.0025
        rad = min(3 * s, 24)
        yy, xx = np.ogrid[-rad:rad + 1, -rad:rad + 1]
        struct_disk = (xx * xx + yy * yy) <= rad * rad
        m = ~binary_dilation(edge, structure=struct_disk)
        scale_masks[s] = m

    for v in variants:
        ctx = lin_rgb[v][start_f:, cy0:cy1, cx0:cx1, :]
        v_bands = {}
        for s in scales:
            m = scale_masks[s]
            if not np.any(m):
                continue
            b = (
                gaussian_filter(ctx, sigma=(0, s, s, 0), mode='reflect')
                - gaussian_filter(ctx, sigma=(0, 2 * s, 2 * s, 0), mode='reflect')
            )[:, oy0:oy1, ox0:ox1, :]
            vals = b[:, m, :]
            pers = vals.mean(axis=0)
            temp = vals - pers
            v_bands[str(s)] = dict(
                pixels=int(m.sum()),
                rgb_rms=float(np.sqrt(np.mean(vals ** 2))),
                persistent_rgb_rms=float(np.sqrt(np.mean(pers ** 2))),
                temporal_rgb_rms=float(np.sqrt(np.mean(temp ** 2))),
            )
        live_results['multiscale_sloped_panel'][v] = v_bands

    for cname, (x0, y0, x1, y1) in crops.items():
        base_c = baseline_rgb[:, y0:y1, x0:x1, :]
        base_l = baseline_luma[:, y0:y1, x0:x1]
        base_g = grain(base_l)
        c_res = {}
        for v in variants:
            var_c = lin_rgb[v][start_f:, y0:y1, x0:x1, :]
            var_l = var_c @ LUMA
            g = grain(var_l)
            cmp = compare(base_l, var_l)
            entry = dict(
                grain=g,
                compare=cmp,
                spatial_change_percent=float(100.0 * (g['spatial_rms'] / max(base_g['spatial_rms'], 1e-20) - 1.0)),
                temporal_change_percent=float(100.0 * (g['temporal_rms'] / max(base_g['temporal_rms'], 1e-20) - 1.0)),
            )
            if cname in ('panel-warm', 'panel-cool', 'sloped-panel'):
                entry['local_wash'] = color_wash(base_c, var_c)
            c_res[v] = entry
        live_results['crops'][cname] = c_res

    # Contact sheet: full moving crop [700, 20, 1140, 440] for all variants at frames 16, 48 (P1), 95 (P2)
    bx0, by0, bx1, by1 = 700, 20, 1140, 440
    bw, bh = bx1 - bx0, by1 - by0
    sample_frames = [16, 48, 95]
    sheet_png = Image.new('RGB', (bw * len(variants), (bh + 24) * len(sample_frames)), '#17191c')
    dr = ImageDraw.Draw(sheet_png)
    for r, fidx in enumerate(sample_frames):
        for c, v in enumerate(variants):
            crop = raw_u8[v][fidx, by0:by1, bx0:bx1]
            ox, oy = c * bw, r * (bh + 24)
            sheet_png.paste(Image.fromarray(crop), (ox, oy + 22))
            dr.text((ox + 6, oy + 4), f'{v}  f={fidx}', fill='white')
    sheet_png.save(out_dir / 'moving-live-contact.png')

    # Windshield boundary 2x zoom at P1 (48) and P2 (95)
    wx0, wy0, wx1, wy1 = 880, 240, 1120, 440
    ww, wh = (wx1 - wx0) * 2, (wy1 - wy0) * 2
    ws_png = Image.new('RGB', (ww * len(variants), (wh + 24) * 2), '#17191c')
    dr2 = ImageDraw.Draw(ws_png)
    for r, fidx in enumerate([48, 95]):
        for c, v in enumerate(variants):
            crop = raw_u8[v][fidx, wy0:wy1, wx0:wx1]
            ox, oy = c * ww, r * (wh + 24)
            ws_png.paste(Image.fromarray(crop).resize((ww, wh), Image.Resampling.NEAREST), (ox, oy + 22))
            dr2.text((ox + 6, oy + 4), f'{v} windshield f={fidx}', fill='white')
    ws_png.save(out_dir / 'moving-windshield-zoom.png')

    # Moving crop videos (60 fps real-time + 10 fps slow-motion) comparing the 4 main live variants
    vid_variants = [v for v in variants if v != 'moving-no-diffuse-ray-specular']
    vdir = out_dir / 'live-video-frames'
    vdir.mkdir(exist_ok=True)
    for idx, fidx in enumerate(range(start_f, raw_u8[variants[0]].shape[0])):
        cols = 2
        rows = (len(vid_variants) + 1) // 2
        canvas = Image.new('RGB', (bw * cols, (bh + 24) * rows), '#17191c')
        dv = ImageDraw.Draw(canvas)
        for k, v in enumerate(vid_variants):
            c, r = k % cols, k // cols
            ox, oy = c * bw, r * (bh + 24)
            canvas.paste(Image.fromarray(raw_u8[v][fidx, by0:by1, bx0:bx1]), (ox, oy + 22))
            dv.text((ox + 6, oy + 4), f'{v}  f={fidx:02d} draw={fidx + 32}', fill='white')
        canvas.save(vdir / f'frame-{idx:04d}.png')

    for fps, fname in [(60, 'moving-live-crops-60fps.mp4'), (10, 'moving-live-crops-slow.mp4')]:
        subprocess.run(
            [
                'ffmpeg', '-y', '-loglevel', 'error', '-framerate', str(fps),
                '-i', str(vdir / 'frame-%04d.png'),
                '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '18',
                str(out_dir / fname),
            ],
            check=True,
        )
    return live_results


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('capture_root', type=Path)
    p.add_argument('output', type=Path)
    p.add_argument('--grid-spacing', type=float, default=0.02)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    sheet = load_cockpit_panel_sheet()
    grid = build_physical_grid(sheet, spacing=args.grid_spacing)

    refs, ref_summary = analyze_fixed_references(args.capture_root, sheet, grid)
    sheet_results = analyze_moving_sheet(args.capture_root, args.output, sheet, grid, refs)
    live_variants = [
        'moving-normal',
        'moving-connected',
        'moving-source-accum',
        'moving-source-accum-spread',
        'moving-no-diffuse-ray-specular',
    ]
    live_results = analyze_live_moving(args.capture_root, args.output, live_variants)

    report = dict(
        scope='Continuous ordinary draw phases with slow repeatable camera motion (every=1, 96 frames), '
              '3 fixed-pose 128-draw multi-phase references with batch convergence cross-checks, '
              'actual lunarshuttle_fuselage 3-triangle cockpit panel sheet in physical metres with '
              'area-aware no-cutoff heat flow, and matched live full-pipeline continuous renders.',
        fixed_references=ref_summary,
        physical_sheet_study=sheet_results,
        live_moving_study=live_results,
    )
    (args.output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Wrote', args.output / 'results.json')


if __name__ == '__main__':
    main()
