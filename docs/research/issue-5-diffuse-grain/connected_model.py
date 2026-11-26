"""Independent dense compact exchange with a connected raster path guard."""
import numpy as np


def connected(image,surface,radius,reach=None):
    result=image.copy()
    height,width=surface.shape
    offsets=[(x,y,(1-(x*x+y*y)/radius**2)**2) for y in range(-radius+1,radius)
        for x in range(-radius+1,radius) if x*x+y*y<radius**2]
    normalizer=sum(w for x,y,w in offsets)
    for dx,dy,weight in offsets:
        if not (dx or dy):
            continue
        y0,y1=max(0,-dy),min(height,height-dy)
        x0,x1=max(0,-dx),min(width,width-dx)
        if y0>=y1 or x0>=x1:
            continue
        p=np.s_[y0:y1,x0:x1];q=np.s_[y0+dy:y1+dy,x0+dx:x1+dx]
        agrees=surface[p]==surface[q]
        steps=max(abs(dx),abs(dy))
        yy,xx=np.mgrid[y0:y1,x0:x1]
        for i in range(1,steps):
            cy=(yy*steps+dy*i+steps//2)//steps
            cx=(xx*steps+dx*i+steps//2)//steps
            agrees &= surface[cy,cx]==surface[p]
        share=weight/normalizer
        if reach is not None:
            common=np.minimum(reach[p],reach[q])
            share=np.maximum(0,1-(dx*dx+dy*dy)/np.maximum(common**2,1e-20))**2/normalizer
        result[p]+=np.asarray(share)[...,None]*agrees[...,None]*(image[q]-image[p])
    return result
