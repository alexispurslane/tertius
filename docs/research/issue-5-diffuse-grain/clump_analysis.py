"""Matched spatial RGB bands with edges excluded by the zero-specular control."""
import argparse
import json
from pathlib import Path
import numpy as np
from scipy.ndimage import gaussian_filter, distance_transform_edt
from analyze import linear_rgb
from multiscale import fluctuations


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--variants',nargs='+',required=True)
    args=parser.parse_args()
    crops=json.loads((Path(__file__).parent/'irregular-crops.json').read_text())
    crops={key:crops[key] for key in ['sloped-panel','panel-warm','panel-cool']}
    result=dict(frames=list(range(16,48)),
        method='Display-linear RGB DoG sigma s minus sigma 2s; same mask for every variant at each scale. '
            'Exclude zero-specular time-mean luminance gradient > 0.001 per output pixel and all pixels within 6s of it. '
            'Persistent = temporal mean of band. Temporal = band minus its temporal mean. No motion registration.',
        crops={})
    for crop,box in crops.items():
        left,top,right,bottom=box
        expanded=(left-128,top-128,right+128,bottom+128)
        control=np.stack([linear_rgb(args.captures/'zero-specular/frames'/f'cockpit-{i:06}.png',expanded) for i in range(16,48)])
        wash=gaussian_filter(control.mean(axis=0)@np.array([0.2126,0.7152,0.0722]),1)
        gy,gx=np.gradient(wash)
        safe=distance_transform_edt(np.hypot(gx,gy)<=0.001)
        interior=np.zeros(safe.shape,bool)
        interior[128:128+bottom-top,128:128+right-left]=True
        masks={s:interior & (safe>6*s) for s in [1,2,4,8,16]}
        result['crops'][crop]=dict(box=box,pixels_at_scale={str(s):int(m.sum()) for s,m in masks.items()},variants={})
        for variant in args.variants:
            frames=np.stack([linear_rgb(args.captures/variant/'frames'/f'cockpit-{i:06}.png',expanded) for i in range(16,48)])
            bands={}
            for s,mask in masks.items():
                bands[str(s)]=fluctuations(frames,mask,[s])[str(s)] if mask.sum()>=50 else None
            result['crops'][crop]['variants'][variant]=bands
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n')


if __name__=='__main__':
    main()
