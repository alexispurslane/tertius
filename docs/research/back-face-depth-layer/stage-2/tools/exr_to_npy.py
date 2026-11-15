"""Throwaway (t1-g4p1): converts a Cycles EXR to a float32 NumPy array of linear RGB, rows from the top.

  blender -b --factory-startup --python exr_to_npy.py -- <in.exr> <out.npy>
"""
import sys
import bpy
import numpy as np

src, dst = sys.argv[sys.argv.index("--") + 1:][:2]
img = bpy.data.images.load(src)
img.colorspace_settings.name = "Non-Color"
w, h = img.size
px = np.empty(w * h * 4, dtype=np.float32)
img.pixels.foreach_get(px)
np.save(dst, px.reshape(h, w, 4)[::-1, :, :3].copy())
print("NPY", dst, w, h)
