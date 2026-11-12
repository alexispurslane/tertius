# fit.py <name:megapixels>...: a straight-line fit of what each pass pays against the render size's pixel count, from the
# traces of one view at several render scales. Prints the fixed part, the part per megapixel, and the fit's value at a
# quarter of the pixels of 1280 x 720 (0.2304 megapixels), the pixel count of a half-resolution pass.
import sys, json
S = '/private/tmp/tertius-sweep-20261111/trace/'
pts = []
for a in sys.argv[1:]:
    names, mp = a.rsplit(':', 1)
    ds = [json.load(open(S + n + '/pays.json')) for n in names.split('+')]
    passes = {p: sum(d['passes'].get(p, {'work': 0, 'idle': 0})['work'] + d['passes'].get(p, {'work': 0, 'idle': 0})['idle'] for d in ds) / len(ds) for p in ds[0]['passes']}
    passes['frame span (mean)'] = sum(d['span_mean'] for d in ds) / len(ds)
    pts.append((float(mp), passes))
xs = [x for x, _ in pts]; n = len(xs); mx = sum(xs) / n
print('| pass | ' + ' | '.join(f'{x:.3f} MP' for x in xs) + ' | fixed (ms) | per megapixel (ms) | at 0.2304 MP (ms) |')
print('|---|' + '---:|' * (n + 3))
for p in pts[0][1]:
    ys = [d.get(p, 0) for _, d in pts]
    if max(ys) < 0.03: continue
    my = sum(ys) / n
    b = sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sum((x - mx) ** 2 for x in xs)
    a = my - b * mx
    print(f'| {p} | ' + ' | '.join(f'{y:.3f}' for y in ys) + f' | {a:.3f} | {b:.3f} | {a + b * 0.2304:.3f} |')
