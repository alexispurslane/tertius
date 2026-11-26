"""Reduce matched PNG sequences. Crop boxes are in output pixels, left/top/right/bottom."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter
from measure import grain, compare, color_wash


def linear_rgb(path, box):
    with Image.open(path) as image:
        rgb = np.asarray(image.convert('RGB').crop(box), dtype=np.float32) / 255
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    return linear


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--frames', type=int, default=120)
    parser.add_argument('--variants', nargs='+', default=['normal', 'zero-diffuse', 'closed-diffuse', 'zero-specular'])
    parser.add_argument('--crops', type=Path, required=True)
    args = parser.parse_args()
    crops = json.loads(args.crops.read_text())
    variants = [name for name in args.variants
                if (args.captures / name / 'frames').exists()]
    args.output.mkdir(parents=True, exist_ok=True)
    indices = [int(args.frames * share) for share in (0.2, 0.5, 0.8)]
    results = dict(crops=crops, skipped_first_frames=16, variants={})
    baselines = {}
    baseline_rgb = {}
    for variant in variants:
        paths = sorted((args.captures / variant / 'frames').glob('cockpit-*.png'))
        assert len(paths) >= args.frames, (variant, len(paths))
        paths = paths[:args.frames]
        exposure = json.loads((paths[0].parent / 'exposure.json').read_text())
        assert all(abs(row['stops'] - 6.0043726) < 1e-5 for row in exposure)
        results['variants'][variant] = dict(frame_count=len(paths), crops={})
        for name, box in crops.items():
            rgb = np.stack([linear_rgb(path, box) for path in paths[16:]])
            frames = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
            result = grain(frames)
            if variant == 'normal':
                baselines[name] = frames
                baseline_rgb[name] = rgb
            else:
                result.update(compare(baselines[name], frames))
            result['color_wash'] = color_wash(baseline_rgb[name], rgb)
            wash = gaussian_filter(rgb.mean(axis=0), sigma=(8, 8, 0), mode='reflect')
            encoded = np.where(wash <= 0.0031308, wash * 12.92, 1.055 * wash ** (1 / 2.4) - 0.055)
            Image.fromarray(np.uint8(np.clip(encoded * 255, 0, 255))).resize(
                (rgb.shape[2] * 2, rgb.shape[1] * 2), Image.Resampling.NEAREST).save(
                args.output / f'{variant}-{name}-wash.png')
            results['variants'][variant]['crops'][name] = result
        # Preserve the whole frame at three matched times.
        for index in indices:
            with Image.open(paths[index]) as image:
                image.save(args.output / f'{variant}-{index:03}.png')
    for name, box in crops.items():
        width, height = box[2] - box[0], box[3] - box[1]
        scale = 2
        sheet = Image.new('RGB', (width * scale * len(variants), height * scale * 3 + 90), '#17191c')
        draw = ImageDraw.Draw(sheet)
        for column, variant in enumerate(variants):
            draw.text((column * width * scale + 8, 8), variant, fill='white')
            for row, index in enumerate(indices):
                with Image.open(args.output / f'{variant}-{index:03}.png') as image:
                    crop = image.crop(box).resize((width * scale, height * scale), Image.Resampling.NEAREST)
                    sheet.paste(crop, (column * width * scale, 30 + row * (height * scale + 20)))
        sheet.save(args.output / f'{name}-matched.png')
    (args.output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
