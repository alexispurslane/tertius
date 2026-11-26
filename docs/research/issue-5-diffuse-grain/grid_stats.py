"""Reuse the cut-grid production check on matched cockpit panel crops."""
import argparse
import importlib.util
import json
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / 'docs/research/cut-grid-probe/tools/roi.py'
spec = importlib.util.spec_from_file_location('cut_grid_roi', SOURCE)
roi = importlib.util.module_from_spec(spec)
spec.loader.exec_module(roi)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--variants',nargs='+',default=['normal','spread-step2','spread-step6','irregular'])
    args = parser.parse_args()
    result = dict(method='Existing roi.py luma/corr/ac functions; Gaussian sigma3 output pixels.',
        crop=[880,40,1080,155],frames=list(range(16,48)),lags_output_pixels=[3,6,8,9,10,12,18,24],variants={})
    for variant in args.variants:
        rows = []
        for index in range(16,48):
            path = args.captures / variant / 'frames' / f'cockpit-{index:06}.png'
            with Image.open(path) as image:
                rgb = np.asarray(image.convert('RGB').crop(tuple(result['crop'])),dtype=np.float64)
            luma = roi.luma(rgb)
            hp = luma - gaussian_filter(luma,3)
            rows.append({str(lag):roi.ac(hp,lag) for lag in result['lags_output_pixels']})
        medians = {str(lag):float(np.median([row[str(lag)] for row in rows])) for lag in result['lags_output_pixels']}
        result['variants'][variant] = dict(median_autocorrelation=medians,
            lag9_peak_above_adjacent=float(medians['9']-(medians['8']+medians['10'])/2),per_frame=rows)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({key:value['median_autocorrelation'] for key,value in result['variants'].items()},indent=2))


if __name__ == '__main__':
    main()
