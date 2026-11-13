# PROBE (scratch, never merges). job_score.py <base> <case> <variant...>: in <base>, whose s/<variant> are survey outputs,
# prints daub.py's score of each variant's shown still against the truth's long reference (REF=truth) and light.py's
# regional light of each variant over the truth's, and packs the shown stills as lossless WebP plus the truth's reference
# PNG into <base>/pack/<case>/, so that a GPU job returns them under 8 MB. daub.py and light.py are copies, unchanged.
import sys, os, runpy, shutil
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
base, case, variants = sys.argv[1], sys.argv[2], sys.argv[3:]
os.chdir(base)
import light
FRAME = [[(0, 0), (1920, 0), (1920, 1080), (0, 1080)]]
light.REGIONS.setdefault(case, {'frame': FRAME})
os.environ['REF'] = 'truth'
sys.argv = ['daub.py'] + [f'{v}:{case}' for v in ['truth'] + variants]
runpy.run_path(os.path.join(here, 'daub.py'), run_name='__main__')
t = light.scene(f's/truth/images/still/{case}-reference.png')
smooth = light.smooth_of(t)
for v in ['truth'] + variants:
    s = light.scene(f's/{v}/images/still/{case}.png'); r = light.scene(f's/{v}/images/still/{case}-reference.png')
    cells = []
    for name, polys in light.REGIONS[case].items():
        m = light.mask(polys) & smooth
        cells.append(f"{name} shown {100 * s[m].mean() / t[m].mean():6.1f}% settled {100 * r[m].mean() / t[m].mean():6.1f}%")
    print(f"light {case:18s} {v:8s} " + "   ".join(cells), flush=True)
from PIL import Image
pack = os.path.join(base, 'pack', case); os.makedirs(pack, exist_ok=True)
shutil.copy(f's/truth/images/still/{case}-reference.png', f'{pack}/truth-reference.png')
for v in variants:
    Image.open(f's/{v}/images/still/{case}.png').convert('RGB').save(f'{pack}/{v}.webp', lossless=True, quality=100, method=4)
print('packed', sorted(os.listdir(pack)), sum(os.path.getsize(os.path.join(pack, f)) for f in os.listdir(pack)), flush=True)
