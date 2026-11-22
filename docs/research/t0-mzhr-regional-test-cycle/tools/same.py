# same.py <a.json> <b.json>: whether two survey result files hold the same numbers. Prints the count of numbers, the
# count that differ, and the largest relative difference. A control run passes when no number differs.
import json, sys

def numbers(v, path=""):
    if isinstance(v, bool) or v is None or isinstance(v, str):
        yield path, v
    elif isinstance(v, (int, float)):
        yield path, float(v)
    elif isinstance(v, list):
        for i, x in enumerate(v):
            yield from numbers(x, f"{path}[{i}]")
    elif isinstance(v, dict):
        for k in sorted(v):
            yield from numbers(v[k], f"{path}.{k}")

a, b = (dict(numbers(json.load(open(p)))) for p in sys.argv[1:3])
keys = sorted(set(a) | set(b))
differ = [k for k in keys if a.get(k) != b.get(k)]
worst = max((abs(a[k] - b[k]) / max(abs(a[k]), abs(b[k]), 1e-30) for k in differ
             if isinstance(a.get(k), float) and isinstance(b.get(k), float)), default=0.0)
print(f"{len(keys)} values, {len(differ)} differ, largest relative difference {worst:.3g}"
      + (f", first: {differ[0]} {a.get(differ[0])} / {b.get(differ[0])}" if differ else ""))
