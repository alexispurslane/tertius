"""A small builder for shader node trees."""

import bpy


class NB:
    """Builds nodes in one node tree. Values may be numbers, tuples or output sockets."""

    def __init__(self, tree):
        self.t = tree
        self.col = 0

    def _sock(self, sockets, key):
        if isinstance(key, int):
            return sockets[key]
        for s in sockets:
            if s.identifier == key:
                return s
        return sockets[key]

    def set(self, sock, v):
        if isinstance(v, bpy.types.NodeSocket):
            self.t.links.new(v, sock)
        elif isinstance(v, bpy.types.Node):
            self.t.links.new(v.outputs[0], sock)
        else:
            if hasattr(sock, "default_value"):
                dv = sock.default_value
                if isinstance(v, (int, float)) and hasattr(dv, "__len__"):
                    n = len(dv)
                    v = (v,) * n if n != 4 else (v, v, v, 1.0)
                elif isinstance(v, tuple) and hasattr(dv, "__len__") and len(dv) == 4 and len(v) == 3:
                    v = (*v, 1.0)
                sock.default_value = v

    def n(self, type_, inputs=None, **props):
        nd = self.t.nodes.new(type_)
        for k, v in props.items():
            setattr(nd, k, v)
        for k, v in (inputs or {}).items():
            self.set(self._sock(nd.inputs, k), v)
        nd.location = (self.col * 40 % 4000, -(self.col // 100) * 300)
        self.col += 1
        return nd

    def out(self, nd, key=0):
        return self._sock(nd.outputs, key)

    def math(self, op, a, b=0.0, c=None, clamp=False):
        ins = {0: a, 1: b}
        if c is not None:
            ins[2] = c
        nd = self.n("ShaderNodeMath", ins, operation=op, use_clamp=clamp)
        return nd.outputs[0]

    def vmath(self, op, a, b=None, scale=None):
        ins = {0: a}
        if b is not None:
            ins[1] = b
        nd = self.n("ShaderNodeVectorMath", ins, operation=op)
        if scale is not None:
            self.set(nd.inputs["Scale"], scale)
        return nd.outputs["Value"] if op in ("DOT_PRODUCT", "DISTANCE", "LENGTH") else nd.outputs["Vector"]

    def sep(self, v):
        nd = self.n("ShaderNodeSeparateXYZ", {0: v})
        return nd.outputs["X"], nd.outputs["Y"], nd.outputs["Z"]

    def comb(self, x, y, z):
        nd = self.n("ShaderNodeCombineXYZ", {0: x, 1: y, 2: z})
        return nd.outputs[0]

    def mixc(self, fac, a, b, blend="MIX"):
        nd = self.n("ShaderNodeMix", {"Factor_Float": fac, "A_Color": a, "B_Color": b},
                    data_type="RGBA", blend_type=blend, clamp_factor=True)
        return self._sock(nd.outputs, "Result_Color")

    def mixf(self, fac, a, b):
        nd = self.n("ShaderNodeMix", {"Factor_Float": fac, "A_Float": a, "B_Float": b},
                    data_type="FLOAT", clamp_factor=True)
        return self._sock(nd.outputs, "Result_Float")

    def maprange(self, v, a, b, c=0.0, d=1.0, interp="LINEAR"):
        nd = self.n("ShaderNodeMapRange", {"Value": v, "From Min": a, "From Max": b,
                                           "To Min": c, "To Max": d}, interpolation_type=interp, clamp=True)
        return nd.outputs["Result"]

    def inrange(self, v, lo, hi, soft=0.05):
        """1 inside [lo, hi], 0 outside, with soft edges."""
        a = self.maprange(v, lo - soft, lo + soft, 0, 1, "SMOOTHSTEP")
        b = self.maprange(v, hi - soft, hi + soft, 1, 0, "SMOOTHSTEP")
        return self.math("MULTIPLY", a, b)

    def noise(self, vec, scale, detail=4.0, rough=0.5, dims="3D"):
        nd = self.n("ShaderNodeTexNoise", {"Vector": vec, "Scale": scale, "Detail": detail,
                                           "Roughness": rough}, noise_dimensions=dims)
        return nd.outputs["Fac"]

    def brick(self, vec, width, height, mortar, smooth, offset=0.5):
        nd = self.n("ShaderNodeTexBrick", {"Vector": vec, "Color1": (0, 0, 0), "Color2": (1, 1, 1),
                                           "Mortar": (1, 1, 1), "Scale": 1.0, "Mortar Size": mortar,
                                           "Mortar Smooth": smooth, "Bias": 0.0, "Brick Width": width,
                                           "Row Height": height},
                    offset=offset, offset_frequency=2, squash=1.0, squash_frequency=2)
        return nd.outputs["Fac"], nd.outputs["Color"]
