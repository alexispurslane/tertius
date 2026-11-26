"""Cross-shot quantitative and visual analysis for issue #5 Blue Danube shots.

Compares matched-exposure baseline, 1:41 candidate (isolated diffuse-ray-sourced specular
RGBA32F physical-point accumulation + gentle radius-5 reciprocal connected spread), and
withdrawal of just the diffuse-ray-sourced specular term (plus optional zero-diffuse check)
across 0:13 doorway, 0:27 cockpit floor + 0:28 dashboard, 4:14.48 & 4:19.82 arrival
(time-lapse every=4 and ordinary-motion every=1), and 7:27 berth floor.
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


def load_frames(folder: Path, prefix: str, frame_slice=None):
    paths = sorted((folder / 'frames').glob(f'{prefix}-*.png'))
    assert paths, f'No frames matching {prefix}-*.png in {folder / "frames"}'
    if frame_slice is not None:
        f0, f1 = frame_slice
        paths = paths[f0:f1]
    u8 = np.stack([np.asarray(Image.open(p).convert('RGB')) for p in paths])
    return u8, srgb_to_linear(u8)


def multiscale_crop(lin_seq, clean_seq, box, scales=(1, 2, 4, 8, 16)):
    x0, y0, x1, y1 = box
    pad = 96
    h, w = lin_seq.shape[1:3]
    cx0, cy0 = max(0, x0 - pad), max(0, y0 - pad)
    cx1, cy1 = min(w, x1 + pad), min(h, y1 + pad)
    ox0, oy0 = x0 - cx0, y0 - cy0
    ox1, oy1 = x1 - cx0, y1 - cy0

    clean_luma = clean_seq[:, y0:y1, x0:x1, :] @ LUMA
    gy, gx = np.gradient(clean_luma, axis=(1, 2))
    grad_max = np.hypot(gx, gy).max(axis=0)
    # Use a conservative threshold relative to crop brightness so dark and bright shots both mask hard material/geometry edges
    thresh = max(0.0025, 0.04 * float(np.median(clean_luma)))
    edge = grad_max >= thresh

    ctx = lin_seq[:, cy0:cy1, cx0:cx1, :]
    blur_cache = {}
    def get_blur(sigma):
        if sigma not in blur_cache:
            blur_cache[sigma] = gaussian_filter(ctx, sigma=(0, sigma, sigma, 0), mode='reflect')
        return blur_cache[sigma]

    out = {}
    for s in scales:
        rad = min(3 * s, 20)
        yy, xx = np.ogrid[-rad:rad + 1, -rad:rad + 1]
        m = ~binary_dilation(edge, structure=(xx * xx + yy * yy) <= rad * rad)
        if int(m.sum()) < 25:
            continue
        b = (get_blur(s) - get_blur(2 * s))[:, oy0:oy1, ox0:ox1, :]
        vals = b[:, m, :].astype(np.float64)
        pers = vals.mean(axis=0)
        temp = vals - pers
        out[str(s)] = dict(
            pixels=int(m.sum()),
            rgb_rms=float(np.sqrt(np.mean(vals ** 2))),
            persistent_rgb_rms=float(np.sqrt(np.mean(pers ** 2))),
            temporal_rgb_rms=float(np.sqrt(np.mean(temp ** 2))),
        )
    return out


def analyze_segment(shot_dir: Path, out_dir: Path, tag: str, prefix: str, frame_slice, key_frame: int, crops: dict, clip_box, variants):
    f0, f1 = frame_slice
    assert f0 <= key_frame < f1, f'key_frame {key_frame} outside [{f0}, {f1})'
    rel_key = key_frame - f0
    u8s = {}
    lins = {}
    draw_audits = {}
    for v in variants:
        vdir = shot_dir / v
        if not vdir.exists():
            continue
        u8, lin = load_frames(vdir, prefix, frame_slice=frame_slice)
        u8s[v] = u8
        lins[v] = lin
        draws = [json.loads(l) for l in (vdir / 'draws.jsonl').read_text().splitlines() if l.strip()]
        cap = [d for d in draws if d['captured']]
        resets = [dict(globalDraw=d['globalDraw'], sampleDraw=d['sampleDraw'], tick=d['tick']) for d in draws if d['reset']]
        exps = [d['exposureStops']['Stops'] if isinstance(d['exposureStops'], dict) else d['exposureStops'] for d in cap]
        draw_audits[v] = dict(
            total_draws=len(draws),
            captured=len(cap),
            reset_count=len(resets),
            resets=resets,
            exposure_min_max=[float(min(exps)), float(max(exps))],
        )

    present = [v for v in variants if v in lins]
    assert 'baseline' in present
    base_lin = lins['baseline']
    clean_ref = lins['no-diffuse-ray-specular'] if 'no-diffuse-ray-specular' in lins else base_lin

    seg_res = dict(
        tag=tag,
        prefix=prefix,
        measured_frames=[f0, f1 - 1],
        key_frame=key_frame,
        draw_audits=draw_audits,
        crops={},
    )

    for cname, box in crops.items():
        x0, y0, x1, y1 = box
        b_rgb = base_lin[:, y0:y1, x0:x1, :].astype(np.float64)
        b_luma = b_rgb @ LUMA.astype(np.float64)
        b_g = grain(b_luma)
        c_dict = {'box': list(box), 'variants': {}}
        for v in present:
            v_rgb = lins[v][:, y0:y1, x0:x1, :].astype(np.float64)
            v_luma = v_rgb @ LUMA.astype(np.float64)
            g = grain(v_luma)
            cmp = compare(b_luma, v_luma)
            lw = color_wash(b_rgb, v_rgb)
            wash_vals = [p['wash_relative_rms_percent'] for p in lw.values()]
            c_dict['variants'][v] = dict(
                grain=g,
                compare=cmp,
                spatial_change_percent=float(100.0 * (g['spatial_rms'] / max(b_g['spatial_rms'], 1e-20) - 1.0)),
                temporal_change_percent=float(100.0 * (g['temporal_rms'] / max(b_g['temporal_rms'], 1e-20) - 1.0)),
                wash_min_max_percent=[float(min(wash_vals)), float(max(wash_vals))],
                local_wash=lw,
                multiscale=multiscale_crop(lins[v], clean_ref, box),
            )
        seg_res['crops'][cname] = c_dict

    # Contact sheet: key_frame full-res crops side by side for present variants
    # Row 0: clip_box (resized to max width 480 per column), Rows 1..: each named crop at 1:1 (or 2x if small)
    col_w = 480
    bx0, by0, bx1, by1 = clip_box
    bw, bh = bx1 - bx0, by1 - by0
    top_h = int(round(bh * (col_w / bw)))
    rows_spec = [('full-region', clip_box, col_w, top_h)]
    for cname, box in crops.items():
        cw, ch = box[2] - box[0], box[3] - box[1]
        scale = min(col_w / cw, 260 / ch)
        rows_spec.append((cname, box, int(round(cw * scale)), int(round(ch * scale))))
    total_h = sum(rh + 24 for _, _, _, rh in rows_spec)
    sheet = Image.new('RGB', (col_w * len(present), total_h), '#17191c')
    dr = ImageDraw.Draw(sheet)
    cur_y = 0
    for rname, box, rw, rh in rows_spec:
        x0, y0, x1, y1 = box
        for ci, v in enumerate(present):
            tile = Image.fromarray(u8s[v][rel_key, y0:y1, x0:x1])
            if (tile.width, tile.height) != (rw, rh):
                tile = tile.resize((rw, rh), Image.Resampling.NEAREST if rw > tile.width else Image.Resampling.BILINEAR)
            ox = ci * col_w
            sheet.paste(tile, (ox, cur_y + 22))
            dr.text((ox + 6, cur_y + 4), f'{tag} {v} | {rname} (f={key_frame})', fill='white')
        cur_y += rh + 24
    sheet.save(out_dir / f'{tag}-contact.png')

    # Moving clip comparing baseline | candidate | no-diffuse-ray-specular on clip_box (12 fps slow-motion)
    clip_vars = [v for v in ('baseline', 'candidate', 'no-diffuse-ray-specular', 'zero-diffuse') if v in present][:3]
    cw = (col_w // 2) * 2
    ch = (top_h // 2) * 2
    vdir = out_dir / f'{tag}-frames'
    vdir.mkdir(exist_ok=True)
    for idx in range(f1 - f0):
        fidx = f0 + idx
        canvas = Image.new('RGB', (cw * len(clip_vars), ch + 24), '#17191c')
        dv = ImageDraw.Draw(canvas)
        for ci, v in enumerate(clip_vars):
            tile = Image.fromarray(u8s[v][idx, by0:by1, bx0:bx1]).resize((cw, ch), Image.Resampling.BILINEAR)
            canvas.paste(tile, (ci * cw, 24))
            dv.text((ci * cw + 6, 4), f'{tag} | {v} | f={fidx:03d}', fill='white')
        canvas.save(vdir / f'frame-{idx:04d}.png')

    subprocess.run(
        [
            'ffmpeg', '-y', '-loglevel', 'error', '-framerate', '12',
            '-i', str(vdir / 'frame-%04d.png'),
            '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '19',
            str(out_dir / f'{tag}-slow.mp4'),
        ],
        check=True,
    )
    print(f'Done segment {tag}', flush=True)
    return seg_res


def check_cut_transition(shot_dir: Path, out_dir: Path, tag: str, pre_prefix: str, post_prefix: str, variants):
    """Inspect exact pre-cut last frame and post-cut first 3 frames for ghosting or leakage across camera cuts."""
    present = [v for v in variants if (shot_dir / v).exists()]
    u8_pre = {v: np.asarray(Image.open(sorted((shot_dir / v / 'frames').glob(f'{pre_prefix}-*.png'))[-1]).convert('RGB')) for v in present}
    u8_post = {
        v: np.stack([np.asarray(Image.open(p).convert('RGB')) for p in sorted((shot_dir / v / 'frames').glob(f'{post_prefix}-*.png'))[:4]])
        for v in present
    }
    # Measure pixel diff on post-cut frame 0 vs baseline
    base_lin0 = srgb_to_linear(u8_post['baseline'][0]).astype(np.float64) @ LUMA.astype(np.float64)
    diffs = {}
    for v in present:
        vl0 = srgb_to_linear(u8_post[v][0]).astype(np.float64) @ LUMA.astype(np.float64)
        diffs[v] = compare(base_lin0, vl0)
    # Save visual strip: pre-last, post-0, post-1, post-2 for baseline vs candidate
    tw, th = 320, 180
    rows = [v for v in ('baseline', 'candidate') if v in present]
    img = Image.new('RGB', (tw * 4, (th + 22) * len(rows)), '#17191c')
    dr = ImageDraw.Draw(img)
    for ri, v in enumerate(rows):
        frames = [
            ('pre-cut last', u8_pre[v]),
            ('post-cut f0', u8_post[v][0]),
            ('post-cut f1', u8_post[v][1]),
            ('post-cut f2', u8_post[v][2]),
        ]
        for ci, (lbl, arr) in enumerate(frames):
            tile = Image.fromarray(arr).resize((tw, th), Image.Resampling.BILINEAR)
            img.paste(tile, (ci * tw, ri * (th + 22) + 20))
            dr.text((ci * tw + 4, ri * (th + 22) + 3), f'{tag} {v}: {lbl}', fill='white')
    img.save(out_dir / f'{tag}-cut-check.png')
    return diffs


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('shots_root', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    variants = ['baseline', 'candidate', 'no-diffuse-ray-specular', 'zero-diffuse', 'candidate-cap32', 'source-accum']
    report = {'segments': {}, 'cut_checks': {}}

    specs = [
        (
            'shot1-doorway', '0m13-doorway', 's1b', (10, 70), 19,
            {
                'doorway-floor': (220, 440, 620, 700),
                'stair-wall': (130, 220, 300, 410),
                'shuttle-underbelly': (300, 170, 760, 320),
                'doorway-jamb-joint': (90, 260, 260, 560),
            },
            (80, 100, 1180, 710),
        ),
        (
            'shot2-cockpit-dash', '0m27-cockpit-floor', 's2a', (15, 75), 29,
            {
                'cockpit-floor': (320, 460, 960, 710),
                'cockpit-ceiling': (480, 0, 780, 220),
                'cockpit-sidewall': (80, 390, 430, 580),
            },
            (80, 0, 1200, 715),
        ),
        (
            'shot2-cockpit-dash', '0m28-dashboard', 's2b', (5, 45), 9,
            {
                'dashboard-console': (830, 480, 1260, 710),
                'seat-armrest-joint': (410, 470, 680, 640),
                'cabin-wall-behind': (0, 150, 380, 460),
            },
            (0, 120, 1280, 720),
        ),
        (
            'shot3a-arrival-early', '4m14-arrival-tl', 's3a', (16, 80), 46,
            {
                'upper-right-panel': (680, 10, 1180, 260),
                'right-side-wall': (980, 260, 1270, 620),
                'wall-sun-edge': (980, 160, 1270, 360),
                'cockpit-floor': (320, 520, 960, 715),
            },
            (580, 0, 1280, 700),
        ),
        (
            'shot3b-arrival-late', '4m19-arrival-tl', 's3b', (16, 80), 46,
            {
                'upper-right-panel': (680, 10, 1180, 260),
                'right-side-wall': (980, 260, 1270, 620),
                'wall-sun-edge': (980, 160, 1270, 360),
                'cockpit-floor': (320, 520, 960, 715),
            },
            (580, 0, 1280, 700),
        ),
        (
            'shot3c-arrival-1x', '4m19-arrival-1x', 's3c', (16, 120), 60,
            {
                'upper-right-panel': (680, 10, 1180, 260),
                'right-side-wall': (980, 260, 1270, 620),
                'wall-sun-edge': (980, 160, 1270, 360),
                'cockpit-floor': (320, 520, 960, 715),
            },
            (580, 0, 1280, 700),
        ),
        (
            'shot4-berth', '7m27-berth-floor', 's4b', (30, 130), 95,
            {
                'berth-floor-center': (220, 440, 1060, 710),
                'berth-ramp-joint': (400, 320, 740, 520),
                'berth-booth-wall': (700, 300, 1020, 480),
            },
            (80, 220, 1200, 715),
        ),
    ]

    for shot_name, tag, prefix, fslice, kf, crops, clip_box in specs:
        sdir = args.shots_root / shot_name
        if not (sdir / 'baseline').exists():
            continue
        report['segments'][tag] = analyze_segment(sdir, args.output, tag, prefix, fslice, kf, crops, clip_box, variants)

    for shot_name, tag, pre_p, post_p in [
        ('shot1-doorway', '0m12-cut-terminal-to-pad', 's1a', 's1b'),
        ('shot2-cockpit-dash', '0m27-cut-cabin-to-seated', 's2a', 's2b'),
        ('shot4-berth', '7m25-cut-work-to-deck', 's4a', 's4b'),
    ]:
        sdir = args.shots_root / shot_name
        if (sdir / 'baseline').exists():
            report['cut_checks'][tag] = check_cut_transition(sdir, args.output, tag, pre_p, post_p, variants)

    (args.output / 'shots-results.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Wrote', args.output / 'shots-results.json')


if __name__ == '__main__':
    main()
