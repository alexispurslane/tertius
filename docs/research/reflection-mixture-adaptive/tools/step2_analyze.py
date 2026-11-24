#!/usr/bin/env python3
"""Analyze Step 2 lossless MKV films, share dumps, cut-settling, flicker/beats, motion, determinism, and encode CRF 12 MP4s."""
from concurrent.futures import ProcessPoolExecutor
import hashlib
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import median_filter, maximum_filter

sys.path.insert(0, "/private/tmp/t1-wl7c/fe63-d1")
import share_analyze as sa

FILMS = Path("/private/tmp/t1-wl7c/fe63-d1/films")
EV = Path("/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63-d1")
EV.mkdir(parents=True, exist_ok=True)


def iter_mkv_frames(mkv: Path, width: int = 1920, height: int = 1080):
    """Yield (frame_index, RGB uint8 ndarray [H, W, 3]) losslessly from an MKV via ffmpeg raw pipe."""
    cmd = [
        "ffmpeg", "-v", "error", "-i", str(mkv),
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"
    ]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    nbytes = width * height * 3
    idx = 0
    try:
        while True:
            buf = proc.stdout.read(nbytes)
            if len(buf) < nbytes:
                break
            yield idx, np.frombuffer(buf, dtype=np.uint8).reshape(height, width, 3)
            idx += 1
    finally:
        proc.stdout.close()
        proc.wait()


def rgb_to_pil_l(rgb: np.ndarray) -> np.ndarray:
    """Match PIL Image.convert('L') ITU-R 601-2 luma (R*299 + G*587 + B*114) / 1000."""
    return (rgb[..., 0].astype(np.float64) * 299.0 +
            rgb[..., 1].astype(np.float64) * 587.0 +
            rgb[..., 2].astype(np.float64) * 114.0) / 1000.0


def _count_specks_gray(g: np.ndarray) -> int:
    lamp = maximum_filter(g > 200.0, size=13)
    return int(((g - median_filter(g, size=7) > 12.0) & ~lamp).sum())


def count_specks(rgb: np.ndarray) -> int:
    return _count_specks_gray(rgb_to_pil_l(rgb[0:500, 480:1880]).astype(np.float32))


def nose_speck_series(mkv: Path, max_frames: int = 360, start_frame: int = 0):
    grays = []
    for idx, rgb in iter_mkv_frames(mkv):
        if idx >= max_frames:
            break
        if idx >= start_frame:
            grays.append(rgb_to_pil_l(rgb[0:500, 480:1880]).astype(np.float32))
    with ProcessPoolExecutor(max_workers=8) as ex:
        return list(ex.map(_count_specks_gray, grays, chunksize=8))


def flicker_metrics(mkv: Path, box, a: int, b: int):
    x0, y0, x1, y1 = box
    frames = []
    for idx, rgb in iter_mkv_frames(mkv):
        if a <= idx < b:
            frames.append(rgb_to_pil_l(rgb[y0:y1, x0:x1]))
        elif idx >= b:
            break
    g = np.stack(frames, axis=0)
    m = g.mean((1, 2))
    n = len(m)
    spec = np.abs(np.fft.rfft(m - m.mean())) * 2.0 / n
    f4 = n // 4
    f2 = n // 2
    other = np.delete(spec[1:], [f4 - 1, f2 - 1])
    return {
        "frames": n,
        "frame_to_frame_rms": float(np.sqrt((np.diff(g, axis=0) ** 2).mean())),
        "mean_temporal_std_per_pixel": float(g.std(0).mean()),
        "box_mean_gray": float(m.mean()),
        "std_of_box_mean": float(m.std()),
        "period_4_amplitude": float(spec[f4]),
        "period_2_amplitude": float(spec[f2]),
        "largest_other_amplitude": float(other.max()),
    }


def encode_side_by_side_crop_mp4(takes, box, a: int, b: int, out_mp4: Path):
    """Stream 1:1 crops side-by-side straight into ffmpeg libx264 CRF 12 at 60 fps (zero temp files)."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pad, hdr = 8, 22
    W = len(takes) * w + pad * (len(takes) - 1)
    W += W % 2
    H = h + hdr
    H += H % 2

    iters = [iter_mkv_frames(p) for _, p in takes]
    enc = subprocess.Popen([
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", "60", "-i", "-",
        "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", str(out_mp4)
    ], stdin=subprocess.PIPE)
    try:
        for k in range(b):
            cur = [next(it) for it in iters]
            if k < a:
                continue
            canvas = Image.new("RGB", (W, H), "white")
            dr = ImageDraw.Draw(canvas)
            for j, ((label, _), (_, rgb)) in enumerate(zip(takes, cur)):
                crop = Image.fromarray(rgb[y0:y1, x0:x1])
                canvas.paste(crop, (j * (w + pad), hdr))
                dr.text((j * (w + pad) + 4, 4), f"{label}  frame {k}", fill="black")
            enc.stdin.write(canvas.tobytes())
    finally:
        enc.stdin.close()
        enc.wait()
        for it in iters:
            it.close()
    assert enc.returncode == 0, f"ffmpeg failed for {out_mp4}"


def encode_motion_mp4_and_sheet(left_lbl, left_mkv: Path, right_lbl, right_mkv: Path,
                                share_prefix: Path, ceil: float,
                                start_f: int, end_f: int, out_mp4: Path, out_sheet: Path,
                                sheet_frames, share_offset: int = 0):
    """Side-by-side 960x540 motion video + bottom bilinear share map strip, plus visual contact sheet."""
    sh, _ = sa.load_shares(share_prefix)
    pw, ph = 800, 450
    sw, sh_h = 400, 225
    pad, hdr = 8, 24
    W = 2 * pw + pad
    W += W % 2
    H = hdr + ph + pad + hdr + sh_h
    H += H % 2

    it_l = iter_mkv_frames(left_mkv)
    it_r = iter_mkv_frames(right_mkv)
    enc = subprocess.Popen([
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", "60", "-i", "-",
        "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", str(out_mp4)
    ], stdin=subprocess.PIPE)

    saved_sheet = []
    sheet_set = set(sheet_frames)
    try:
        for k in range(end_f):
            _, rgb_l = next(it_l)
            _, rgb_r = next(it_r)
            if k < start_f:
                continue
            d_idx = min(k + share_offset, sh.shape[0] - 1)
            sh_frame = sh[d_idx]
            sh_rgb = sa.colormap(sh_frame, ceil)

            canvas = Image.new("RGB", (W, H), "white")
            dr = ImageDraw.Draw(canvas)
            im_l = Image.fromarray(rgb_l).resize((pw, ph), Image.BILINEAR)
            im_r = Image.fromarray(rgb_r).resize((pw, ph), Image.BILINEAR)
            canvas.paste(im_l, (0, hdr))
            canvas.paste(im_r, (pw + pad, hdr))
            dr.text((4, 5), f"{left_lbl}  frame {k}", fill="black")
            dr.text((pw + pad + 4, 5), f"{right_lbl}  frame {k}", fill="black")

            by = hdr + ph + pad
            im_sh_near = Image.fromarray(sh_rgb).resize((sw, sh_h), Image.NEAREST)
            im_sh_bi = Image.fromarray(sh_rgb).resize((sw, sh_h), Image.BILINEAR)
            # Also 1:1 ceiling crop (center 360x225 of top ceiling) from left and right
            c_l = Image.fromarray(rgb_l[0:225, 780:1140])
            c_r = Image.fromarray(rgb_r[0:225, 780:1140])
            canvas.paste(im_sh_near, (0, by + hdr))
            canvas.paste(im_sh_bi, (sw + pad, by + hdr))
            canvas.paste(c_l, (2 * (sw + pad), by + hdr))
            canvas.paste(c_r, (2 * (sw + pad) + 360 + pad, by + hdr))
            dr.text((4, by + 5), f"tile share (0..{ceil:.2f}) mean={sh_frame.mean():.3f} max={sh_frame.max():.3f}", fill="black")
            dr.text((sw + pad + 4, by + 5), "bilinear sampled share", fill="black")
            dr.text((2 * (sw + pad) + 4, by + 5), f"{left_lbl} 1:1 top crop", fill="black")
            dr.text((2 * (sw + pad) + 360 + pad + 4, by + 5), f"{right_lbl} 1:1 top crop", fill="black")

            enc.stdin.write(canvas.tobytes())
            if k in sheet_set:
                saved_sheet.append((k, canvas.copy()))
    finally:
        enc.stdin.close()
        enc.wait()
        it_l.close()
        it_r.close()
    assert enc.returncode == 0, f"ffmpeg failed for {out_mp4}"

    if saved_sheet:
        # Stack chosen frames vertically (scaled down 0.5x so easy to inspect cleanly)
        sw2, sh2 = W // 2, H // 2
        sheet = Image.new("RGB", (sw2, len(saved_sheet) * (sh2 + 6) - 6), "white")
        for idx, (_, fr) in enumerate(saved_sheet):
            sheet.paste(fr.resize((sw2, sh2), Image.BILINEAR), (0, idx * (sh2 + 6)))
        sheet.save(out_sheet)


def make_cut_settling_plot(nose_specks, cut_specks, cut_shares, out_png: Path):
    """Plot BOTH cut regimes: (left) initial-share start on nose-film, (right) 180-deg cut from share-0 on cut-hall."""
    pw, ph = 560, 280
    ml, mr, mt, mb = 58, 16, 32, 34
    pad = 16
    canvas = Image.new("RGB", (2 * pw + 3 * pad, 2 * ph + 3 * pad), "white")

    def draw_panel(col_i, row_i, title, x_label, ymax, series_list, n_x):
        im = Image.new("RGB", (pw, ph), "white")
        dr = ImageDraw.Draw(im)
        dr.text((8, 6), title, fill="black")
        x0, y0, x1, y1 = ml, mt, pw - mr, ph - mb
        dr.rectangle([x0, y0, x1, y1], outline=(180, 180, 180))
        for frac in [0.0, 0.25, 0.5, 0.75, 1.0]:
            yv = frac * ymax
            yp = int(round(y1 - frac * (y1 - y0)))
            dr.line([(x0, yp), (x1, yp)], fill=(235, 235, 235))
            lbl = f"{yv:.2f}" if ymax <= 1.0 else f"{int(round(yv))}"
            dr.text((4, yp - 6), lbl, fill=(80, 80, 80))
        step_x = max(1, n_x // 6)
        for d in range(0, n_x, step_x):
            xp = int(round(x0 + (d / max(n_x - 1, 1)) * (x1 - x0)))
            dr.line([(xp, y0), (xp, y1)], fill=(235, 235, 235))
            dr.text((xp - 6, y1 + 4), str(d), fill=(80, 80, 80))
        dr.text((x0 + (x1 - x0) // 2 - 36, ph - 14), x_label, fill=(60, 60, 60))

        for i_s, (lbl, col, vals) in enumerate(series_list):
            pts = [(int(round(x0 + (i / max(n_x - 1, 1)) * (x1 - x0))),
                    int(round(y1 - min(vals[i] / max(ymax, 1e-6), 1.0) * (y1 - y0))))
                   for i in range(min(len(vals), n_x))]
            if len(pts) >= 2:
                dr.line(pts, fill=col, width=2)
            lx = x1 - 210
            ly = y0 + 6 + i_s * 14
            dr.line([(lx, ly + 5), (lx + 14, ly + 5)], fill=col, width=2)
            dr.text((lx + 18, ly), lbl, fill="black")
        canvas.paste(im, (pad + col_i * (pw + pad), pad + row_i * (ph + pad)))

    C_MAIN = (120, 120, 120)
    C_025 = (31, 119, 180)
    C_050 = (214, 39, 40)

    # Top-left: nose-film specks frames 0..120 (from initial share = ceiling)
    ymax_n = max(max(nose_specks["main"][:120]), 1000)
    draw_panel(0, 0, "Sunlit nose from initial share=ceiling: specks (frames 0..119)",
               "frame index", ymax_n, [
                   (f"main (med={int(np.median(nose_specks['main'][:120]))})", C_MAIN, nose_specks["main"][:120]),
                   (f"D1 0.25 (med={int(np.median(nose_specks['d1_025'][:120]))})", C_025, nose_specks["d1_025"][:120]),
                   (f"D1 0.50 (med={int(np.median(nose_specks['d1_050'][:120]))})", C_050, nose_specks["d1_050"][:120]),
               ], 120)

    # Top-right: cut-hall post-cut specks (frames 61..147 = 0..86 after 180-deg cut from share=0)
    ymax_c = max(max(cut_specks["main"][61:148]), 1000)
    draw_panel(1, 0, "180-deg cut from share-0 aft view: post-cut specks (frames 0..86)",
               "frames after cut", ymax_c, [
                   (f"main (f0={cut_specks['main'][61]})", C_MAIN, cut_specks["main"][61:148]),
                   (f"D1 0.25 (f0={cut_specks['d1_025'][61]}, f30={cut_specks['d1_025'][91]})", C_025, cut_specks["d1_025"][61:148]),
                   (f"D1 0.50 (f0={cut_specks['d1_050'][61]}, f30={cut_specks['d1_050'][91]})", C_050, cut_specks["d1_050"][61:148]),
               ], 87)

    # Bottom-left: nose-film ceiling-box share from initial share (draws 0..119)
    sh_n25, _ = sa.load_shares(FILMS / "nose-d1-025/d")
    sh_n50, _ = sa.load_shares(FILMS / "nose-d1-050/d")
    tx0, ty0, tx1, ty1 = round(750 / 24), round(0 / 24), round(1170 / 24), round(330 / 24)
    n25_box = sh_n25[:120, ty0:ty1, tx0:tx1].mean(axis=(1, 2))
    n50_box = sh_n50[:120, ty0:ty1, tx0:tx1].mean(axis=(1, 2))
    draw_panel(0, 1, "Sunlit nose from initial share=ceiling: ceiling-box share",
               "draw index", 0.55, [
                   (f"D1 0.25 (d0={n25_box[0]:.3f}->d119={n25_box[-1]:.3f})", C_025, n25_box),
                   (f"D1 0.50 (d0={n50_box[0]:.3f}->d119={n50_box[-1]:.3f})", C_050, n50_box),
               ], 120)

    # Bottom-right: cut-hall ceiling-box share across cut (aligned: post-cut draw 0 = share draw 93 = film frame 61)
    c25_box = cut_shares["d1_025"][:, ty0:ty1, tx0:tx1].mean(axis=(1, 2))
    c50_box = cut_shares["d1_050"][:, ty0:ty1, tx0:tx1].mean(axis=(1, 2))
    draw_panel(1, 1, "180-deg cut from share-0 aft view: ceiling-box share (aligned)",
               "frames after cut", 0.55, [
                   (f"D1 0.25 (d0={c25_box[93]:.3f}, d16={c25_box[109]:.3f})", C_025, c25_box[93:180]),
                   (f"D1 0.50 (d0={c50_box[93]:.3f}, d30={c50_box[123]:.3f})", C_050, c50_box[93:180]),
               ], 87)

    canvas.save(out_png)


def ffprobe_info(mp4: Path):
    out = subprocess.check_output([
        "ffprobe", "-v", "error", "-select_streams", "v:0",
        "-show_entries", "stream=codec_name,width,height,r_frame_rate,nb_frames,pix_fmt",
        "-of", "json", str(mp4)
    ], text=True)
    return json.loads(out)["streams"][0]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    summary_path = FILMS / "step2-summary.json"
    summary = json.loads(summary_path.read_text()) if summary_path.exists() else {}

    if mode in ("all", "batchA"):
        print("Analyzing Batch A (nose + lamps films)...")
        summary["flicker_nose"] = {
            "main": flicker_metrics(FILMS / "nose-main/film.mkv", (750, 0, 1170, 330), 120, 360),
            "d1_025": flicker_metrics(FILMS / "nose-d1-025/film.mkv", (750, 0, 1170, 330), 120, 360),
            "d1_050": flicker_metrics(FILMS / "nose-d1-050/film.mkv", (750, 0, 1170, 330), 120, 360),
        }
        summary["flicker_lamps"] = {
            "main": flicker_metrics(FILMS / "lamps-main/film.mkv", (600, 620, 1320, 1000), 120, 360),
            "d1_025": flicker_metrics(FILMS / "lamps-d1-025/film.mkv", (600, 620, 1320, 1000), 120, 360),
            "d1_050": flicker_metrics(FILMS / "lamps-d1-050/film.mkv", (600, 620, 1320, 1000), 120, 360),
        }
        nose_specks = {
            "main": nose_speck_series(FILMS / "nose-main/film.mkv", 360),
            "d1_025": nose_speck_series(FILMS / "nose-d1-025/film.mkv", 360),
            "d1_050": nose_speck_series(FILMS / "nose-d1-050/film.mkv", 360),
        }
        summary["nose_film_specks"] = {
            k: {
                "draw0": v[0], "draw1": v[1], "draw2": v[2], "draw5": v[5], "draw10": v[10],
                "draw30": v[30], "draw60": v[60],
                "first_30_median": float(np.median(v[:30])),
                "first_30_max": int(max(v[:30])),
                "settled_120_359_median": float(np.median(v[120:360])),
                "settled_120_359_max": int(max(v[120:360])),
                "series_0_119": v[:120],
            } for k, v in nose_specks.items()
        }
        encode_side_by_side_crop_mp4([
            ("main", FILMS / "nose-main/film.mkv"),
            ("D1 0.25", FILMS / "nose-d1-025/film.mkv"),
            ("D1 0.50", FILMS / "nose-d1-050/film.mkv"),
        ], (750, 0, 1170, 330), 120, 360, EV / "nose-film-main-d1-025-050.mp4")
        encode_side_by_side_crop_mp4([
            ("main", FILMS / "lamps-main/film.mkv"),
            ("D1 0.25", FILMS / "lamps-d1-025/film.mkv"),
            ("D1 0.50", FILMS / "lamps-d1-050/film.mkv"),
        ], (600, 620, 1320, 1000), 120, 360, EV / "lamps-floor-film-main-d1-025-050.mp4")

        summary.setdefault("ffprobe", {})["nose-film-main-d1-025-050.mp4"] = ffprobe_info(EV / "nose-film-main-d1-025-050.mp4")
        summary["ffprobe"]["lamps-floor-film-main-d1-025-050.mp4"] = ffprobe_info(EV / "lamps-floor-film-main-d1-025-050.mp4")

    if mode in ("all", "batchB"):
        print("Analyzing Batch B (pan, walk, cut, determinism)...")
        encode_motion_mp4_and_sheet(
            "main", FILMS / "pan-main/film.mkv",
            "D1 0.25", FILMS / "pan-d1-025/film.mkv",
            FILMS / "pan-d1-025/d", 0.25, 60, 268,
            EV / "pan-hall-main-vs-d1-025.mp4", EV / "pan-frames-check.png",
            [60, 112, 164, 216, 267], share_offset=32,
        )
        encode_motion_mp4_and_sheet(
            "D1 0.25", FILMS / "pan-d1-025/film.mkv",
            "D1 0.50", FILMS / "pan-d1-050/film.mkv",
            FILMS / "pan-d1-050/d", 0.50, 60, 268,
            EV / "pan-hall-d1-025-vs-050.mp4", EV / "pan-025-vs-050-check.png",
            [60, 164, 267], share_offset=32,
        )
        encode_motion_mp4_and_sheet(
            "main", FILMS / "walk-main/film.mkv",
            "D1 0.25", FILMS / "walk-d1-025/film.mkv",
            FILMS / "walk-d1-025/d", 0.25, 60, 208,
            EV / "walk-hall-main-vs-d1-025.mp4", EV / "walk-frames-check.png",
            [60, 110, 160, 207], share_offset=32,
        )
        encode_motion_mp4_and_sheet(
            "main", FILMS / "cut-main/film.mkv",
            "D1 0.25", FILMS / "cut-d1-025/film.mkv",
            FILMS / "cut-d1-025/d", 0.25, 55, 148,
            EV / "cut-hall-main-vs-d1-025.mp4", EV / "cut-frames-check.png",
            [60, 61, 66, 72, 78, 120], share_offset=32,
        )

        # Pan & walk share-tracking stats + speck counts
        pan_specks = {
            "main": nose_speck_series(FILMS / "pan-main/film.mkv", 300, 60),
            "d1_025": nose_speck_series(FILMS / "pan-d1-025/film.mkv", 300, 60),
            "d1_050": nose_speck_series(FILMS / "pan-d1-050/film.mkv", 300, 60),
        }
        walk_specks = {
            "main": nose_speck_series(FILMS / "walk-main/film.mkv", 240, 60),
            "d1_025": nose_speck_series(FILMS / "walk-d1-025/film.mkv", 240, 60),
            "d1_050": nose_speck_series(FILMS / "walk-d1-050/film.mkv", 240, 60),
        }
        cut_specks = {
            "main": [0] * 60 + nose_speck_series(FILMS / "cut-main/film.mkv", 180, 60),
            "d1_025": [0] * 60 + nose_speck_series(FILMS / "cut-d1-025/film.mkv", 180, 60),
            "d1_050": [0] * 60 + nose_speck_series(FILMS / "cut-d1-050/film.mkv", 180, 60),
        }
        summary["motion_specks"] = {
            "pan": {k: {"median": float(np.median(v)), "p95": float(np.percentile(v, 95)), "max": int(max(v))} for k, v in pan_specks.items()},
            "walk": {k: {"median": float(np.median(v)), "p95": float(np.percentile(v, 95)), "max": int(max(v))} for k, v in walk_specks.items()},
        }

        sh_c25, _ = sa.load_shares(FILMS / "cut-d1-025/d")
        sh_c50, _ = sa.load_shares(FILMS / "cut-d1-050/d")
        tx0, ty0, tx1, ty1 = round(750 / 24), round(0 / 24), round(1170 / 24), round(330 / 24)
        # Note: film frame k corresponds to share draw index k + 32 (32 warm-up draws after run 277003).
        # The 180-deg cut takes effect on film frame 61 (= share draw 93); frame 60 (= share draw 92) is the last aft frame.
        cut_stats = {}
        for tag, sh, ceil in [("d1_025", sh_c25, 0.25), ("d1_050", sh_c50, 0.50)]:
            box_m = sh[:, ty0:ty1, tx0:tx1].mean(axis=(1, 2))
            pre_settled = float(box_m[85:93].mean())
            post_settled = float(box_m[145:180].mean())
            post = box_m[93:180]  # post-cut draw 0 = film frame 61
            d50 = next((i for i, v in enumerate(post) if v >= 0.5 * post_settled), len(post))
            d90 = next((i for i, v in enumerate(post) if v >= 0.9 * post_settled), len(post))
            sp = cut_specks[tag][61:180]  # post-cut frame 0 = film frame 61
            cut_stats[tag] = {
                "warmup_draw_offset": 32,
                "cut_film_frame": 61,
                "cut_share_draw": 93,
                "pre_cut_aft_mean_share": pre_settled,
                "post_cut_settled_share": post_settled,
                "draws_to_50pct_settled": int(d50),
                "draws_to_90pct_settled": int(d90),
                "post_cut_specks_d0": int(sp[0]),
                "post_cut_specks_d5": int(sp[5]),
                "post_cut_specks_d15": int(sp[15]),
                "post_cut_specks_d30": int(sp[30]),
                "post_cut_specks_d60": int(sp[60]),
                "post_cut_specks_60_118_median": float(np.median(sp[60:119])),
            }
        sp_m = cut_specks["main"][61:180]
        cut_stats["main"] = {
            "post_cut_specks_d0": int(sp_m[0]),
            "post_cut_specks_d5": int(sp_m[5]),
            "post_cut_specks_d15": int(sp_m[15]),
            "post_cut_specks_d30": int(sp_m[30]),
            "post_cut_specks_d60": int(sp_m[60]),
            "post_cut_specks_60_118_median": float(np.median(sp_m[60:119])),
        }
        summary["cut_from_share0"] = cut_stats

        # Cut settling plot (using saved nose_film_specks series + cut_specks + cut shares)
        nose_sp_for_plot = {k: summary["nose_film_specks"][k]["series_0_119"] for k in ("main", "d1_025", "d1_050")}
        make_cut_settling_plot(nose_sp_for_plot, cut_specks, {"d1_025": sh_c25, "d1_050": sh_c50}, EV / "cut-settling-plot.png")

        # Determinism check
        p1 = (FILMS / "det-walk-r1/w-000179.png").read_bytes()
        p2 = (FILMS / "det-walk-r2/w-000179.png").read_bytes()
        s1 = (FILMS / "det-walk-r1/d.shares.f32").read_bytes()
        s2 = (FILMS / "det-walk-r2/d.shares.f32").read_bytes()
        sw = (FILMS / "walk-d1-025/d.shares.f32").read_bytes()
        summary["determinism"] = {
            "png_bit_identical": p1 == p2,
            "png_bytes": len(p1),
            "png_sha256_r1": hashlib.sha256(p1).hexdigest(),
            "png_sha256_r2": hashlib.sha256(p2).hexdigest(),
            "shares_bit_identical": s1 == s2,
            "shares_match_video_run": s1 == sw,
            "shares_bytes": len(s1),
            "shares_sha256_r1": hashlib.sha256(s1).hexdigest(),
            "shares_sha256_r2": hashlib.sha256(s2).hexdigest(),
            "shares_sha256_video_run": hashlib.sha256(sw).hexdigest(),
        }

        for fn in [
            "pan-hall-main-vs-d1-025.mp4",
            "pan-hall-d1-025-vs-050.mp4",
            "walk-hall-main-vs-d1-025.mp4",
            "cut-hall-main-vs-d1-025.mp4",
        ]:
            summary.setdefault("ffprobe", {})[fn] = ffprobe_info(EV / fn)

    summary_path.write_text(json.dumps(summary, indent=2))
    (EV / "step2-summary.json").write_text(json.dumps(summary, indent=2))
    print(f"STEP2_ANALYZE_{mode.upper()}_DONE")


if __name__ == "__main__":
    main()
