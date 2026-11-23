# Tool code of ter-8pco's lamp-hall part (for t7-3e22): reads the images that the rays read, which RayCensus dumps at the
# last counted draw with TERTIUS_PROBE_CENSUS_DUMP=1, around one near lamp, and tests three forms of a bright border:
#   - a 1 px row or column at the lamp's edge far brighter than the lamp;
#   - a lamp edge on a texel boundary of a level at one render size and inside a texel at the other;
#   - a level whose value at the lamp's border exceeds the lamp's own value.
# The images, as linear values before tone mapping, row by row from the top, each at its allocated size (the valid part
# is the render size, halved and rounded up per level):
#   - hit-diffuse: layer 1 of the hit light, the light that leaves each pixel toward a diffuse ray. A diffuse ray's hit
#     reads it (screen_trace_plan.comp writes it from this draw's direct light and the last draw's filtered indirect
#     light). Luminance of its rgb.
#   - hit-reflection: layer 0, the same for a reflection ray.
#   - pyramid-0, -1, -2: levels 0 to 2 of the light pyramid. The guided quarter of the diffuse rays aims by it. No ray
#     reads its value as the light of a hit.
# Level k of the light pyramid holds the sum of the 4^k pixels under each cell (light_pyramid.glsl); this tool divides it
# by 4^k, so that every level reads as a mean per render pixel, on the scale of level 0.
# The lamp: its entity holds a light, so the glow that it sends is 0 (glowSent in frame.glsl), and in the images that
# the rays read, the lamp is a dark rectangle in the lit ceiling. The lamp is the largest 4-connected set of render
# pixels in the window whose hit-diffuse luminance is below a tenth of the window's median. "The ceiling" is the median of
# the pixels 3 to 8 px outside the lamp. At level k (cell = 2^k render px), a cell is inside when all of its render
# pixels are lamp pixels, on the border when some are, and in the ring when none are and it touches a lamp cell.
# Usage: python3 lampdump.py <take dir> <window "x0,y0,x1,y1" in output px> <out prefix>
#        python3 lampdump.py --crops <take A> <take B> <window> <out png>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import binary_erosion, binary_dilation, label

LUM = np.array([0.2126, 0.7152, 0.0722])


def load(take):
    take = Path(take)
    meta = json.loads((take / 'census.json').read_text())
    sizes = json.loads((take / 'census.dump.json').read_text())
    w, h = meta['Viewport'] if 'Viewport' in meta and len(meta['Viewport']) == 2 else (meta['Width'], meta['Height'])
    images = {}
    for name, (iw, ih, ch) in sizes.items():
        a = np.fromfile(take / f'census.{name}.f32', '<f4').reshape(ih, iw, ch)
        a = a[..., :3] @ LUM if ch == 4 else a[..., 0]
        k = int(name[-1]) if name.startswith('pyramid') else 0
        vw, vh = w, h
        for _ in range(k):
            vw, vh = (vw + 1) // 2, (vh + 1) // 2
        images[name] = a[:vh, :vw]
    return images, w, h


def lamp_mask(lum, win):
    x0, y0, x1, y1 = win
    sub = lum[y0:y1, x0:x1]
    dark = sub < 0.1 * np.median(sub)
    lab, n = label(dark)
    sizes = np.bincount(lab.ravel())
    sizes[0] = 0
    full = np.zeros(lum.shape, bool)
    full[y0:y1, x0:x1] = lab == int(np.argmax(sizes))
    return full


def level_cells(mask, k, shape):
    """Per cell of level k: the share of its render pixels that are lamp pixels."""
    s = 2 ** k
    hh, ww = shape
    pad = np.zeros((hh * s, ww * s), float)
    pad[:mask.shape[0], :mask.shape[1]] = mask
    return pad.reshape(hh, s, ww, s).mean(axis=(1, 3))


def per_pixel(name, img):
    k = int(name[-1]) if name.startswith('pyramid') else 0
    return k, img / 4 ** k


def analyze(take, out_win):
    images, w, h = load(take)
    scale = w / 1920
    win = [int(out_win[0] * scale), int(out_win[1] * scale), int(out_win[2] * scale), int(out_win[3] * scale)]
    lamp = lamp_mask(images['hit-diffuse'], win)
    ys, xs = np.nonzero(lamp)
    box = [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]
    around = binary_dilation(lamp, iterations=8) & ~binary_dilation(lamp, iterations=2)
    result = {'render': [w, h], 'lamp_box_render_px': box, 'lamp_pixels': int(lamp.sum()),
              'lamp_fills_box': bool(lamp.sum() == (box[2] - box[0]) * (box[3] - box[1])),
              'lamp_box_in_output_px': [round(v / scale, 2) for v in box]}
    for name, raw in images.items():
        k, img = per_pixel(name, raw)
        ceiling = float(np.median(images[name][around])) / 4 ** k if k == 0 else None
        share = level_cells(lamp, k, img.shape)
        inside, border = share == 1, (share > 0) & (share < 1)
        ring = binary_dilation(share > 0) & (share == 0)
        far = binary_dilation(share > 0, iterations=max(2, 8 >> k)) & ~binary_dilation(share > 0, iterations=1)
        ceiling = float(np.median(img[far]))
        r = {'ceiling': ceiling,
             'lamp_own': float(np.median(img[inside])) if inside.any() else None,
             'inside_max': float(img[inside].max()) if inside.any() else None,
             'border_cells': int(border.sum()),
             'border_max': float(img[border].max()) if border.any() else None,
             'ring_max': float(img[ring].max()), 'ring_median': float(np.median(img[ring]))}
        if k == 0:
            b = box
            edges = {'top': img[b[1] - 1, b[0]:b[2]], 'bottom': img[b[3], b[0]:b[2]],
                     'left': img[b[1]:b[3], b[0] - 1], 'right': img[b[1]:b[3], b[2]],
                     'top_2': img[b[1] - 2, b[0]:b[2]], 'bottom_2': img[b[3] + 1, b[0]:b[2]],
                     'left_2': img[b[1]:b[3], b[0] - 2], 'right_2': img[b[1]:b[3], b[2] + 1]}
            r['outside_edge_line_mean_over_ceiling'] = {e: round(float(v.mean() / ceiling), 4) for e, v in edges.items()}
            r['outside_edge_line_max_over_ceiling'] = {e: round(float(v.max() / ceiling), 4) for e, v in edges.items()}
        else:
            s_ = 2 ** k
            r['lamp_edges_on_cell_boundary'] = {'left': box[0] % s_ == 0, 'top': box[1] % s_ == 0,
                                                'right': box[2] % s_ == 0, 'bottom': box[3] % s_ == 0}
        r['ring_max_over_ceiling'] = r['ring_max'] / ceiling if ceiling > 0 else None
        r['border_max_over_ceiling'] = (r['border_max'] / ceiling if ceiling > 0 and r['border_max'] is not None
                                        else None)
        result[name] = r
    return result, images, box


def crops(take_a, take_b, out_win, out_png):
    """Each image around the lamp of both takes at nearest sampling: rows = hit-diffuse, hit-reflection, pyramid levels 0,
    1 and 2; columns = takes. Every crop spans the lamp's box plus 8 render px on each side; level k is magnified 2^k
    times more than level 0, so that all crops of one take show the same piece of the screen. Gray = log2 of the value
    per render pixel over that image's ceiling value, from -4 (black) to +2 (white), the same mapping in every crop: the
    ceiling is mid gray (171), a border twice as bright as the ceiling is 213, and 4 times as bright is white."""
    panels = []
    for take in (take_a, take_b):
        res, images, box = analyze(take, out_win)
        col = []
        for name in ('hit-diffuse', 'hit-reflection', 'pyramid-0', 'pyramid-1', 'pyramid-2'):
            k, img = per_pixel(name, images[name])
            s_ = 2 ** k
            ceiling = res[name]['ceiling'] if res[name]['ceiling'] > 0 else 1e-3
            x0, y0, x1, y1 = (box[0] - 8) // s_, (box[1] - 8) // s_, -(-(box[2] + 8) // s_), -(-(box[3] + 8) // s_)
            c = img[y0:y1, x0:x1]
            g = np.clip((np.log2(np.maximum(c, 1e-6) / ceiling) + 4) / 6, 0, 1)
            col.append((np.kron(g, np.ones((6 * s_, 6 * s_))) * 255).astype(np.uint8))
        panels.append(col)
    rows = []
    for a, b in zip(*panels):
        hh = max(a.shape[0], b.shape[0])
        pad = lambda x: np.pad(x, ((0, hh - x.shape[0]), (0, 0)), constant_values=40)
        rows.append(np.concatenate([pad(a), np.full((hh, 8), 255, np.uint8), pad(b)], axis=1))
    ww = max(r.shape[1] for r in rows)
    rows = [np.pad(r, ((0, 8), (0, ww - r.shape[1])), constant_values=255) for r in rows]
    Image.fromarray(np.concatenate(rows, axis=0)).save(out_png)


if __name__ == '__main__':
    if sys.argv[1] == '--crops':
        crops(sys.argv[2], sys.argv[3], [int(v) for v in sys.argv[4].split(',')], sys.argv[5])
    else:
        res, _, _ = analyze(sys.argv[1], [int(v) for v in sys.argv[2].split(',')])
        Path(sys.argv[3]).write_text(json.dumps(res, indent=1))
        print(json.dumps(res, indent=1))
