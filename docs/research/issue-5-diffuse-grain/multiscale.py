"""Display-linear spatial color bands, split into persistent and temporal parts."""
import numpy as np
from scipy.ndimage import gaussian_filter


def fluctuations(frames, mask, scales):
    result={}
    for scale in scales:
        band=gaussian_filter(frames,sigma=(0,scale,scale,0),mode='reflect')-gaussian_filter(
            frames,sigma=(0,2*scale,2*scale,0),mode='reflect')
        values=band[:,mask,:]
        persistent=values.mean(axis=0)
        temporal=values-persistent
        result[str(scale)]=dict(rgb_rms=float(np.sqrt(np.mean(values**2))),
            luminance_rms=float(np.sqrt(np.mean((values@np.array([0.2126,0.7152,0.0722]))**2))),
            persistent_rgb_rms=float(np.sqrt(np.mean(persistent**2))),
            temporal_rgb_rms=float(np.sqrt(np.mean(temporal**2))))
    return result
