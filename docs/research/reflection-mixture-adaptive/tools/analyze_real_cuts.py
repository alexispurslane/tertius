#!/usr/bin/env python3
"""Analyze forward (aft->nose) and reverse (aft->lamps floor) real camera cuts (unpatched main vs D1 0.25 reset-off vs reset-on)."""
import json
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, median_filter, maximum_filter

sys.path.insert(0, "/private/tmp/t1-wl7c/fe63-d1")
import share_analyze as sa

ROOT = Path("/private/tmp/t1-wl7c/fe63-d1/real-cuts")
EV = Path("/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63-d1")
REF_LAMPS = Path("/private/tmp/t1-wl7c/fe63/daub-lamps-main/reference-mean.npy")
S1_MEANS_LAMPS = Path("/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/docs/research/reflection-mixture/tables/means-lamps.json")
EV.mkdir(parents=True, exist_ok=True)

W = np.array([0.2126, 0.7152, 0.0722], dtype=np.float64)


def load_mkv_frames(mkv: Path, width: int = 1920, height: int = 1080):
    cmd = ["ffmpeg", "-v", "error", "-i", str(mkv), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    nbytes = width * height * 3
    frames = []
    try:
        while True:
            buf = proc.stdout.read(nbytes)
            if len(buf) < nbytes:
                break
            frames.append(np.frombuffer(buf, dtype=np.uint8).reshape(height, width, 3).copy())
    finally:
        proc.stdout.close()
        proc.wait()
    return frames


def rgb_to_luma_u8(rgb: np.ndarray) -> np.ndarray:
    return (rgb[..., 0].astype(np.float32) * 299.0 +
            rgb[..., 1].astype(np.float32) * 587.0 +
            rgb[..., 2].astype(np.float32) * 114.0) / 1000.0


def srgb_to_lin_y(rgb: np.ndarray) -> np.ndarray:
    a = rgb.astype(np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W


def _count_specks_gray(g: np.ndarray) -> int:
    lamp = maximum_filter(g > 200.0, size=13)
    return int(((g - median_filter(g, size=7) > 12.0) & ~lamp).sum())


def parse_events(prefix: Path):
    rows = []
    lines = Path(f"{prefix}.shares.events.tsv").read_text().strip().splitlines()
    hdr = lines[0].split("\t")
    for ln in lines[1:]:
        vals = [int(x) for x in ln.split("\t")]
        rows.append(dict(zip(hdr, vals)))
    # Map film frame index (takeCapture==1) to draw index
    cap_draws = [r["draw"] for r in rows if r["takeCapture"] == 1]
    return rows, cap_draws


def box_tile_mean(sh: np.ndarray, box):
    x0, y0, x1, y1 = box
    tx0, ty0 = round(x0 / 24.0), round(y0 / 24.0)
    tx1, ty1 = round(x1 / 24.0), round(y1 / 24.0)
    return sh[:, ty0:ty1, tx0:tx1].mean(axis=(1, 2))


def ffprobe_info(mp4: Path):
    out = subprocess.check_output([
        "ffprobe", "-v", "error", "-select_streams", "v:0",
        "-show_entries", "stream=codec_name,width,height,r_frame_rate,nb_frames,pix_fmt",
        "-of", "json", str(mp4)
    ], text=True)
    return json.loads(out)["streams"][0]


def encode_triple_mp4(frames_dict, labels, box, start_f, end_f, out_mp4: Path, per_frame_notes=None):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pad, hdr = 8, 28
    W_tot = 3 * w + 2 * pad
    W_tot += W_tot % 2
    H_tot = h + hdr
    H_tot += H_tot % 2
    enc = subprocess.Popen([
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W_tot}x{H_tot}", "-r", "60", "-i", "-",
        "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", str(out_mp4)
    ], stdin=subprocess.PIPE)
    try:
        for k in range(start_f, end_f):
            canvas = Image.new("RGB", (W_tot, H_tot), "white")
            dr = ImageDraw.Draw(canvas)
            for j, (key, lbl) in enumerate(labels):
                crop = Image.fromarray(frames_dict[key][k][y0:y1, x0:x1])
                ox = j * (w + pad)
                canvas.paste(crop, (ox, hdr))
                note = per_frame_notes[key](k) if per_frame_notes and key in per_frame_notes else ""
                rel = f"d{k - 61}" if k >= 61 else ("far1" if k == 60 else f"aft{k}")
                dr.text((ox + 4, 6), f"{lbl}  {rel}  {note}", fill="black")
            enc.stdin.write(canvas.tobytes())
    finally:
        enc.stdin.close()
        enc.wait()
    assert enc.returncode == 0, f"ffmpeg failed for {out_mp4}"


def make_1x_strip(frames_dict, rows_spec, post_draws, box, out_png: Path, cut_frame: int = 61, note_fn=None):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pad, hdr, row_lbl_h = 6, 22, 20
    W_tot = len(post_draws) * (w + pad) - pad
    H_tot = len(rows_spec) * (h + hdr + row_lbl_h + pad) - pad
    canvas = Image.new("RGB", (W_tot, H_tot), "white")
    dr = ImageDraw.Draw(canvas)
    for r, (key, row_title) in enumerate(rows_spec):
        oy = r * (h + hdr + row_lbl_h + pad)
        dr.text((4, oy + 3), row_title, fill="black")
        for c, d in enumerate(post_draws):
            k = cut_frame + d
            ox = c * (w + pad)
            crop = Image.fromarray(frames_dict[key][k][y0:y1, x0:x1])
            canvas.paste(crop, (ox, oy + row_lbl_h + hdr))
            extra = note_fn(key, d) if note_fn else ""
            dr.text((ox + 3, oy + row_lbl_h + 4), f"d{d} {extra}", fill="black")
    canvas.save(out_png)


def make_plots(nose_res, lamps_res, out_png: Path):
    pw, ph = 560, 270
    ml, mr, mt, mb = 58, 16, 32, 34
    pad = 16
    canvas = Image.new("RGB", (2 * pw + 3 * pad, 2 * ph + 3 * pad), "white")

    def draw_panel(ci, ri, title, xlabel, ymax, series_list, n_x):
        im = Image.new("RGB", (pw, ph), "white")
        dr = ImageDraw.Draw(im)
        dr.text((8, 6), title, fill="black")
        x0, y0, x1, y1 = ml, mt, pw - mr, ph - mb
        dr.rectangle([x0, y0, x1, y1], outline=(180, 180, 180))
        for frac in [0.0, 0.25, 0.5, 0.75, 1.0]:
            yv = frac * ymax
            yp = int(round(y1 - frac * (y1 - y0)))
            dr.line([(x0, yp), (x1, yp)], fill=(235, 235, 235))
            lbl = f"{yv:.4f}" if ymax < 0.05 else (f"{yv:.2f}" if ymax <= 1.0 else f"{int(round(yv))}")
            dr.text((3, yp - 6), lbl, fill=(80, 80, 80))
        for d in [0, 5, 10, 15, 20, 29, 45, 60]:
            if d >= n_x:
                continue
            xp = int(round(x0 + (d / max(n_x - 1, 1)) * (x1 - x0)))
            dr.line([(xp, y0), (xp, y1)], fill=(235, 235, 235))
            dr.text((xp - 6, y1 + 4), str(d), fill=(80, 80, 80))
        dr.text((x0 + (x1 - x0) // 2 - 42, ph - 14), xlabel, fill=(60, 60, 60))
        for i_s, (lbl, col, vals) in enumerate(series_list):
            pts = [(int(round(x0 + (i / max(n_x - 1, 1)) * (x1 - x0))),
                    int(round(y1 - min(vals[i] / max(ymax, 1e-9), 1.0) * (y1 - y0))))
                   for i in range(min(len(vals), n_x))]
            if len(pts) >= 2:
                dr.line(pts, fill=col, width=2)
            lx = x1 - 210
            ly = y0 + 6 + i_s * 14
            dr.line([(lx, ly + 5), (lx + 14, ly + 5)], fill=col, width=2)
            dr.text((lx + 18, ly), lbl, fill="black")
        canvas.paste(im, (pad + ci * (pw + pad), pad + ri * (ph + pad)))

    C_MAIN = (110, 110, 110)
    C_OFF = (214, 39, 40)
    C_ON = (31, 119, 180)

    n_sp = nose_res["specks_post"]
    ymax_sp = max(max(n_sp["main"]), max(n_sp["d1_off"]), max(n_sp["d1_on"]), 1000) * 1.05
    draw_panel(0, 0, "Forward real cut (aft->nose): counted specks (d0..d64)", "post-cut draw", ymax_sp, [
        (f"unpatched main (d0={n_sp['main'][0]}, med30={int(nose_res['summary']['main']['d0_d29_median'])})", C_MAIN, n_sp["main"]),
        (f"D1 0.25 reset-off (d0={n_sp['d1_off'][0]}, med30={int(nose_res['summary']['d1_off']['d0_d29_median'])})", C_OFF, n_sp["d1_off"]),
        (f"D1 0.25 reset-on (d0={n_sp['d1_on'][0]}, med30={int(nose_res['summary']['d1_on']['d0_d29_median'])})", C_ON, n_sp["d1_on"]),
    ], 65)

    draw_panel(0, 1, "Forward real cut (aft->nose): ceiling-box tile share (d0..d64)", "post-cut draw", 0.28, [
        (f"reset-off (d0={nose_res['share_post']['d1_off'][0]:.3f}, d15={nose_res['share_post']['d1_off'][15]:.3f})", C_OFF, nose_res["share_post"]["d1_off"]),
        (f"reset-on  (d0={nose_res['share_post']['d1_on'][0]:.3f}, d15={nose_res['share_post']['d1_on'][15]:.3f})", C_ON, nose_res["share_post"]["d1_on"]),
    ], 65)

    l_gr = lamps_res["grain_post"]
    ymax_gr = max(max(l_gr["main"]), max(l_gr["d1_off"]), max(l_gr["d1_on"])) * 1.15
    draw_panel(1, 0, "Reverse real cut (aft->lamps floor): grain RMS (d0..d64)", "post-cut draw", ymax_gr, [
        (f"unpatched main (d0={l_gr['main'][0]:.4f}, d29={l_gr['main'][29]:.4f})", C_MAIN, l_gr["main"]),
        (f"D1 0.25 reset-off (d0={l_gr['d1_off'][0]:.4f}, d29={l_gr['d1_off'][29]:.4f})", C_OFF, l_gr["d1_off"]),
        (f"D1 0.25 reset-on  (d0={l_gr['d1_on'][0]:.4f}, d29={l_gr['d1_on'][29]:.4f})", C_ON, l_gr["d1_on"]),
    ], 65)

    draw_panel(1, 1, "Reverse real cut (aft->lamps floor): floor tile share (d0..d64)", "post-cut draw", 0.28, [
        (f"reset-off (d0={lamps_res['share_post']['d1_off'][0]:.4f}, d29={lamps_res['share_post']['d1_off'][29]:.4f})", C_OFF, lamps_res["share_post"]["d1_off"]),
        (f"reset-on  (d0={lamps_res['share_post']['d1_on'][0]:.4f}, d29={lamps_res['share_post']['d1_on'][29]:.4f})", C_ON, lamps_res["share_post"]["d1_on"]),
    ], 65)

    canvas.save(out_png)


def main():
    CUT_F = 61  # frames 0..59 = aft (60), frame 60 = far1 (1), frames 61..125 = post d0..d64 (65)
    summary = {}

    # ---------------- 1. FORWARD REAL CUT (AFT -> SUNLIT NOSE) ----------------
    print("Loading forward real-cut films (aft -> sunlit nose)...")
    nose_frames = {
        "main": load_mkv_frames(ROOT / "nose-main/film.mkv"),
        "d1_off": load_mkv_frames(ROOT / "nose-d1-off/film.mkv"),
        "d1_on": load_mkv_frames(ROOT / "nose-d1-on/film.mkv"),
    }
    for k, frs in nose_frames.items():
        assert len(frs) == 126, f"nose {k} expected 126 frames, got {len(frs)}"

    nose_ev_off, nose_cap_off = parse_events(ROOT / "nose-d1-off/d")
    nose_ev_on, nose_cap_on = parse_events(ROOT / "nose-d1-on/d")
    sh_n_off, _ = sa.load_shares(ROOT / "nose-d1-off/d")
    sh_n_on, _ = sa.load_shares(ROOT / "nose-d1-on/d")
    nose_box = (750, 0, 1170, 330)
    sh_n_off_box = box_tile_mean(sh_n_off, nose_box)
    sh_n_on_box = box_tile_mean(sh_n_on, nose_box)

    # Verify cut event on film frame 61 (post-000000)
    d_cut_off = nose_cap_off[CUT_F]
    d_cut_on = nose_cap_on[CUT_F]
    ev_cut_off = nose_ev_off[d_cut_off]
    ev_cut_on = nose_ev_on[d_cut_on]
    print(f"Forward cut frame 61 -> draw_off={d_cut_off} ({ev_cut_off}), draw_on={d_cut_on} ({ev_cut_on})")

    # Count specks on post d0..d64
    nose_specks = {}
    for k in ("main", "d1_off", "d1_on"):
        grays = [rgb_to_luma_u8(nose_frames[k][CUT_F + d][0:500, 480:1880]) for d in range(65)]
        with ProcessPoolExecutor(max_workers=8) as ex:
            nose_specks[k] = list(ex.map(_count_specks_gray, grays, chunksize=4))

    nose_share_post = {
        "d1_off": [float(sh_n_off_box[nose_cap_off[CUT_F + d]]) for d in range(65)],
        "d1_on": [float(sh_n_on_box[nose_cap_on[CUT_F + d]]) for d in range(65)],
    }
    nose_res = {
        "cut_event_off": ev_cut_off,
        "cut_event_on": ev_cut_on,
        "pre_cut_aft59_share_off": float(sh_n_off_box[nose_cap_off[59]]),
        "pre_cut_far1_share_off": float(sh_n_off_box[nose_cap_off[60]]),
        "pre_cut_aft59_share_on": float(sh_n_on_box[nose_cap_on[59]]),
        "pre_cut_far1_share_on": float(sh_n_on_box[nose_cap_on[60]]),
        "pre_cut_far1_whole_max_off": float(sh_n_off[nose_cap_off[60]].max()),
        "specks_post": nose_specks,
        "share_post": nose_share_post,
        "summary": {
            k: {
                "d0": nose_specks[k][0],
                "d1": nose_specks[k][1],
                "d2": nose_specks[k][2],
                "d5": nose_specks[k][5],
                "d10": nose_specks[k][10],
                "d15": nose_specks[k][15],
                "d29": nose_specks[k][29],
                "d0_d29_median": float(np.median(nose_specks[k][:30])),
                "d0_d29_max": int(max(nose_specks[k][:30])),
                "d60": nose_specks[k][60],
            } for k in ("main", "d1_off", "d1_on")
        },
    }

    # Encode forward real-cut 60 fps CRF 12 film (1:1 ceiling cone 640,0,1280,360) and 1:1 strip (750,0,1170,330)
    encode_triple_mp4(
        nose_frames,
        [("main", "unpatched main"), ("d1_off", "D1 0.25 reset-off"), ("d1_on", "D1 0.25 reset-on")],
        (640, 0, 1280, 360), 55, 126, EV / "real-cut-nose-main-off-on.mp4",
        per_frame_notes={
            "main": lambda k: f"sp={nose_specks['main'][k-CUT_F]}" if k >= CUT_F else "",
            "d1_off": lambda k: f"sp={nose_specks['d1_off'][k-CUT_F]} sh={nose_share_post['d1_off'][k-CUT_F]:.3f}" if k >= CUT_F else "",
            "d1_on": lambda k: f"sp={nose_specks['d1_on'][k-CUT_F]} sh={nose_share_post['d1_on'][k-CUT_F]:.3f}" if k >= CUT_F else "",
        },
    )
    make_1x_strip(
        nose_frames,
        [("main", "unpatched main (-1.7507 stops, 1:1 crop 750,0,1170,330)"),
         ("d1_off", "Design 1 (ceil 0.25) reset-off (inherits 0.000 from aft)"),
         ("d1_on", "Design 1 (ceil 0.25) reset-on (resets to 0.25 before d0)")],
        [0, 1, 2, 5, 10, 15, 29], nose_box, EV / "real-cut-nose-strip-1x.png",
        cut_frame=CUT_F,
        note_fn=lambda key, d: f"sp={nose_specks[key][d]}" + ("" if key == "main" else f" sh={nose_share_post[key][d]:.3f}"),
    )

    # ---------------- 2. REVERSE REAL CUT (AFT -> RECT-LAMPS GLOSSY FLOOR) ----------------
    print("Loading reverse real-cut films (aft -> rect-lamps glossy floor)...")
    lamps_frames = {
        "main": load_mkv_frames(ROOT / "lamps-main/film.mkv"),
        "d1_off": load_mkv_frames(ROOT / "lamps-d1-off/film.mkv"),
        "d1_on": load_mkv_frames(ROOT / "lamps-d1-on/film.mkv"),
    }
    for k, frs in lamps_frames.items():
        assert len(frs) == 126, f"lamps {k} expected 126 frames, got {len(frs)}"

    lamps_ev_off, lamps_cap_off = parse_events(ROOT / "lamps-d1-off/d")
    lamps_ev_on, lamps_cap_on = parse_events(ROOT / "lamps-d1-on/d")
    sh_l_off, _ = sa.load_shares(ROOT / "lamps-d1-off/d")
    sh_l_on, _ = sa.load_shares(ROOT / "lamps-d1-on/d")
    lamps_box = (600, 620, 1320, 1000)
    x0, y0, x1, y1 = lamps_box
    sh_l_off_box = box_tile_mean(sh_l_off, lamps_box)
    sh_l_on_box = box_tile_mean(sh_l_on, lamps_box)

    d_lcut_off = lamps_cap_off[CUT_F]
    d_lcut_on = lamps_cap_on[CUT_F]
    ev_lcut_off = lamps_ev_off[d_lcut_off]
    ev_lcut_on = lamps_ev_on[d_lcut_on]
    print(f"Reverse cut frame 61 -> draw_off={d_lcut_off} ({ev_lcut_off}), draw_on={d_lcut_on} ({ev_lcut_on})")

    # Reference mean & floor mean for exact grain/blob band decomposition
    ref_y = np.load(REF_LAMPS).astype(np.float64) @ W
    ref_box = ref_y[y0:y1, x0:x1]
    main_floor_mean = json.loads(S1_MEANS_LAMPS.read_text())["lamps-main"]["floor"][2]

    grain_post, blob_post, hp_rms_post, temporal_stats = {}, {}, {}, {}
    for k in ("main", "d1_off", "d1_on"):
        gr_list, bl_list, hp_list = [], [], []
        gray_stack = []
        for d in range(65):
            crop_rgb = lamps_frames[k][CUT_F + d][y0:y1, x0:x1]
            gray_stack.append(rgb_to_luma_u8(crop_rgb).astype(np.float64))
            ly = srgb_to_lin_y(crop_rgb)
            e = ly - ref_box
            g_band = e - gaussian_filter(e, 2)
            b_band = gaussian_filter(e, 2) - gaussian_filter(e, 20)
            hp_self = ly - gaussian_filter(ly, 2)
            gr_list.append(float(np.sqrt((g_band ** 2).mean()) / main_floor_mean))
            bl_list.append(float(np.sqrt((b_band ** 2).mean()) / main_floor_mean))
            hp_list.append(float(np.sqrt((hp_self ** 2).mean()) / main_floor_mean))
        grain_post[k] = gr_list
        blob_post[k] = bl_list
        hp_rms_post[k] = hp_list
        gs = np.stack(gray_stack, axis=0)
        temporal_stats[k] = {}
        for span_name, a, b in [("d0_d29", 0, 30), ("d0_d14", 0, 15), ("d15_d29", 15, 30), ("d30_d64", 30, 65)]:
            sub = gs[a:b]
            temporal_stats[k][span_name] = {
                "frame_to_frame_rms": float(np.sqrt((np.diff(sub, axis=0) ** 2).mean())),
                "mean_temporal_std_per_pixel": float(sub.std(0).mean()),
                "mean_grain": float(np.mean(gr_list[a:b])),
                "max_grain": float(np.max(gr_list[a:b])),
                "mean_hp_rms": float(np.mean(hp_list[a:b])),
            }

    lamps_share_post = {
        "d1_off": [float(sh_l_off_box[lamps_cap_off[CUT_F + d]]) for d in range(65)],
        "d1_on": [float(sh_l_on_box[lamps_cap_on[CUT_F + d]]) for d in range(65)],
    }
    lamps_res = {
        "cut_event_off": ev_lcut_off,
        "cut_event_on": ev_lcut_on,
        "pre_cut_aft59_share_off": float(sh_l_off_box[lamps_cap_off[59]]),
        "pre_cut_far1_share_off": float(sh_l_off_box[lamps_cap_off[60]]),
        "pre_cut_aft59_share_on": float(sh_l_on_box[lamps_cap_on[59]]),
        "pre_cut_far1_share_on": float(sh_l_on_box[lamps_cap_on[60]]),
        "grain_post": grain_post,
        "blob_post": blob_post,
        "hp_rms_post": hp_rms_post,
        "share_post": lamps_share_post,
        "temporal": temporal_stats,
        "key_draws_grain": {
            k: {f"d{d}": grain_post[k][d] for d in [0, 1, 2, 5, 10, 15, 25, 29, 60]}
            for k in ("main", "d1_off", "d1_on")
        },
        "key_draws_hp_rms": {
            k: {f"d{d}": hp_rms_post[k][d] for d in [0, 1, 2, 5, 10, 15, 25, 29, 60]}
            for k in ("main", "d1_off", "d1_on")
        },
        "key_draws_share": {
            k: {f"d{d}": lamps_share_post[k][d] for d in [0, 1, 2, 5, 10, 15, 25, 29, 60]}
            for k in ("d1_off", "d1_on")
        },
    }

    encode_triple_mp4(
        lamps_frames,
        [("main", "unpatched main"), ("d1_off", "D1 0.25 reset-off"), ("d1_on", "D1 0.25 reset-on")],
        lamps_box, 55, 126, EV / "real-cut-lamps-main-off-on.mp4",
        per_frame_notes={
            "main": lambda k: f"gr={grain_post['main'][k-CUT_F]:.4f}" if k >= CUT_F else "",
            "d1_off": lambda k: f"gr={grain_post['d1_off'][k-CUT_F]:.4f} sh={lamps_share_post['d1_off'][k-CUT_F]:.3f}" if k >= CUT_F else "",
            "d1_on": lambda k: f"gr={grain_post['d1_on'][k-CUT_F]:.4f} sh={lamps_share_post['d1_on'][k-CUT_F]:.3f}" if k >= CUT_F else "",
        },
    )
    # 1:1 early post-cut crop strip on glossy floor highlight (760,660,1160,960)
    make_1x_strip(
        lamps_frames,
        [("main", "unpatched main (3.83 stops, 1:1 floor crop 760,660,1160,960)"),
         ("d1_off", "Design 1 (ceil 0.25) reset-off (stays at share 0.000)"),
         ("d1_on", "Design 1 (ceil 0.25) reset-on (resets to 0.25 before d0, decays to 0)")],
        [0, 2, 5, 10, 15, 25, 29], (760, 660, 1160, 960), EV / "real-cut-lamps-strip-1x.png",
        cut_frame=CUT_F,
        note_fn=lambda key, d: f"gr={grain_post[key][d]:.4f}" + ("" if key == "main" else f" sh={lamps_share_post[key][d]:.3f}"),
    )

    # Also build a 16x amplified high-pass grain residual strip on lamps (d0, d2, d5, d10, d15, d25, d29)
    res_frames = {}
    bx0, by0, bx1, by1 = 760, 660, 1160, 960
    for k in ("main", "d1_off", "d1_on"):
        res_frames[k] = {}
        for d in [0, 2, 5, 10, 15, 25, 29]:
            ly = srgb_to_lin_y(lamps_frames[k][CUT_F + d][by0:by1, bx0:bx1])
            g_hp = ly - gaussian_filter(ly, 2)
            vis = np.clip(128.0 + (g_hp / main_floor_mean) * (255.0 * 4.0), 0, 255).astype(np.uint8)
            full_dummy = np.zeros((1080, 1920, 3), dtype=np.uint8)
            full_dummy[by0:by1, bx0:bx1] = np.stack([vis] * 3, axis=-1)
            res_frames[k][CUT_F + d] = full_dummy
    make_1x_strip(
        res_frames,
        [("main", "unpatched main: 16x high-pass grain residual (floor 760,660,1160,960)"),
         ("d1_off", "D1 0.25 reset-off: 16x high-pass grain residual"),
         ("d1_on", "D1 0.25 reset-on: 16x high-pass grain residual")],
        [0, 2, 5, 10, 15, 25, 29], (760, 660, 1160, 960), EV / "real-cut-lamps-residual-16x.png",
        cut_frame=CUT_F,
        note_fn=lambda key, d: f"hp={hp_rms_post[key][d]:.4f}" + ("" if key == "main" else f" sh={lamps_share_post[key][d]:.3f}"),
    )

    make_plots(nose_res, lamps_res, EV / "real-cut-plots.png")

    summary["forward_nose_real_cut"] = nose_res
    summary["reverse_lamps_real_cut"] = lamps_res
    summary["ffprobe"] = {
        "real-cut-nose-main-off-on.mp4": ffprobe_info(EV / "real-cut-nose-main-off-on.mp4"),
        "real-cut-lamps-main-off-on.mp4": ffprobe_info(EV / "real-cut-lamps-main-off-on.mp4"),
    }
    (ROOT / "real-cuts-summary.json").write_text(json.dumps(summary, indent=2))
    (EV / "real-cuts-summary.json").write_text(json.dumps(summary, indent=2))
    print("ANALYZE_REAL_CUTS_DONE")


if __name__ == "__main__":
    main()
