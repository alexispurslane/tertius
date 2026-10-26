"""An independent copy of MPFB's macro slider formula, the test oracle of test_mpfb.py.

MPFB 2.0.18 turns the macro sliders of a human (gender, age, muscle, weight, proportions, height, cup size, firmness
and the three race shares) into a stack of weighted targets: TargetService._interpolate_macro_components and
calculate_target_stack_from_macro_info_dict. This module computes the same stack from data/targets/macrodetails/
macro.json, as the astronaut's s10_body.py did before the models ran MPFB itself, and sums the targets onto the base
mesh with numpy. The test builds the same bodies through MPFB's own API and compares the vertices.

Pure Python and numpy: it runs in Blender's Python and reads only MPFB's data files.
"""

import gzip
import json
import os

import numpy as np

MACROS = ("gender", "age", "muscle", "weight", "proportions", "height", "cupsize", "firmness")


def read_obj_vertices(path):
    """The vertex positions of an OBJ file, in its own units and axes."""
    verts = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("v "):
                verts.append([float(t) for t in line.split()[1:4]])
    return np.array(verts, dtype=np.float64)


class Macros:
    """The macro targets of MPFB's data folder."""

    def __init__(self, data):
        self.data = data
        with open(os.path.join(data, "targets", "macrodetails", "macro.json"), encoding="utf-8") as fh:
            self.parts = json.load(fh)["macrotargets"]

    def components(self, name, value):
        """The (target word, weight) pairs of one slider value. A value inside the open range of a part splits
        linearly between the part's low and high word, each weight rounded to four decimals."""
        out = []
        for part in self.parts[name]["parts"]:
            lowest, highest = part["lowest"], part["highest"]
            if lowest < value < highest:
                position = (value - lowest) / (highest - lowest)
                if part["low"]:
                    out.append((part["low"], round(1 - position, 4)))
                if part["high"]:
                    out.append((part["high"], round(position, 4)))
        return out

    def stack(self, sliders, cutoff=0.01):
        """The (target name, weight) pairs of the sliders: a dict with the eight MACROS and "race", a dict of
        race -> share. A target whose weight is not above `cutoff` is left out."""
        c = {k: self.components(k, sliders[k]) for k in MACROS}
        out = []
        for race, share in sliders["race"].items():
            if share > 0.0001:
                for a, wa in c["age"]:
                    for g, wg in c["gender"]:
                        if g != "universal" and share * wg * wa > cutoff:
                            out.append(("macrodetails/{}-{}-{}".format(race, g, a), share * wg * wa))
        for g, wg in c["gender"]:
            for a, wa in c["age"]:
                for m, wm in c["muscle"]:
                    for wt, ww in c["weight"]:
                        w = wg * wa * wm * ww
                        if w > cutoff:
                            out.append(("macrodetails/universal-{}-{}-{}-{}".format(g, a, m, wt), w))
        for g, wg in c["gender"]:
            for a, wa in c["age"]:
                for m, wm in c["muscle"]:
                    for wt, ww in c["weight"]:
                        for h, wh in c["height"]:
                            w = wg * wa * wm * ww * wh
                            if w > cutoff:
                                out.append(("macrodetails/height/{}-{}-{}-{}-{}".format(g, a, m, wt, h), w))
        # Breasts: female only, and the gender weight is not a factor, because no male targets complement them.
        for g, _wg in c["gender"]:
            if g != "female":
                continue
            for a, wa in c["age"]:
                for m, wm in c["muscle"]:
                    for wt, ww in c["weight"]:
                        for cup, wc in c["cupsize"]:
                            for f, wf in c["firmness"]:
                                name = "breast/{}-{}-{}-{}-{}-{}".format(g, a, m, wt, cup, f)
                                w = wa * wm * ww * wc * wf
                                if w > cutoff and "averagecup-averagefirmness" not in name and "-baby-" not in name:
                                    out.append((name, w))
        for g, wg in c["gender"]:
            for a, wa in c["age"]:
                for m, wm in c["muscle"]:
                    for wt, ww in c["weight"]:
                        for p, wp in c["proportions"]:
                            name = "macrodetails/proportions/{}-{}-{}-{}-{}".format(g, a, m, wt, p)
                            w = wg * wa * wm * ww * wp
                            if w > cutoff and "-baby-" not in name:
                                out.append((name, w))
        return out

    def body(self, sliders):
        """The base mesh with the stack of the sliders summed onto it: every vertex of base.obj, in meters, with +Z up
        and the face toward -Y. A point (x, y, z) of the file becomes (x, -z, y) / 10."""
        coords = read_obj_vertices(os.path.join(self.data, "3dobjs", "base.obj"))
        for name, weight in self.stack(sliders):
            path = os.path.join(self.data, "targets", name + ".target.gz")
            with gzip.open(path, "rt", encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line or line[0] in "#\"":
                        continue
                    p = line.split()
                    coords[int(p[0])] += weight * np.array((float(p[1]), float(p[2]), float(p[3])))
        return np.stack((coords[:, 0], -coords[:, 2], coords[:, 1]), axis=1) * 0.1
