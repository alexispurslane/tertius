#!/usr/bin/env python3
"""Generate all Hold 1 tables, 1:1 crop sheets, share maps, settling plots, share strips, share films, and stretched tile-border checks."""
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, median_filter, maximum_filter

sys.path.insert(0, "/private/tmp/t1-wl7c/fe63-d1")
import share_analyze as sa

OUT = Path("/private/tmp/t1-wl7c/fe63-d1")
EV = Path("/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63-d1")
F = Path("/private/tmp/t1-wl7c/fe63")
G = Path("/private/tmp/t1-wl7c/guide")
S1_TABLES = Path("/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/docs/research/reflection-mixture/tables")
EV.mkdir(parents=True, exist_ok=True)

W = np.array([0.2126, 0.7152, 0.0722], dtype=np.float64)


def lin_y(p: Path) -> np.ndarray:
    a = np.asarray(Image.open(p).convert("RGB"), dtype=np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W


def make_sheet(box, out_png: Path, cols):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pad, hdr = 8, 34
    s = Image.new("RGB", (len(cols) * (w + pad) - pad, h + hdr), "white")
    dr = ImageDraw.Draw(s)
    for i, (label, path) in enumerate(cols):
        im = Image.open(path).convert("RGB").crop((x0, y0, x1, y1))
        s.paste(im, (i * (w + pad), hdr))
        for j, line in enumerate(label.split("|")):
            dr.text((i * (w + pad) + 4, 3 + 14 * j), line, fill="black")
    s.save(out_png)


def nose_metrics(d: Path, ceil: float):
    specks = []
    for k in range(20):
        g = np.asarray(Image.open(d / f"c-s{k:02d}.png").convert("L"), dtype=np.float64)[0:500, 480:1880]
        lamp = maximum_filter(g > 200, size=13)
        specks.append(int(((g - median_filter(g, size=7) > 12) & ~lamp).sum()))
    fields = [("x", "<f4"), ("pre", "<f4"), ("bin", "u1"), ("xs", "<f4"), ("ds", "<f4"),
              ("d14", "<f4", 4), ("d15", "<f4", 4), ("d22", "<f4", 4)]
    rec = np.fromfile(d / "census.keep0", dtype=np.dtype(fields))
    census = json.loads((d / "census.json").read_text())
    draws = census["DrawEnd"] - census["DrawFirst"]
    B = 3.8886351585388184
    x0, y0, x1, y1 = 750, 0, 1170, 330
    mean20 = float(np.mean([lin_y(d / f"c-s{k:02d}.png")[y0:y1, x0:x1].mean() for k in range(20)]))
    sh, _ = sa.load_shares(d / "d")
    st = sa.region_stats(sh, [x0, y0, x1, y1], ceil)
    return {
        "capture_specks_median": float(np.median(specks)),
        "capture_specks_max": max(specks),
        "capture_specks_c0": specks[0],
        "capture_specks_c1": specks[1],
        "capture_specks_c19": specks[19],
        "largest_diffuse_sample": float(rec["x"].max()),
        "largest_specular_sample": float(rec["xs"].max()),
        "specular_samples_above_B_per_draw": float((rec["xs"] > B).sum() / draws),
        "mean_specular_sample": float(rec["xs"].mean()),
        "box_mean_20_captures": mean20,
        "box_mean_vs_main": mean20 / 0.3012,
        "share": st,
    }


def make_static_share_maps(out_png: Path):
    """Render static settled share maps (scene thumb | 0.25 settled mean | 0.25 late std | 0.50 settled mean | 0.50 late std)."""
    tw, th = 320, 180
    pad, hdr, bar_w = 8, 28, 16
    rows_spec = [
        ("Bay (bay-sunbeam)", OUT / "bay-d1-025/c-s19.png", OUT / "bay-d1-025/d", OUT / "bay-d1-050/d"),
        ("Nose (sunlit-nose)", OUT / "nose-d1-025/c-s19.png", OUT / "nose-d1-025/d", OUT / "nose-d1-050/d"),
        ("Lamps (rect-lamps-f62)", OUT / "lamps-d1-025/c-s19.png", OUT / "lamps-d1-025/d", OUT / "lamps-d1-050/d"),
    ]
    ncols = 5
    W_tot = ncols * tw + (ncols - 1) * pad + 2 * pad + bar_w + 54
    H_tot = len(rows_spec) * (th + hdr + pad) + pad
    canvas = Image.new("RGB", (W_tot, H_tot), "white")
    dr = ImageDraw.Draw(canvas)

    for r, (name, scene_p, p025, p050) in enumerate(rows_spec):
        oy = pad + r * (th + hdr + pad)
        sh025, _ = sa.load_shares(p025)
        sh050, _ = sa.load_shares(p050)
        m025 = sh025[100:].mean(axis=0)
        s025 = sh025[100:].std(axis=0)
        m050 = sh050[100:].mean(axis=0)
        s050 = sh050[100:].std(axis=0)

        panels = [
            (f"{name}|scene c19", Image.open(scene_p).convert("RGB").resize((tw, th), Image.BILINEAR)),
            (f"D1 0.25 settled mean|mean={m025.mean():.3f}, max={m025.max():.3f}",
             Image.fromarray(sa.colormap(m025, 0.25)).resize((tw, th), Image.NEAREST)),
            (f"D1 0.25 late std (0..0.05)|med={np.median(s025):.4f}, p95={np.percentile(s025,95):.4f}",
             Image.fromarray(sa.colormap(s025, 0.05)).resize((tw, th), Image.NEAREST)),
            (f"D1 0.50 settled mean|mean={m050.mean():.3f}, max={m050.max():.3f}",
             Image.fromarray(sa.colormap(m050, 0.50)).resize((tw, th), Image.NEAREST)),
            (f"D1 0.50 late std (0..0.05)|med={np.median(s050):.4f}, p95={np.percentile(s050,95):.4f}",
             Image.fromarray(sa.colormap(s050, 0.05)).resize((tw, th), Image.NEAREST)),
        ]
        for c, (label, im) in enumerate(panels):
            ox = pad + c * (tw + pad)
            canvas.paste(im, (ox, oy + hdr))
            for j, line in enumerate(label.split("|")):
                dr.text((ox + 2, oy + 2 + 12 * j), line, fill="black")

    # Colorbar on the right
    bx = pad + ncols * (tw + pad) + 4
    by0, by1 = pad + hdr, H_tot - pad - 12
    bh = by1 - by0
    vals = np.linspace(1.0, 0.0, bh)[:, None]
    bar_rgb = sa.colormap(np.repeat(vals, bar_w, axis=1), 1.0)
    canvas.paste(Image.fromarray(bar_rgb), (bx, by0))
    dr.rectangle([bx, by0, bx + bar_w, by1], outline="black")
    for frac, lbl in [(0.0, "100%"), (0.25, "75%"), (0.5, "50%"), (0.75, "25%"), (1.0, "0%")]:
        yy = int(by0 + frac * (bh - 1))
        dr.text((bx + bar_w + 4, yy - 6), lbl, fill="black")
    dr.text((bx - 2, pad + 6), "scale", fill="black")
    canvas.save(out_png)


def make_settling_plots(out_png: Path):
    """Draw region-mean and representative single-tile settling curves over draws 0..659 (PIL, crisp grid)."""
    bay_boxes = json.loads((G / "daub/boxes.json").read_text())
    lamps_boxes = json.loads((F / "lamps-boxes.json").read_text())

    def region_series(sh, box):
        x0, y0, x1, y1 = box
        tx0, ty0 = int(round(x0 / 24.0)), int(round(y0 / 24.0))
        tx1, ty1 = int(round(x1 / 24.0)), int(round(y1 / 24.0))
        sub = sh[:, ty0:ty1, tx0:tx1].astype(np.float64)
        mid = sub[:, sub.shape[1] // 2, sub.shape[2] // 2]
        return sub.mean(axis=(1, 2)), mid

    pw, ph = 560, 260
    ml, mr, mt, mb = 54, 16, 32, 34
    pad = 16
    panels = []

    sh_b25, _ = sa.load_shares(OUT / "bay-d1-025/d")
    sh_b50, _ = sa.load_shares(OUT / "bay-d1-050/d")
    sh_n25, _ = sa.load_shares(OUT / "nose-d1-025/d")
    sh_n50, _ = sa.load_shares(OUT / "nose-d1-050/d")
    sh_l25, _ = sa.load_shares(OUT / "lamps-d1-025/d")
    sh_l50, _ = sa.load_shares(OUT / "lamps-d1-050/d")

    colors = {
        "right wall": (214, 39, 40),
        "floor": (31, 119, 180),
        "back wall": (44, 160, 44),
        "sunlit patch": (255, 127, 14),
        "nose box": (148, 103, 189),
        "lamps floor": (23, 190, 207),
    }

    for title, ceil, curves in [
        ("Frozen bay: region means, ceiling 0.25 (solid=mean, dashed=center tile)", 0.25, [
            ("right wall", colors["right wall"], *region_series(sh_b25, bay_boxes["right wall"])),
            ("floor", colors["floor"], *region_series(sh_b25, bay_boxes["floor"])),
            ("back wall", colors["back wall"], *region_series(sh_b25, bay_boxes["back wall below the slot"])),
            ("sunlit patch", colors["sunlit patch"], *region_series(sh_b25, bay_boxes["sunlit patch"])),
        ]),
        ("Frozen bay: region means, ceiling 0.50 (solid=mean, dashed=center tile)", 0.50, [
            ("right wall", colors["right wall"], *region_series(sh_b50, bay_boxes["right wall"])),
            ("floor", colors["floor"], *region_series(sh_b50, bay_boxes["floor"])),
            ("back wall", colors["back wall"], *region_series(sh_b50, bay_boxes["back wall below the slot"])),
            ("sunlit patch", colors["sunlit patch"], *region_series(sh_b50, bay_boxes["sunlit patch"])),
        ]),
        ("Sunlit nose box & rect-lamps floor, ceiling 0.25", 0.25, [
            ("nose box", colors["nose box"], *region_series(sh_n25, [750, 0, 1170, 330])),
            ("lamps floor", colors["lamps floor"], *region_series(sh_l25, lamps_boxes["floor"])),
        ]),
        ("Sunlit nose box & rect-lamps floor, ceiling 0.50", 0.50, [
            ("nose box", colors["nose box"], *region_series(sh_n50, [750, 0, 1170, 330])),
            ("lamps floor", colors["lamps floor"], *region_series(sh_l50, lamps_boxes["floor"])),
        ]),
    ]:
        im = Image.new("RGB", (pw, ph), "white")
        dr = ImageDraw.Draw(im)
        dr.text((8, 6), title, fill="black")
        x0, y0, x1, y1 = ml, mt, pw - mr, ph - mb
        dr.rectangle([x0, y0, x1, y1], outline=(180, 180, 180))
        ymax = ceil * 1.08
        n = len(curves[0][2])
        for frac in [0.0, 0.25, 0.5, 0.75, 1.0]:
            yv = frac * ceil
            yp = int(round(y1 - (yv / ymax) * (y1 - y0)))
            dr.line([(x0, yp), (x1, yp)], fill=(235, 235, 235))
            dr.text((4, yp - 6), f"{yv:.3f}", fill=(80, 80, 80))
        for d_tick in [0, 30, 60, 120, 240, 480, 659]:
            xp = int(round(x0 + (d_tick / (n - 1)) * (x1 - x0)))
            dr.line([(xp, y0), (xp, y1)], fill=(235, 235, 235))
            dr.text((xp - 8, y1 + 4), str(d_tick), fill=(80, 80, 80))
        dr.text((x0 + (x1 - x0) // 2 - 20, ph - 14), "draw index", fill=(60, 60, 60))

        # Plot dashed single-tile first, then solid region mean
        for idx_c, (lbl, col, mean_s, tile_s) in enumerate(curves):
            pts_t = [(int(round(x0 + (i / (n - 1)) * (x1 - x0))),
                      int(round(y1 - (tile_s[i] / ymax) * (y1 - y0)))) for i in range(0, n, 2)]
            light_col = tuple(min(255, int(c * 0.55 + 255 * 0.45)) for c in col)
            for k in range(0, len(pts_t) - 1, 2):
                dr.line([pts_t[k], pts_t[k + 1]], fill=light_col, width=1)
            pts_m = [(int(round(x0 + (i / (n - 1)) * (x1 - x0))),
                      int(round(y1 - (mean_s[i] / ymax) * (y1 - y0)))) for i in range(n)]
            dr.line(pts_m, fill=col, width=2)
            # Legend at right side
            lx = x1 - 165
            ly = y0 + 6 + idx_c * 14
            dr.line([(lx, ly + 5), (lx + 14, ly + 5)], fill=col, width=2)
            dr.text((lx + 18, ly), f"{lbl} ({mean_s[100:].mean():.3f})", fill="black")
        panels.append(im)

    canvas = Image.new("RGB", (2 * pw + 3 * pad, 2 * ph + 3 * pad), "white")
    for idx, im in enumerate(panels):
        r, c = divmod(idx, 2)
        canvas.paste(im, (pad + c * (pw + pad), pad + r * (ph + pad)))
    canvas.save(out_png)


def tile_boundary_seam_metrics():
    """Quantify whether 24-px tile boundaries (or 12-px tile centers) show excess gradient on the 20-capture mean or grain residual."""
    out = {}
    cases = [
        ("bay_border_main", G / "stills/bay-0.25", G / "daub/reference-mean.npy", (320, 40, 680, 400)),
        ("bay_border_d1_025", OUT / "bay-d1-025", OUT / "daub-bay-d1-025/reference-mean.npy", (320, 40, 680, 400)),
        ("bay_border_d1_050", OUT / "bay-d1-050", OUT / "daub-bay-d1-050/reference-mean.npy", (320, 40, 680, 400)),
        ("bay_floor_main", G / "stills/bay-0.25", G / "daub/reference-mean.npy", (600, 650, 1200, 950)),
        ("bay_floor_d1_025", OUT / "bay-d1-025", OUT / "daub-bay-d1-025/reference-mean.npy", (600, 650, 1200, 950)),
        ("bay_floor_d1_050", OUT / "bay-d1-050", OUT / "daub-bay-d1-050/reference-mean.npy", (600, 650, 1200, 950)),
        ("nose_border_main", F / "nose-main", None, (560, 0, 920, 360)),
        ("nose_border_d1_025", OUT / "nose-d1-025", None, (560, 0, 920, 360)),
        ("nose_border_d1_050", OUT / "nose-d1-050", None, (560, 0, 920, 360)),
    ]
    for name, d, ref_p, (x0, y0, x1, y1) in cases:
        c19 = lin_y(d / "c-s19.png")[y0:y1, x0:x1]
        if ref_p is not None:
            ref = (np.load(ref_p).astype(np.float64) @ W)[y0:y1, x0:x1]
            e = c19 - ref
            grain = e - gaussian_filter(e, 2)
        else:
            grain = c19 - gaussian_filter(c19, 2)
        # Column-wise horizontal difference |grain[:, j] - grain[:, j-1]| grouped by global x mod 24
        dx = np.abs(np.diff(grain, axis=1))
        xs = np.arange(x0 + 1, x1) % 24
        by_phase_x = [float(dx[:, xs == p].mean()) for p in range(24)]
        dy = np.abs(np.diff(grain, axis=0))
        ys = np.arange(y0 + 1, y1) % 24
        by_phase_y = [float(dy[ys == p, :].mean()) for p in range(24)]
        # Also test 24-px periodic peak in 1D FFT of column-mean high-pass residual
        col_prof = grain.mean(axis=0)
        row_prof = grain.mean(axis=1)
        out[name] = {
            "grain_dx_at_tile_edge_ratio": by_phase_x[0] / float(np.mean(by_phase_x)),
            "grain_dx_max_phase": int(np.argmax(by_phase_x)),
            "grain_dx_max_over_mean": float(max(by_phase_x) / np.mean(by_phase_x)),
            "grain_dy_at_tile_edge_ratio": by_phase_y[0] / float(np.mean(by_phase_y)),
            "grain_dy_max_phase": int(np.argmax(by_phase_y)),
            "grain_dy_max_over_mean": float(max(by_phase_y) / np.mean(by_phase_y)),
            "col_profile_std": float(col_prof.std()),
            "row_profile_std": float(row_prof.std()),
        }
    return out


def make_border_check(out_png: Path):
    """Stretched tile-border check across 0 <-> 0.25 and 0 <-> 0.50 borders in bay and nose."""
    rows = [
        ("Bay main (ref crop)", G / "stills/bay-0.25/c-s19.png", G / "daub/reference-mean.npy",
         None, 0.25, (320, 40, 680, 400), 12.0),
        ("Bay D1 0.25 (0<->0.25)", OUT / "bay-d1-025/c-s19.png", OUT / "daub-bay-d1-025/reference-mean.npy",
         OUT / "bay-d1-025/d", 0.25, (320, 40, 680, 400), 12.0),
        ("Bay D1 0.50 (0<->0.50)", OUT / "bay-d1-050/c-s19.png", OUT / "daub-bay-d1-050/reference-mean.npy",
         OUT / "bay-d1-050/d", 0.50, (320, 40, 680, 400), 12.0),
        ("Nose main (ref crop)", F / "nose-main/c-s19.png", None,
         None, 0.25, (560, 0, 920, 360), 12.0),
        ("Nose D1 0.25 (0<->0.25)", OUT / "nose-d1-025/c-s19.png", None,
         OUT / "nose-d1-025/d", 0.25, (560, 0, 920, 360), 12.0),
        ("Nose D1 0.50 (0<->0.50)", OUT / "nose-d1-050/c-s19.png", None,
         OUT / "nose-d1-050/d", 0.50, (560, 0, 920, 360), 12.0),
    ]
    cw, ch = 360, 360
    pad, hdr = 8, 30
    canvas = Image.new("RGB", (3 * (cw + pad) + pad, len(rows) * (ch + hdr + pad) + pad), "white")
    dr = ImageDraw.Draw(canvas)
    for r, (title, png_p, ref_p, share_p, ceil, box, gain) in enumerate(rows):
        x0, y0, x1, y1 = box
        im = Image.open(png_p).convert("RGB").crop(box)
        arr = np.asarray(im, dtype=np.float64)
        lo, hi = np.percentile(arr, 1), np.percentile(arr, 99)
        stretched = np.clip((arr - lo) / max(hi - lo, 1.0) * 255.0, 0, 255).astype(np.uint8)
        y_img = lin_y(png_p)
        if ref_p is not None:
            ref = np.load(ref_p).astype(np.float64) @ W
            e = (y_img - ref)[y0:y1, x0:x1]
            m = max(float(ref[y0:y1, x0:x1].mean()), 1e-4)
        else:
            e = y_img[y0:y1, x0:x1]
            m = max(float(e.mean()), 1e-4)
        g = e - gaussian_filter(e, 2)
        vis = np.clip(128.0 + (g / m) * (255.0 * gain * 0.25), 0, 255).astype(np.uint8)
        if share_p is not None:
            sh, _ = sa.load_shares(share_p)
            rgb = sa.colormap(sh[-1], ceil)
            sh_im = Image.fromarray(rgb).resize((1920, 1080), Image.BILINEAR).crop(box)
            sh_lbl = f"bilinear share (0..{ceil:.2f})"
        else:
            sh_im = Image.new("RGB", (cw, ch), (240, 240, 240))
            ImageDraw.Draw(sh_im).text((120, 170), "main (share = 0)", fill="black")
            sh_lbl = "main (no share map)"

        row_items = [
            (f"{title}|contrast-stretched 1:1", Image.fromarray(stretched)),
            (f"{title}|high-pass grain ({gain:.0f}x)", Image.fromarray(np.stack([vis] * 3, axis=-1))),
            (f"{title}|{sh_lbl}", sh_im),
        ]
        oy = pad + r * (ch + hdr + pad)
        for c, (label, p) in enumerate(row_items):
            ox = pad + c * (cw + pad)
            canvas.paste(p, (ox, oy + hdr))
            for j, line in enumerate(label.split("|")):
                dr.text((ox + 2, oy + 2 + 12 * j), line, fill="black")
    canvas.save(out_png)


def make_share_evolution_film(case: str, out_mp4: Path, n_frames: int = 240, scale: int = 6):
    """Encode side-by-side 60 fps H.264 CRF 12 film of the 80x45 share image settling over draws 0..n_frames-1 (0.25 vs 0.50)."""
    sh25, _ = sa.load_shares(OUT / f"{case}-d1-025/d")
    sh50, _ = sa.load_shares(OUT / f"{case}-d1-050/d")
    n = min(n_frames, sh25.shape[0], sh50.shape[0])
    tw, th = sh25.shape[2] * scale, sh25.shape[1] * scale
    pad, hdr = 8, 24
    W_tot = 2 * tw + pad
    W_tot += W_tot % 2
    H_tot = th + hdr
    H_tot += H_tot % 2
    with tempfile.TemporaryDirectory() as tmp:
        for i in range(n):
            frame = Image.new("RGB", (W_tot, H_tot), "white")
            dr = ImageDraw.Draw(frame)
            im25 = Image.fromarray(sa.colormap(sh25[i], 0.25)).resize((tw, th), Image.NEAREST)
            im50 = Image.fromarray(sa.colormap(sh50[i], 0.50)).resize((tw, th), Image.NEAREST)
            frame.paste(im25, (0, hdr))
            frame.paste(im50, (tw + pad, hdr))
            dr.text((4, 5), f"{case} D1 0.25  draw {i:03d}  mean={sh25[i].mean():.3f}", fill="black")
            dr.text((tw + pad + 4, 5), f"{case} D1 0.50  draw {i:03d}  mean={sh50[i].mean():.3f}", fill="black")
            frame.save(f"{tmp}/f{i:05d}.png")
        subprocess.run([
            "ffmpeg", "-y", "-loglevel", "error", "-framerate", "60",
            "-i", f"{tmp}/f%05d.png", "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", str(out_mp4)
        ], check=True)


def main():
    summary = {}

    # 1. Plate bit-identity
    b_main = (F / "plate-main/c-s19.png").read_bytes()
    summary["plate"] = {}
    for tag in ("025", "050"):
        b = (OUT / f"plate-d1-{tag}/c-s19.png").read_bytes()
        summary["plate"][tag] = {"bit_identical": b == b_main, "bytes": len(b), "sha256": hashlib.sha256(b).hexdigest()}

    # 2. Nose
    summary["nose"] = {
        "d1_025": nose_metrics(OUT / "nose-d1-025", 0.25),
        "d1_050": nose_metrics(OUT / "nose-d1-050", 0.5),
    }

    # 3. Bay parts + means + shares
    bay_boxes = json.loads((G / "daub/boxes.json").read_text())
    main_bay_means = json.loads((S1_TABLES / "means-bay-full.json").read_text())["bay-0.25"]
    summary["bay_parts"] = json.loads((OUT / "parts-bay-d1.json").read_text())
    summary["bay_means_vs_main"] = {}
    summary["bay_shares"] = {}
    for tag, ceil in [("025", 0.25), ("050", 0.5)]:
        bd = OUT / f"bay-d1-{tag}"
        summary["bay_means_vs_main"][f"d1_{tag}"] = {
            name: float(np.mean([lin_y(bd / f"c-s{k:02d}.png")[y0:y1, x0:x1].mean() for k in range(20)]))
            / main_bay_means[name][2]
            for name, (x0, y0, x1, y1) in bay_boxes.items()
        }
        sh, _ = sa.load_shares(bd / "d")
        summary["bay_shares"][f"d1_{tag}"] = {
            "whole_frame": sa.region_stats(sh, [0, 0, 1920, 1080], ceil),
            "regions": {k: sa.region_stats(sh, v, ceil) for k, v in bay_boxes.items()},
        }

    # 4. Lamps parts + means + shares
    lamps_boxes = json.loads((F / "lamps-boxes.json").read_text())
    main_lamps_mean = json.loads((S1_TABLES / "means-lamps.json").read_text())["lamps-main"]["floor"][2]
    summary["lamps_parts"] = json.loads((OUT / "parts-lamps-d1.json").read_text())
    summary["lamps_means_vs_main"] = {}
    summary["lamps_shares"] = {}
    for tag, ceil in [("025", 0.25), ("050", 0.5)]:
        ld = OUT / f"lamps-d1-{tag}"
        x0, y0, x1, y1 = lamps_boxes["floor"]
        summary["lamps_means_vs_main"][f"d1_{tag}"] = {
            "floor": float(np.mean([lin_y(ld / f"c-s{k:02d}.png")[y0:y1, x0:x1].mean() for k in range(20)]))
            / main_lamps_mean
        }
        sh, _ = sa.load_shares(ld / "d")
        summary["lamps_shares"][f"d1_{tag}"] = {
            "whole_frame": sa.region_stats(sh, [0, 0, 1920, 1080], ceil),
            "floor": sa.region_stats(sh, lamps_boxes["floor"], ceil),
        }

    # 5. Fasttrace clamp check (window 4 vs window 2)
    summary["fasttrace"] = {
        "stage1": json.loads((S1_TABLES / "fasttrace.json").read_text()),
        "d1_025": json.loads((OUT / "fasttrace-d1-025.json").read_text()),
        "d1_050": json.loads((OUT / "fasttrace-d1-050.json").read_text()),
    }

    # 6. Tile-boundary seam metrics
    summary["tile_boundary_seams"] = tile_boundary_seam_metrics()

    (OUT / "hold1-summary.json").write_text(json.dumps(summary, indent=2))
    (EV / "hold1-summary.json").write_text(json.dumps(summary, indent=2))

    # 7. 1:1 crop sheets (all 5 surfaces + bay back wall & floor)
    make_sheet((750, 0, 1170, 330), EV / "nose-box-1x-d1.png", [
        ("main|-1.7507 stops, c19", F / "nose-main/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp", F / "nose-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive tile share", OUT / "nose-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive tile share", OUT / "nose-d1-050/c-s19.png"),
    ])
    make_sheet((1300, 100, 1800, 500), EV / "bay-d1-right-wall.png", [
        ("main|-4.089 stops, c19", G / "stills/bay-0.25/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp", F / "bay-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive tile share", OUT / "bay-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive tile share", OUT / "bay-d1-050/c-s19.png"),
    ])
    make_sheet((100, 40, 420, 360), EV / "bay-d1-sunlit-patch.png", [
        ("main|-4.089 stops, c19", G / "stills/bay-0.25/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp (+30% grain)", F / "bay-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive (settles to 0)", OUT / "bay-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive (settles to 0)", OUT / "bay-d1-050/c-s19.png"),
    ])
    make_sheet((560, 420, 980, 560), EV / "bay-d1-back-wall.png", [
        ("main|-4.089 stops, c19", G / "stills/bay-0.25/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp", F / "bay-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive (share ~0.034)", OUT / "bay-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive (share ~0.035)", OUT / "bay-d1-050/c-s19.png"),
    ])
    make_sheet((600, 650, 1200, 950), EV / "bay-d1-floor.png", [
        ("main|-4.089 stops, c19", G / "stills/bay-0.25/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp", F / "bay-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive (share ~0.132)", OUT / "bay-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive (share ~0.176)", OUT / "bay-d1-050/c-s19.png"),
    ])
    make_sheet((600, 620, 1320, 1000), EV / "lamps-floor-d1.png", [
        ("main|3.83 stops, c19", F / "lamps-main/c-s19.png"),
        ("stage1 exact 0.25|fixed ramp (+25% grain)", F / "lamps-exact/c-s19.png"),
        ("Design 1 (ceil 0.25)|adaptive (settles to 0)", OUT / "lamps-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|adaptive (settles to 0)", OUT / "lamps-d1-050/c-s19.png"),
    ])
    make_sheet((800, 350, 1150, 700), EV / "plate-d1.png", [
        ("main|0.73 stops, c19", F / "plate-main/c-s19.png"),
        ("Design 1 (ceil 0.25)|byte-identical", OUT / "plate-d1-025/c-s19.png"),
        ("Design 1 (ceil 0.50)|byte-identical", OUT / "plate-d1-050/c-s19.png"),
    ])

    # 8. Static settled share maps + settling plots + share strips
    make_static_share_maps(EV / "share-maps-d1.png")
    make_settling_plots(EV / "share-settling-plots.png")
    for case, tag, ceil in [
        ("bay", "025", 0.25), ("bay", "050", 0.5),
        ("nose", "025", 0.25), ("nose", "050", 0.5),
        ("lamps", "025", 0.25), ("lamps", "050", 0.5),
    ]:
        sh, _ = sa.load_shares(OUT / f"{case}-d1-{tag}/d")
        sa.save_strip(sh, ceil, [0, 5, 15, 30, 60, 120, 659], EV / f"share-strip-{case}-{tag}.png", scale=4)

    # 9. Stretched tile-border check (bay + nose, main + 0.25 + 0.50)
    make_border_check(EV / "tile-border-check.png")

    # 10. Share evolution films over time (frozen bay and frozen hall/nose)
    make_share_evolution_film("bay", EV / "share-film-bay-025-vs-050.mp4", n_frames=240, scale=6)
    make_share_evolution_film("nose", EV / "share-film-nose-025-vs-050.mp4", n_frames=240, scale=6)

    print("HOLD1_ANALYSIS_DONE")


if __name__ == "__main__":
    main()
