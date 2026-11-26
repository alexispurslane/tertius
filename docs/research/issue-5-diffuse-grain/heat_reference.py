"""Offline exact heat flow on a rectangular grid with insulating crop boundaries.

Cells have equal screen area. This supplies neither true surface topology nor motion.
"""
import numpy as np
from scipy.fft import dctn,idctn


def heat(image,amount):
    height,width=image.shape[-3:-1]
    row_rate=2*np.cos(np.pi*np.arange(height)/height)-2
    column_rate=2*np.cos(np.pi*np.arange(width)/width)-2
    rate=row_rate[:,None]+column_rate[None,:]
    coefficients=dctn(image,type=2,axes=(-3,-2),norm='ortho')
    return idctn(coefficients*np.exp(amount*rate)[...,None],type=2,axes=(-3,-2),norm='ortho')
