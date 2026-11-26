"""Donor reuse and explicitly offline adaptive controls on outgoing reflected RGB."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter,uniform_filter,maximum_filter,label,distance_transform_edt
from connected_model import connected
from multiscale import fluctuations
from measure import color_wash


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('raw',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--draws',action='store_true')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    width,height=map(int,(args.raw/'size.txt').read_text().split())
    # Complete 2x2 donor blocks wholly inside the existing sloped-panel crop.
    box=(584,28,714,102);x0,y0,x1,y1=box
    paths=sorted(args.raw.glob('*.sample.f32'))
    paths=paths[:33][8:] if args.draws else paths[16:48]
    n=len(paths)
    samples=np.stack([np.fromfile(p,dtype='<f4').reshape(height,width,4)[y0:y1,x0:x1].copy()
        for p in paths])
    rgb=samples[...,:3].astype(np.float64);ids=samples[...,3].astype(np.int64)
    luma=rgb@np.array([0.2126,0.7152,0.0722]);threshold=float(np.quantile(luma,0.99));bright=luma>threshold
    groups=[];shared_bright=0;shared_bright_multiple=0
    for i in range(len(rgb)):
        unique,counts=np.unique(ids[i][bright[i]],return_counts=True)
        for donor,count in zip(unique,counts):
            if donor>0:
                shared_bright+=int(count)
                if count>=2:shared_bright_multiple+=int(count)
                groups.append(int(count))
    result=dict(scope='OFFLINE source samples before history or tone mapping. Outgoing, pre-exposed diffuse-ray-sourced specular RGB only.',
        render_pixel_box=box,frames=list(range(8,33)) if args.draws else list(range(16,48)),sample_count=int(luma.size),
        shared_record_fraction=float(np.mean(ids>0)),bright_threshold=threshold,
        bright_shared_record_fraction=shared_bright/int(bright.sum()),
        bright_pixels_in_shared_groups_with_multiple_bright_receivers_fraction=shared_bright_multiple/int(bright.sum()),
        bright_shared_group_sizes={str(k):groups.count(k) for k in range(1,5)})
    # Count a complete shared 2x2 record once, not as four independent observations.
    block=luma.reshape(n,(y1-y0)//2,2,(x1-x0)//2,2).mean(axis=(2,4))
    block_donors=ids.reshape(n,(y1-y0)//2,2,(x1-x0)//2,2)
    result['fraction_blocks_with_one_shared_record']=float(np.mean(np.all(block_donors==block_donors[:,:,0,:,0][:,:,None,:,None],axis=(2,4)) & (block_donors[:,:,0,:,0]>0)))
    local_total=uniform_filter(block,size=(1,5,5),mode='reflect')*25
    dominance=maximum_filter(block,size=(1,5,5),mode='reflect')/np.maximum(local_total,1e-20)
    dominance_time_mean=dominance.mean(axis=0)
    mean=luma.mean(axis=0);std=luma.std(axis=0)
    cv=std/np.maximum(mean,1e-20)
    gy,gx=np.gradient(gaussian_filter(mean,3))
    gradient=np.hypot(gx,gy)/np.maximum(gaussian_filter(mean,3),1e-20)
    dominant=np.repeat(np.repeat(dominance_time_mean,2,axis=0),2,axis=1)
    uncertain=(np.count_nonzero(luma>np.quantile(luma,0.90),axis=0)<3)
    energy_reach=np.where((cv>1)&(dominant>0.5)&(gradient<0.15),11.,3.)
    energy_reach=gaussian_filter(energy_reach,2)
    energy_reach[uncertain]=0
    # A supported cluster must recur on at least three distinct captured draws.
    support=(np.count_nonzero(luma>np.quantile(luma,0.90),axis=0)>=3)&(mean>np.quantile(mean,0.75))
    labels,count=label(support,structure=np.ones((3,3)))
    sizes=np.bincount(labels.ravel());keep=[k for k in range(1,count+1) if sizes[k]>=4]
    fields=[distance_transform_edt(labels!=k) for k in keep]
    numerator=np.zeros_like(mean);denominator=np.zeros_like(mean);cluster_rows=[]
    for index,k in enumerate(keep):
        others=[fields[j][labels==k].min() for j in range(len(fields)) if j!=index]
        distance=float(min(others)) if others else None
        reach=float(np.clip(0.75*distance+2,3,11)) if distance is not None else 11.
        weights=np.exp(-fields[index]**2/72)
        numerator+=weights*reach;denominator+=weights
        cluster_rows.append(dict(label=k,pixels=int(sizes[k]),next_cluster_distance_render_pixels=distance,reach=reach))
    meeting_reach=np.divide(numerator,denominator,out=np.full_like(mean,3.),where=denominator>1e-20)
    # Conservative dark/gradient guard. Darkness may also mean missing samples.
    meeting_reach[uncertain | (gradient>=0.15)]=0
    result.update(dominance_method='5x5 complete donor-block means; exact donor interpretation only where all four pixels share a record.',
        dominance_median=float(np.median(dominance)),dominance_above_half_fraction=float(np.mean(dominance>0.5)),
        uncertain_fraction=float(uncertain.mean()),supported_clusters=cluster_rows,
        adaptive_scope='Uses the entire retained sequence, including future draws. This is an offline oracle, not a live classifier. '
            'Finite data cannot distinguish persistent darkness from unsampled reflected light. Zero reach protects uncertain pixels. '
            'The crop is an inspected continuous panel; no full geometry buffer is available. No claim about other boundaries.')
    surface=np.zeros(mean.shape,int)
    variants={'unspread':rgb}
    for name,reach in [('fixed',None),('energy-adaptive',energy_reach),('meeting-adaptive',meeting_reach)]:
        variants[name]=np.stack([connected(frame,surface,11,reach) for frame in rgb])
    result['variants']={}
    for name,frames in variants.items():
        drift=np.max(np.abs(frames.sum(axis=(1,2))-rgb.sum(axis=(1,2))))
        assert drift<1e-8,(name,drift)
        result['variants'][name]=dict(maximum_per_frame_rgb_sum_drift=float(drift),
            bands_render_pixels=fluctuations(frames,np.ones(mean.shape,bool),[1,2,4,8]),
            local_wash=color_wash(rgb,frames))
        # Common diagnostic encoding, never a replacement for a live render.
        encoded=np.log1p(frames.mean(axis=0)*100)/np.log(11)
        Image.fromarray(np.uint8(np.clip(encoded,0,1)*255)).resize((520,296)).save(args.output/(name+'-source-mean.png'))
    for name,reach in [('energy-adaptive',energy_reach),('meeting-adaptive',meeting_reach)]:
        result[name+'_reach_quantiles']=np.quantile(reach,[0,.25,.5,.75,1]).tolist()
        Image.fromarray(np.uint8(reach/11*255)).resize((520,296)).save(args.output/(name+'-reach.png'))
    np.savez_compressed(args.output/'source-and-offline-controls.npz',source=rgb,donor_ids=ids,
        fixed=variants['fixed'],energy_adaptive=variants['energy-adaptive'],meeting_adaptive=variants['meeting-adaptive'],
        energy_reach=energy_reach,meeting_reach=meeting_reach)
    (args.output/'results.json').write_text(json.dumps(result,indent=2)+'\n')


if __name__=='__main__':
    main()
