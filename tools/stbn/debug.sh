set -euo pipefail
nproc
(cd main && git apply --whitespace=nowarn tools/renderer-survey/probe.patch && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false | tail -1)
python3 -I - <<'PY'
import importlib.util, json, os, subprocess, time
from pathlib import Path
import numpy as np
from PIL import Image
W = Path.cwd()
spec = importlib.util.spec_from_file_location("survey", W / "main/tools/renderer-survey.py"); sv = importlib.util.module_from_spec(spec); spec.loader.exec_module(sv)
case = next(c for c in sv.load_cases()["stills"] if c["name"] == "cabin-light-cabin")
sv.STILL_CAPTURES = 4
script, first = sv.still_script(*sv.source_of(case))
out = W / "out"; out.mkdir(exist_ok=True)
(out / "s.json").write_text(json.dumps(script))
env = {**os.environ, "TERTIUS_PROBE_STILL_CONTINUES": "1", "TERTIUS_PROBE_EXPOSURE": "4.08", "TERTIUS_PROBE_SIGNAL": "0"}
t = time.time()
r = subprocess.run([str(W / "main/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo"), "--script", str(out / "s.json"), "--headless", "--out", str(out), "--size", "1920x1080", "--bloom", "off", "--tone-curve", "hable"], cwd=W / "main", env=env, capture_output=True, text=True)
print("render seconds", time.time() - t, r.returncode); print(r.stdout[-1500:]); print(r.stderr[-800:])
for k in range(4):
    p = out / f"v-s{k:02d}.png"
    t = time.time(); y = sv.probe_luminance(p); dt = time.time() - t
    a = np.asarray(Image.open(p))
    print(k, a.shape, a.dtype, "pixel(960,540)", a[540, 960], "pixel(100,100)", a[100, 100], "y pct", np.percentile(y, [1, 10, 50, 90, 99]), "decode s", dt)
PY
ls -la out | head
