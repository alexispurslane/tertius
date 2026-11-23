# Tool code of ter-pdw8, group 4: the survey's walk (tools/renderer-survey.py, part walk, case cabin-walk) of a variant
# against V0's. Each result holds, per film frame, the region means of the shown image (the whole image, then the 3 x 3
# grid by rows, display luminance): "during" the 120 frames of the walk, "after" the 200 frames after the stop, and the
# settle draws of each region after the stop against a control that stands where the walk ends.
# Per region: the variant's mean over V0's - 1 over the first 30 frames of the walk (the areas that the walk uncovers
# enter there first), over the whole walk, the lowest per-frame ratio, and the settle draws of both.
# Usage: python3 walkcmp.py <V0 result json> <variant result json>
import json, sys

v0, v = (json.load(open(p)) for p in sys.argv[1:3])
names = ['whole image'] + [f'grid {r},{c}' for r in range(3) for c in range(3)]
out = {}
for i, name in enumerate(names):
    a = [row[i] for row in v0['during']]
    b = [row[i] for row in v['during']]
    first = sum(b[:30]) / max(sum(a[:30]), 1e-9) - 1
    whole = sum(b) / max(sum(a), 1e-9) - 1
    lowest = min(y / x - 1 for x, y in zip(a, b) if x > 1e-6)
    out[name] = {'first_30_frames': round(first, 4), 'whole_walk': round(whole, 4), 'lowest_frame': round(lowest, 4),
                 'settle_draws_v0': v0['regionDraws'][i], 'settle_draws_variant': v['regionDraws'][i]}
out['settle'] = {'v0': v0['settleDraws'], 'variant': v['settleDraws']}
print(json.dumps(out, indent=1))
