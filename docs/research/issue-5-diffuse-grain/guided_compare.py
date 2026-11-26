"""Compare fixed-budget probability-weighted diffuse guiding controls against normal,
mainstream rough-policy, and isolated-source accumulation prototypes on the 1:41 cockpit.
"""
import argparse
import json
from pathlib import Path
import subprocess
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, binary_dilation

from measure import grain, compare, color_wash

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def srgb_to_linear(u8):
    x = u8.astype(np.float32) / np.float32(255.0)
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4).astype(np.float32)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('capture_root', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    variants = [
        'moving-normal',
        'guided-diffuse-075',
        'guided-diffuse-075-tight',
        'moving-mainstream-rough-cap64',
        'moving-source-accum',
        'moving-source-accum-spread',
        'moving-no-diffuse-ray-specular',
    ]
    present = [v for v in variants if (args.capture_root / v / 'frames').exists()]
    start_f = 16
    u8s = {}
    lins = {}
    for v in present:
        pngs = sorted((args.capture_root / v / 'frames').glob('cockpit-*.png'))[start_f:96]
        u8 = np.stack([np.asarray(Image.open(p).convert('RGB')) for p in pngs])
        u8s[v] = u8
        lins[v] = srgb_to_linear(u8)

    crops = {
        'sloped-panel': (860, 40, 1080, 155),
        'panel-warm': (740, 35, 855, 130),
        'panel-cool': (855, 225, 955, 315),
        'cabin-floor-diffuse': (20, 460, 360, 710),
        'windshield-glass': (660, 230, 750, 300),
        'helmet-detail': (505, 315, 595, 385),
        'glossy-trim': (340, 0, 620, 110),
    }

    clean_luma = lins['moving-no-diffuse-ray-specular'] @ LUMA
    gy, gx = np.gradient(clean_luma, axis=(1, 2))
    grad_max = np.hypot(gx, gy).max(axis=0)

    px0, py0, px1, py1 = crops['sloped-panel']
    pad = 96
    cx0, cy0 = max(0, px0 - pad), max(0, py0 - pad)
    cx1, cy1 = min(1280, px1 + pad), min(720, py1 + pad)
    ox0, oy0 = px0 - cx0, py0 - cy0
    ox1, oy1 = px1 - cx0, py1 - cy0
    grad_crop = grad_max[py0:py1, px0:px1]
    scales = [1, 2, 4, 8, 16]
    scale_masks = {}
    for s in scales:
        edge = grad_crop >= 0.0025
        rad = min(3 * s, 20)
        yy, xx = np.ogrid[-rad:rad + 1, -rad:rad + 1]
        scale_masks[s] = ~binary_dilation(edge, structure=(xx * xx + yy * yy) <= rad * rad)

    multiscale = {}
    for v in present:
        ctx = lins[v][:, cy0:cy1, cx0:cx1, :]
        cache = {}
        def gb(sig):
            if sig not in cache:
                cache[sig] = gaussian_filter(ctx, sigma=(0, sig, sig, 0), mode='reflect')
            return cache[sig]
        vb = {}
        for s in scales:
            m = scale_masks[s]
            b = (gb(s) - gb(2 * s))[:, oy0:oy1, ox0:ox1, :]
            vals = b[:, m, :].astype(np.float64)
            pers = vals.mean(axis=0)
            temp = vals - pers
            vb[str(s)] = dict(
                pixels=int(m.sum()),
                rgb_rms=float(np.sqrt(np.mean(vals ** 2))),
                persistent_rgb_rms=float(np.sqrt(np.mean(pers ** 2))),
                temporal_rgb_rms=float(np.sqrt(np.mean(temp ** 2))),
            )
        multiscale[v] = vb

    base_rgb = lins['moving-normal']
    crop_metrics = {}
    for cname, (x0, y0, x1, y1) in crops.items():
        b_c = base_rgb[:, y0:y1, x0:x1, :].astype(np.float64)
        b_l = b_c @ LUMA.astype(np.float64)
        b_g = grain(b_l)
        cm = {}
        for v in present:
            v_c = lins[v][:, y0:y1, x0:x1, :].astype(np.float64)
            v_l = v_c @ LUMA.astype(np.float64)
            g = grain(v_l)
            cmp = compare(b_l, v_l)
            entry = dict(
                grain=g,
                compare=cmp,
                spatial_change_percent=float(100.0 * (g['spatial_rms'] / max(b_g['spatial_rms'], 1e-20) - 1.0)),
                temporal_change_percent=float(100.0 * (g['temporal_rms'] / max(b_g['temporal_rms'], 1e-20) - 1.0)),
            )
            if cname in ('sloped-panel', 'panel-warm', 'panel-cool', 'cabin-floor-diffuse'):
                entry['local_wash'] = color_wash(b_c, v_c)
            cm[v] = entry
        crop_metrics[cname] = cm

    # Contact sheet on [700, 20, 1140, 440] at f=48 (rel 32) and f=95 (rel 79)
    show = [
        v for v in (
            'moving-normal',
            'guided-diffuse-075',
            'guided-diffuse-075-tight',
            'moving-mainstream-rough-cap64',
            'moving-source-accum-spread',
        ) if v in present
    ]
    bx0, by0, bx1, by1 = 700, 20, 1140, 440
    bw, bh = bx1 - bx0, by1 - by0
    sheet = Image.new('RGB', (bw * len(show), (bh + 24) * 2), '#17191c')
    dr = ImageDraw.Draw(sheet)
    for r, rel in enumerate([32, 79]):
        for c, v in enumerate(show):
            ox, oy = c * bw, r * (bh + 24)
            sheet.paste(Image.fromarray(u8s[v][rel, by0:by1, bx0:bx1]), (ox, oy + 22))
            dr.text((ox + 6, oy + 4), f'{v} f={rel + start_f}', fill='white')
    sheet.save(args.output / 'guided-controls-contact.png')

    # Slow-mo 2x2 clip: normal | guided-diffuse-075 | guided-diffuse-075-tight | mainstream-rough-cap64
    c_vars = [
        v for v in ('moving-normal', 'guided-diffuse-075', 'guided-diffuse-075-tight', 'moving-mainstream-rough-cap64')
        if v in present
    ]
    vdir = args.output / 'guided-video-frames'
    vdir.mkdir(exist_ok=True)
    for idx in range(u8s[c_vars[0]].shape[0]):
        canvas = Image.new('RGB', (bw * 2, (bh + 24) * 2), '#17191c')
        dv = ImageDraw.Draw(canvas)
        for k, v in enumerate(c_vars):
            c, r = k % 2, k // 2
            ox, oy = c * bw, r * (bh + 24)
            canvas.paste(Image.fromarray(u8s[v][idx, by0:by1, bx0:bx1]), (ox, oy + 22))
            dv.text((ox + 6, oy + 4), f'{v} f={idx + start_f:02d}', fill='white')
        canvas.save(vdir / f'frame-{idx:04d}.png')

    subprocess.run(
        [
            'ffmpeg', '-y', '-loglevel', 'error', '-framerate', '10',
            '-i', str(vdir / 'frame-%04d.png'),
            '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '18',
            str(args.output / 'guided-controls-slow.mp4'),
        ],
        check=True,
    )

    out_json = dict(measured_frames=[start_f, 95], multiscale_sloped_panel=multiscale, crops=crop_metrics)
    (args.output / 'guided-results.json').write_text(json.dumps(out_json, indent=2) + '\n')
    print('Wrote', args.output / 'guided-results.json')


if __name__ == '__main__':
    main()
