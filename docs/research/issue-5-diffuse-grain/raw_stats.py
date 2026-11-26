"""Describe the raw, demodulated, pre-exposed specular samples in fixed panel crops."""
import argparse
import json
from pathlib import Path
import numpy as np

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('raw', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--source', action='store_true')
    parser.add_argument('--threshold', type=float)
    parser.add_argument('--crops', type=Path, required=True)
    args = parser.parse_args()
    width, height = map(int, (args.raw / 'size.txt').read_text().split())
    crops = json.loads(args.crops.read_text())
    paths = sorted(args.raw.glob('*.sample.f32'))[16:]
    assert len(paths) == 32, len(paths)
    args.output.mkdir(parents=True, exist_ok=True)
    result = dict(render_width=width, render_height=height, frames=32,
        quantity=('Diffuse-ray-sourced specular RGB before limiting and reconstruction' if args.source else 'Mixed specular sample RGB after resolve, before reconstruction') + '; demodulated, pre-exposed by 2^6.0043726.',
        crops={})
    saved = {}
    for name, (left, top, right, bottom) in crops.items():
        box = (int(np.ceil(left * width / 1280)), int(np.ceil(top * height / 720)),
               int(np.floor(right * width / 1280)), int(np.floor(bottom * height / 720)))
        x0, y0, x1, y1 = box
        values = []; materials = []; shares = []
        for path in paths:
            rgba = np.fromfile(path, dtype='<f4').reshape(height, width, 4)[y0:y1, x0:x1].copy()
            sample = rgba[..., :3]
            shares.append(rgba[..., 3])
            material = np.fromfile(path.with_name(path.name.replace('.sample.f32', '.material.u8')),
                dtype=np.uint8).reshape(height, width, 4)[y0:y1, x0:x1].copy()
            values.append(sample); materials.append(material)
        rgb = np.stack(values)
        material = np.stack(materials)
        y = (rgb @ LUMA).ravel()
        assert np.isfinite(y).all()
        roughness = material[..., 1].astype(np.float32) / 255
        handover = np.clip((np.maximum(roughness, 0.045) - 0.4) / 0.1, 0, 1)
        if args.source:
            handover = np.stack(shares)
        threshold = args.threshold if args.threshold is not None else float(np.quantile(y, 0.99))
        tail = y > threshold
        result['crops'][name] = dict(
            render_pixel_box=box, sample_count=y.size,
            roughness_quantiles=dict(zip(['min','median','max'], np.quantile(roughness, [0,.5,1]).tolist())),
            handover_quantiles=dict(zip(['min','median','max'], np.quantile(handover, [0,.5,1]).tolist())),
            full_diffuse_ray_share=float(np.mean(handover == 1)),
            mean_rgb=rgb.mean(axis=(0,1,2)).tolist(),
            luminance_quantiles=dict(zip(['min','p25','p50','p75','p90','p95','p99','p995','p999','max'],
                np.quantile(y,[0,.25,.5,.75,.9,.95,.99,.995,.999,1]).tolist())),
            mean_luminance=float(y.mean()),
            proposed_limit=threshold,
            baseline_affected_fraction=float(tail.mean()),
            tail_energy_fraction=float(y[tail].sum()/y.sum()),
            predicted_raw_luminance_loss_fraction=float(np.maximum(y-threshold,0).sum()/y.sum()),
        )
        saved[name + '_rgb'] = rgb
        saved[name + '_material'] = material
        if args.source:
            saved[name + '_handover'] = handover
    np.savez_compressed(args.output / 'panel-raw-samples.npz', **saved)
    (args.output / 'distribution.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))


if __name__ == '__main__':
    main()
