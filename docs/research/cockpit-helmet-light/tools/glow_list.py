"""Diagnostic tool code: the self-lit materials of cooked models, with the nodes that use them, the emitted radiance
(emissiveFactor x KHR_materials_emissive_strength, as GlbReader reads it), its saturation and the factor that today's
restrainNeutral applies when the glow enters the volume.
usage: glow_list.py <model.glb>..."""
import sys, json, struct


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


for path in sys.argv[1:]:
    b = open(path, 'rb').read()
    n = struct.unpack_from('<I', b, 12)[0]
    j = json.loads(b[20:20 + n])
    users = {}
    for node in j.get('nodes', []):
        if 'mesh' in node:
            for p in j['meshes'][node['mesh']]['primitives']:
                users.setdefault(p.get('material'), []).append(node.get('name', '?'))
    for i, m in enumerate(j.get('materials', [])):
        e = m.get('emissiveFactor') or [0, 0, 0]
        s = m.get('extensions', {}).get('KHR_materials_emissive_strength', {}).get('emissiveStrength', 1)
        if not any(e):
            continue
        rad = [c * s for c in e]
        sat = (max(rad) - min(rad)) / max(rad)
        f = 0.02 + 0.98 * smooth(0.58, 0.78, sat)
        nodes = sorted(set(users.get(i, [])))
        shown = ', '.join(nodes[:4]) + (f' and {len(nodes) - 4} more' if len(nodes) > 4 else '')
        print(f"| {path.split('/')[-2]} | {m.get('name')} | {shown or 'no node'} | "
              f"({rad[0]:.3g}, {rad[1]:.3g}, {rad[2]:.3g}) | {sat:.2f} | {100 * f:.0f}% |")
