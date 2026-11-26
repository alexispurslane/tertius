"""Analyze mainstream specular accumulator control-trace and rough-receiver policy controls.

Quantifies on the 1:41 continuous-motion cockpit:
1. Byte-identity verification of moving-control-trace against moving-normal.
2. Per-region roughness distribution (sloped panel, warm/cool patches, windshield, helmet).
3. AccumulationTrace breakdown distinguishing:
   - reprojection/surface match (TracedSurfaceHolds, TracedReflectedTried, TracedReflectedWon, sum(r.weights)),
   - pre-Step-4 blended historyLength (layer 4.r) vs post-Step-4 historyLength after narrow-box clamp/anti-lag/evidence (layer 4.g),
   - clamp trigger rate (layer 5.a) and clamp lag (layer 6.a).
4. Head-to-head comparison of moving-normal, moving-mainstream-rough-cap64,
   moving-mainstream-rough-uncapped, moving-source-accum (separated, no spread),
   moving-source-accum-spread (separated + gentle radius-5 spread), and
   moving-no-diffuse-ray-specular across steady wash, flicker, multiscale clump bands,
   motion lag/streaks, glossy helmet, and windshield boundary.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, binary_dilation

from measure import grain, compare, color_wash

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float64)

TRACED_REFLECTED_WON = 1 << 4
TRACED_REFLECTED_TRIED = 1 << 5
TRACED_SURFACE_HOLDS = 1 << 6
TRACED_REFLECTED_HOLDS = 1 << 7
TRACED_SAMPLE = 1 << 8
TRACED_SURFACE_PIXEL = 1 << 9
TRACED_PIXEL_PLACE_READ = 1 << 10
TRACED_REFLECTED_PIXEL_PLACE_READ = 1 << 11


def srgb_to_linear(u8):
    x = u8.astype(np.float64) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def output_box_to_render_box(box, out_w, out_h, ren_w, ren_h):
    x0, y0, x1, y1 = box
    return (
        max(0, int(round(x0 * ren_w / out_w))),
        max(0, int(round(y0 * ren_h / out_h))),
        min(ren_w, int(round(x1 * ren_w / out_w))),
        min(ren_h, int(round(y1 * ren_h / out_h))),
    )


def summarize_trace(trace_dir: Path, output_crops: dict, out_w=1280, out_h=720):
    ren_w, ren_h = map(int, (trace_dir / 'size.txt').read_text().split())
    stems = sorted({p.name.split('.')[0] for p in trace_dir.glob('*.layer0.f32')})
    out = dict(render_size=[ren_w, ren_h], sample_draws=[int(s) for s in stems], by_crop={})

    for cname, obox in output_crops.items():
        rx0, ry0, rx1, ry1 = output_box_to_render_box(obox, out_w, out_h, ren_w, ren_h)
        per_draw = {}
        agg_rough = []
        agg_surf_holds = []
        agg_refl_tried = []
        agg_refl_won = []
        agg_weights = []
        agg_last_acc = []
        agg_pre_blend = []
        agg_before_step4 = []
        agg_after_step4 = []
        agg_clamp_ran = []
        agg_clamp_cut = []
        agg_lag = []
        agg_motion_px = []

        for s in stems:
            l0 = np.fromfile(trace_dir / f'{s}.layer0.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            l2 = np.fromfile(trace_dir / f'{s}.layer2.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            l4 = np.fromfile(trace_dir / f'{s}.layer4.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            l5 = np.fromfile(trace_dir / f'{s}.layer5.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            l6 = np.fromfile(trace_dir / f'{s}.layer6.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            spec = np.fromfile(trace_dir / f'{s}.specular.u8', dtype=np.uint8).reshape(ren_h, ren_w, 4)[ry0:ry1, rx0:rx1]
            dist = np.fromfile(trace_dir / f'{s}.distance.f32', dtype='<f4').reshape(ren_h, ren_w)[ry0:ry1, rx0:rx1]

            surf = dist > 0.0
            if not np.any(surf):
                continue
            rough = spec[..., 1].astype(np.float64)[surf] / 255.0
            flags = np.rint(l0[..., 3][surf]).astype(np.uint32)
            surf_holds = (flags & TRACED_SURFACE_HOLDS) != 0
            refl_tried = (flags & TRACED_REFLECTED_TRIED) != 0
            refl_won = (flags & TRACED_REFLECTED_WON) != 0
            weights = l0[..., 2][surf].astype(np.float64)
            last_acc = l0[..., 0][surf].astype(np.float64)
            pre_blend = l0[..., 1][surf].astype(np.float64)
            before_s4 = l4[..., 0][surf].astype(np.float64)
            after_s4 = l4[..., 1][surf].astype(np.float64)
            clamp_ran = l5[..., 3][surf] > 0.5
            clamp_cut = after_s4 < (before_s4 - 0.01)
            lag = l6[..., 3][surf].astype(np.float64)
            mot_px = np.hypot(l2[..., 0][surf].astype(np.float64), l2[..., 1][surf].astype(np.float64))

            per_draw[str(int(s))] = dict(
                surface_pixels=int(surf.sum()),
                roughness_p10_p50_p90=[float(np.quantile(rough, q)) for q in (0.1, 0.5, 0.9)],
                rough_ge_040_frac=float(np.mean(rough >= 0.4)),
                rough_ge_050_frac=float(np.mean(rough >= 0.5)),
                surface_holds_frac=float(np.mean(surf_holds)),
                reflected_tried_frac=float(np.mean(refl_tried)),
                reflected_won_frac=float(np.mean(refl_won)),
                weight_sum_mean=float(np.mean(weights)),
                motion_texels_median=float(np.median(mot_px)),
                len_pre_blend_p50_p90=[float(np.median(pre_blend)), float(np.quantile(pre_blend, 0.9))],
                len_before_step4_p50_p90=[float(np.median(before_s4)), float(np.quantile(before_s4, 0.9))],
                len_after_step4_p50_p90=[float(np.median(after_s4)), float(np.quantile(after_s4, 0.9))],
                clamp_ran_frac=float(np.mean(clamp_ran)),
                clamp_cut_frac=float(np.mean(clamp_cut)),
                lag_p50_p90_when_clamped=(
                    [float(np.median(lag[clamp_ran])), float(np.quantile(lag[clamp_ran], 0.9))]
                    if np.any(clamp_ran)
                    else [0.0, 0.0]
                ),
            )
            if int(s) >= 48:
                agg_rough.append(rough)
                agg_surf_holds.append(surf_holds)
                agg_refl_tried.append(refl_tried)
                agg_refl_won.append(refl_won)
                agg_weights.append(weights)
                agg_last_acc.append(last_acc)
                agg_pre_blend.append(pre_blend)
                agg_before_step4.append(before_s4)
                agg_after_step4.append(after_s4)
                agg_clamp_ran.append(clamp_ran)
                agg_clamp_cut.append(clamp_cut)
                agg_lag.append(lag)
                agg_motion_px.append(mot_px)

        if agg_rough:
            rough_a = np.concatenate(agg_rough)
            sh_a = np.concatenate(agg_surf_holds)
            rt_a = np.concatenate(agg_refl_tried)
            rw_a = np.concatenate(agg_refl_won)
            w_a = np.concatenate(agg_weights)
            b4_a = np.concatenate(agg_before_step4)
            a4_a = np.concatenate(agg_after_step4)
            cr_a = np.concatenate(agg_clamp_ran)
            cc_a = np.concatenate(agg_clamp_cut)
            lag_a = np.concatenate(agg_lag)
            mot_a = np.concatenate(agg_motion_px)
            pooled = dict(
                draws_included='sampleDraw >= 48',
                roughness_min_p50_max=[float(rough_a.min()), float(np.median(rough_a)), float(rough_a.max())],
                rough_ge_040_frac=float(np.mean(rough_a >= 0.4)),
                rough_ge_050_frac=float(np.mean(rough_a >= 0.5)),
                surface_holds_frac=float(np.mean(sh_a)),
                reflected_tried_frac=float(np.mean(rt_a)),
                reflected_won_frac=float(np.mean(rw_a)),
                weight_sum_mean=float(np.mean(w_a)),
                motion_texels_p50_p90=[float(np.median(mot_a)), float(np.quantile(mot_a, 0.9))],
                len_before_step4_mean_p50_p90=[float(np.mean(b4_a)), float(np.median(b4_a)), float(np.quantile(b4_a, 0.9))],
                len_after_step4_mean_p50_p90=[float(np.mean(a4_a)), float(np.median(a4_a)), float(np.quantile(a4_a, 0.9))],
                clamp_ran_frac=float(np.mean(cr_a)),
                clamp_cut_frac=float(np.mean(cc_a)),
                lag_p50_p90_when_clamped=(
                    [float(np.median(lag_a[cr_a])), float(np.quantile(lag_a[cr_a], 0.9))]
                    if np.any(cr_a)
                    else [0.0, 0.0]
                ),
            )
        else:
            pooled = {}

        out['by_crop'][cname] = dict(
            render_box=[rx0, ry0, rx1, ry1],
            pooled=pooled,
            per_draw=per_draw,
        )
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('capture_root', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    crops = {
        'sloped-panel': (860, 40, 1080, 155),
        'panel-warm': (740, 35, 855, 130),
        'panel-cool': (855, 225, 955, 315),
        'windshield-edge': (900, 260, 1050, 440),
        'windshield-glass': (660, 230, 750, 300),
        'helmet-detail': (505, 315, 595, 385),
        'glossy-trim': (340, 0, 620, 110),
    }

    # Verify byte identity of moving-control-trace vs moving-normal
    n_pngs = sorted((args.capture_root / 'moving-normal' / 'frames').glob('cockpit-*.png'))
    ct_pngs = sorted((args.capture_root / 'moving-control-trace' / 'frames').glob('cockpit-*.png'))
    byte_identical = len(n_pngs) == len(ct_pngs) and all(
        hashlib.sha256(a.read_bytes()).digest() == hashlib.sha256(b.read_bytes()).digest()
        for a, b in zip(n_pngs, ct_pngs)
    )

    variants = [
        'moving-normal',
        'moving-mainstream-rough-cap64',
        'moving-mainstream-rough-uncapped',
        'moving-source-accum',
        'moving-source-accum-spread',
        'moving-no-diffuse-ray-specular',
    ]
    present = [v for v in variants if (args.capture_root / v / 'frames').exists()]

    u8s = {}
    lins = {}
    for v in present:
        pngs = sorted((args.capture_root / v / 'frames').glob('cockpit-*.png'))
        u8 = np.stack([np.asarray(Image.open(p).convert('RGB')) for p in pngs])
        u8s[v] = u8
        lins[v] = srgb_to_linear(u8)

    start_f = 16
    base_rgb = lins['moving-normal'][start_f:]
    base_luma = base_rgb @ LUMA
    clean_luma = lins['moving-no-diffuse-ray-specular'][start_f:] @ LUMA
    gy, gx = np.gradient(clean_luma, axis=(1, 2))
    grad_max = np.hypot(gx, gy).max(axis=0)

    # Multiscale bands on sloped-panel
    px0, py0, px1, py1 = crops['sloped-panel']
    pad = 128
    cx0, cy0 = max(0, px0 - pad), max(0, py0 - pad)
    cx1, cy1 = min(1280, px1 + pad), min(720, py1 + pad)
    ox0, oy0 = px0 - cx0, py0 - cy0
    ox1, oy1 = px1 - cx0, py1 - cy0
    grad_crop = grad_max[py0:py1, px0:px1]
    scales = [1, 2, 4, 8, 16]
    scale_masks = {}
    for s in scales:
        edge = grad_crop >= 0.0025
        rad = min(3 * s, 24)
        yy, xx = np.ogrid[-rad:rad + 1, -rad:rad + 1]
        scale_masks[s] = ~binary_dilation(edge, structure=(xx * xx + yy * yy) <= rad * rad)

    multiscale_panel = {}
    for v in present:
        ctx = lins[v][start_f:, cy0:cy1, cx0:cx1, :]
        vb = {}
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
            vb[str(s)] = dict(
                pixels=int(m.sum()),
                rgb_rms=float(np.sqrt(np.mean(vals ** 2))),
                persistent_rgb_rms=float(np.sqrt(np.mean(pers ** 2))),
                temporal_rgb_rms=float(np.sqrt(np.mean(temp ** 2))),
            )
        multiscale_panel[v] = vb

    crop_metrics = {}
    for cname, (x0, y0, x1, y1) in crops.items():
        b_c = base_rgb[:, y0:y1, x0:x1, :]
        b_l = base_luma[:, y0:y1, x0:x1]
        b_g = grain(b_l)
        cm = {}
        for v in present:
            v_c = lins[v][start_f:, y0:y1, x0:x1, :]
            v_l = v_c @ LUMA
            g = grain(v_l)
            cmp = compare(b_l, v_l)
            entry = dict(
                grain=g,
                compare=cmp,
                spatial_change_percent=float(100.0 * (g['spatial_rms'] / max(b_g['spatial_rms'], 1e-20) - 1.0)),
                temporal_change_percent=float(100.0 * (g['temporal_rms'] / max(b_g['temporal_rms'], 1e-20) - 1.0)),
            )
            if cname in ('sloped-panel', 'panel-warm', 'panel-cool'):
                entry['local_wash'] = color_wash(b_c, v_c)
            cm[v] = entry
        crop_metrics[cname] = cm

    # Trace summaries for all traced runs
    traces = {}
    for tv in ('moving-control-trace', 'moving-mainstream-rough-cap64', 'moving-mainstream-rough-uncapped'):
        tdir = args.capture_root / tv / 'trace'
        if tdir.exists():
            traces[tv] = summarize_trace(tdir, crops)

    # Contact sheet comparing mainstream variants vs prototype on moving cockpit [700, 20, 1140, 440] at frames 16, 48, 95
    show_vars = [
        v for v in (
            'moving-normal',
            'moving-mainstream-rough-cap64',
            'moving-mainstream-rough-uncapped',
            'moving-source-accum',
            'moving-source-accum-spread',
        ) if v in present
    ]
    bx0, by0, bx1, by1 = 700, 20, 1140, 440
    bw, bh = bx1 - bx0, by1 - by0
    sample_frames = [16, 48, 95]
    sheet = Image.new('RGB', (bw * len(show_vars), (bh + 24) * len(sample_frames)), '#17191c')
    dr = ImageDraw.Draw(sheet)
    for r, fidx in enumerate(sample_frames):
        for c, v in enumerate(show_vars):
            ox, oy = c * bw, r * (bh + 24)
            sheet.paste(Image.fromarray(u8s[v][fidx, by0:by1, bx0:bx1]), (ox, oy + 22))
            dr.text((ox + 6, oy + 4), f'{v} f={fidx}', fill='white')
    sheet.save(args.output / 'mainstream-vs-prototype-contact.png')

    # Helmet & windshield glass 2x zoom at f=48 and f=95
    hx0, hy0, hx1, hy1 = 480, 220, 770, 420
    hw, hh = (hx1 - hx0) * 2, (hy1 - hy0) * 2
    himg = Image.new('RGB', (hw * len(show_vars), (hh + 24) * 2), '#17191c')
    dh = ImageDraw.Draw(himg)
    for r, fidx in enumerate([48, 95]):
        for c, v in enumerate(show_vars):
            ox, oy = c * hw, r * (hh + 24)
            tile = Image.fromarray(u8s[v][fidx, hy0:hy1, hx0:hx1]).resize((hw, hh), Image.Resampling.NEAREST)
            himg.paste(tile, (ox, oy + 22))
            dh.text((ox + 6, oy + 4), f'{v} helmet+glass f={fidx}', fill='white')
    himg.save(args.output / 'mainstream-helmet-glass-zoom.png')

    # Moving slow-mo clip (10 fps): 2x2 grid comparing normal, mainstream-cap64, source-accum, source-accum-spread
    clip_vars = [
        v for v in (
            'moving-normal',
            'moving-mainstream-rough-cap64',
            'moving-source-accum',
            'moving-source-accum-spread',
        ) if v in present
    ]
    vdir = args.output / 'mainstream-video-frames'
    vdir.mkdir(exist_ok=True)
    for idx, fidx in enumerate(range(start_f, u8s[clip_vars[0]].shape[0])):
        canvas = Image.new('RGB', (bw * 2, (bh + 24) * 2), '#17191c')
        dv = ImageDraw.Draw(canvas)
        for k, v in enumerate(clip_vars):
            c, r = k % 2, k // 2
            ox, oy = c * bw, r * (bh + 24)
            canvas.paste(Image.fromarray(u8s[v][fidx, by0:by1, bx0:bx1]), (ox, oy + 22))
            dv.text((ox + 6, oy + 4), f'{v}  f={fidx:02d}', fill='white')
        canvas.save(vdir / f'frame-{idx:04d}.png')

    subprocess.run(
        [
            'ffmpeg', '-y', '-loglevel', 'error', '-framerate', '10',
            '-i', str(vdir / 'frame-%04d.png'),
            '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '18',
            str(args.output / 'mainstream-vs-prototype-slow.mp4'),
        ],
        check=True,
    )

    report = dict(
        control_trace_byte_identical_to_moving_normal=byte_identical,
        measured_frames=[start_f, base_rgb.shape[0] + start_f - 1],
        traces=traces,
        multiscale_sloped_panel=multiscale_panel,
        crops=crop_metrics,
    )
    (args.output / 'mainstream-results.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Wrote', args.output / 'mainstream-results.json')


if __name__ == '__main__':
    main()
