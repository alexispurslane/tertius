"""Whole saved visible panel crop, with no imposed spatial distance cutoff."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from heat_reference import heat
from multiscale import fluctuations
from measure import color_wash


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    archive=np.load(args.archive);source=archive['source']
    result=dict(scope='OFFLINE outgoing source, 25 distinct draw phases at one fixed cockpit pose. '
        'Full 74x130 visible crop, rectangular grid, uniform screen-cell area, insulating crop boundary. '
        'True topology, physical area, hidden geometry and motion/reprojection are unavailable. '
        'This is spatial accumulation of each draw independently; no temporal accumulation.',variants={})
    variants={'source':source,'fixed-cutoff':archive['fixed']}
    for name,width in [('heat-width3',3.),('heat-width5.5',5.5),('heat-width11',11.)]:
        amount=width**2/4
        filtered=heat(source,amount);variants[name]=filtered
        difference=filtered.sum(axis=(1,2))-source.sum(axis=(1,2))
        assert np.max(np.abs(difference))<1e-8
        result['variants'][name]=dict(diffusion_amount=amount,
            interior_impulse_rms_radius_render_cells=width,hard_cutoff=None,
            max_rgb_sum_drift=float(np.max(np.abs(difference))),
            mean_rgb_before=source.mean(axis=(0,1,2)).tolist(),mean_rgb_after=filtered.mean(axis=(0,1,2)).tolist(),
            local_wash=color_wash(source,filtered),
            bands_render_cells=fluctuations(filtered,np.ones(source.shape[1:3],bool),[1,2,4,8]))
    np.savez_compressed(args.output/'whole-crop-heat.npz',**{k.replace('.','_'):v for k,v in variants.items()})
    (args.output/'results.json').write_text(json.dumps(result,indent=2)+'\n')
    folder=args.output/'frames';folder.mkdir(exist_ok=True)
    for i in range(len(source)):
        sheet=Image.new('RGB',(780,344),'#17191c');draw=ImageDraw.Draw(sheet)
        for k,(name,frames) in enumerate(variants.items()):
            x=(k%3)*260;y=(k//3)*172
            draw.text((x+3,y+3),name,fill='white')
            encoded=np.log1p(frames[i]*100)/np.log(11)
            image=Image.fromarray(np.uint8(np.clip(encoded,0,1)*255)).resize((260,148),Image.Resampling.NEAREST)
            sheet.paste(image,(x,y+24))
        draw.text((526,190),'OFFLINE SOURCE',fill='white')
        draw.text((526,210),'Uniform screen cells',fill='white')
        draw.text((526,230),'Insulating crop boundary',fill='white')
        draw.text((526,250),'No topology / motion',fill='white')
        sheet.save(folder/f'frame-{i:06}.png')
    # Center and corner impulses use one common normalization for both kernels.
    sheet=Image.new('RGB',(516,284),'#17191c');draw=ImageDraw.Draw(sheet)
    for col,(x,y,label) in enumerate([(32,32,'Center impulse'),(0,0,'Boundary impulse')]):
        impulse=np.zeros((65,65,3));impulse[y,x]=[2,4,8]
        moved=heat(impulse,7.5625)
        draw.text((col*258+3,3),label,fill='white')
        encoded=np.log1p(np.maximum(moved,0)*100)/np.log(11)
        image=Image.fromarray(np.uint8(np.clip(encoded,0,1)*255)).resize((258,258),Image.Resampling.NEAREST)
        sheet.paste(image,(col*258,24))
    sheet.save(args.output/'impulse-boundaries.png')


if __name__=='__main__':
    main()
