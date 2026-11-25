"""Tool code. Readers of Pioneer's model files that need no Blender: the material file (.model) and the static meshes of
a COLLADA file (.dae). Blender 5.2 has no COLLADA importer, so the converter builds its meshes from what these return.

The standard library only, so that the tests run in the default dev shell."""

import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field


@dataclass
class PioneerMaterial:
    """One material of a Pioneer .model file. Texture names are the file names that the .model gives, such as
    "facades_diff.dds". `opacity` is in percent, 100 for opaque."""

    name: str
    diffuse: str | None = None
    glow: str | None = None
    unlit: bool = False
    opacity: float = 100.0
    blend_alpha: bool = False
    diffuse_color: tuple = (1.0, 1.0, 1.0)


def parse_model_file(text):
    """The materials of a Pioneer .model file by name. Lines outside a material block, such as `lod` and `collision`,
    are skipped, because the sidecar names the meshes."""
    materials = {}
    current = None
    # The depth of the braces inside the open material: a render_state block opens one more.
    depth = 0
    for raw in text.splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        start = re.match(r'material\s+"([^"]+)"\s*\{', line)
        if start:
            current = PioneerMaterial(start.group(1))
            materials[current.name] = current
            depth = 0
            continue
        if current is None:
            continue
        if line == "}":
            if depth == 0:
                current = None
            else:
                depth -= 1
            continue
        if line.endswith("{"):
            depth += 1
            continue
        words = line.split()
        if words[0] == "texture" and len(words) >= 3 and words[1] in ("diffuse", "glow"):
            setattr(current, words[1], words[2].strip('"'))
        elif words[0] == "unlit":
            current.unlit = True
        elif words[0] == "opacity":
            current.opacity = float(words[1])
        elif words[:2] == ["blend", "alpha"]:
            current.blend_alpha = True
        elif words[0] == "diffuse" and len(words) >= 4:
            current.diffuse_color = tuple(float(w) for w in words[1:4])
    return materials


@dataclass
class Primitive:
    """The triangles of one material of one geometry: per corner a position index, a normal and a UV.

    `positions` are the geometry's points in its own axes. `corners` holds (position index, normal, uv) for each
    triangle corner, three per triangle, in COLLADA's counterclockwise order."""

    material: str
    corners: list = field(default_factory=list)


@dataclass
class Geometry:
    name: str
    positions: list
    primitives: list


@dataclass
class Instance:
    """One placed geometry: the node name, the 4x4 world matrix as four rows, and the bound materials of the
    geometry's symbols."""

    node: str
    geometry: str
    matrix: tuple
    materials: dict


def _strip_namespaces(tree):
    for element in tree.iter():
        element.tag = element.tag.split("}")[-1]


def _sources(mesh):
    out = {}
    for source in mesh.findall("source"):
        values = [float(x) for x in source.findtext("float_array").split()]
        stride = int(source.find("technique_common/accessor").get("stride"))
        out["#" + source.get("id")] = [tuple(values[i : i + stride]) for i in range(0, len(values), stride)]
    return out


def _triangulate(counts, corners):
    """Fans each polygon of `counts` corners into triangles, in order. Pioneer's polygons are convex quads and
    triangles from Blender's exporter."""
    out = []
    start = 0
    for count in counts:
        polygon = corners[start : start + count]
        for i in range(1, count - 1):
            out.extend((polygon[0], polygon[i], polygon[i + 1]))
        start += count
    return out


def _geometry(element):
    mesh = element.find("mesh")
    sources = _sources(mesh)
    vertices = {"#" + v.get("id"): v.find('input[@semantic="POSITION"]').get("source") for v in mesh.findall("vertices")}
    positions_source = None
    primitives = []
    for prim in mesh:
        if prim.tag not in ("triangles", "polylist"):
            # Lines and points draw nothing in the engine.
            continue
        inputs = prim.findall("input")
        stride = max(int(i.get("offset")) for i in inputs) + 1
        by_semantic = {}
        for i in inputs:
            semantic = i.get("semantic")
            # The first texture set is the one that Pioneer's materials sample.
            if semantic == "TEXCOORD" and int(i.get("set", "0")) != 0:
                continue
            by_semantic.setdefault(semantic, (int(i.get("offset")), i.get("source")))
        vertex_offset, vertex_source = by_semantic["VERTEX"]
        positions_source = vertices[vertex_source]
        normal = by_semantic.get("NORMAL")
        uv = by_semantic.get("TEXCOORD")
        indices = [int(x) for x in (prim.findtext("p") or "").split()]
        n = len(indices) // stride
        corners = []
        for c in range(n):
            base = c * stride
            corners.append((
                indices[base + vertex_offset],
                sources[normal[1]][indices[base + normal[0]]][:3] if normal else None,
                sources[uv[1]][indices[base + uv[0]]][:2] if uv else (0.0, 0.0),
            ))
        counts = [3] * (n // 3) if prim.tag == "triangles" else [int(x) for x in prim.findtext("vcount").split()]
        primitives.append(Primitive(material=prim.get("material", ""), corners=_triangulate(counts, corners)))
    positions = [p[:3] for p in sources[positions_source]] if positions_source else []
    return Geometry(name=element.get("id"), positions=positions, primitives=primitives)


def _matmul(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)) for i in range(4))


IDENTITY = ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))


def read_collada(text, drop_prefixes):
    """The geometries of the COLLADA text by id, the instances of its visual scene, and the names of the dropped nodes.

    A node whose lowercase name starts with one of `drop_prefixes` is dropped with all its children. Every node
    holds a <matrix>, as Blender's exporter writes it: row-major, for column vectors."""
    root = ET.fromstring(text)
    _strip_namespaces(root)
    material_names = {"#" + m.get("id"): m.get("name") or m.get("id") for m in root.iter("material")}
    geometries = {"#" + g.get("id"): _geometry(g) for g in root.iter("geometry") if g.find("mesh") is not None}
    instances = []
    dropped = []

    def visit(node, parent):
        name = node.get("name") or node.get("id") or ""
        if name.lower().startswith(tuple(drop_prefixes)):
            dropped.append(name)
            return
        text = node.findtext("matrix")
        local = IDENTITY
        if text:
            values = [float(x) for x in text.split()]
            local = tuple(tuple(values[r * 4 : r * 4 + 4]) for r in range(4))
        world = _matmul(parent, local)
        for inst in node.findall("instance_geometry"):
            url = inst.get("url")
            if url not in geometries:
                continue
            bound = {m.get("symbol"): material_names.get(m.get("target"), m.get("target", "").lstrip("#"))
                     for m in inst.iter("instance_material")}
            instances.append(Instance(node=name, geometry=url, matrix=world, materials=bound))
        for child in node.findall("node"):
            visit(child, world)

    for scene in root.iter("visual_scene"):
        for node in scene.findall("node"):
            visit(node, IDENTITY)
    return geometries, instances, dropped
