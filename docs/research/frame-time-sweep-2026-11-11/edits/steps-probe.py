# The step cap variables of steps.py on top of the renderer survey's probe (tools/renderer-survey/probe.patch), so that
# the survey's still part can render a step cap variant for the daub score. Main's probe patch names specialization
# constant 11, which the shadow tile's corner means took (t2-gfq5), so its two hunks of ShaderConstants.cs fail. This
# script applies the rest and gives the probe's signal constant the id 12 by hand.
import subprocess, sys, runpy, re
root = sys.argv[1]
done = subprocess.run(['patch', '-p1', '--forward', '--batch', '-i', root + '/tools/renderer-survey/probe.patch'], cwd=root,
                      capture_output=True, text=True)
failed = re.findall(r"saving rejects to file (\S+)\.rej", done.stdout) + re.findall(r"hunks? failed while patching '([^']+)'", done.stdout)
assert failed == ['src/Tertius.Rendering/ShaderConstants.cs'], done.stdout
p = root + '/src/Tertius.Rendering/ShaderConstants.cs'; s = open(p).read()
head, tail = s.split('        ];\n        for (int i = 0; i < rows.Length; i++)', 1)
s = head + '            (12u, uint.TryParse(Environment.GetEnvironmentVariable("TERTIUS_PROBE_SIGNAL"), out uint probeSignal) ? probeSignal : 0u),\n        ];\n        for (int i = 0; i < rows.Length; i++)' + tail
assert s.count('public const int Count = 11;') == 1
s = s.replace('public const int Count = 11;', 'public const int Count = 12;'); open(p, 'w').write(s)
p = root + '/src/Tertius.Rendering/Shaders/composite_pass.glsl'; s = open(p).read()
assert s.count('layout(constant_id = 11) const int probeSignal = 0;') == 1
open(p, 'w').write(s.replace('layout(constant_id = 11) const int probeSignal = 0;', 'layout(constant_id = 12) const int probeSignal = 0;'))
import glob, os
for f in glob.glob(root + '/**/*.orig', recursive=True) + glob.glob(root + '/**/*.rej', recursive=True): os.remove(f)
runpy.run_path('/private/tmp/tertius-sweep-20261111/edits/steps.py', run_name='__main__')
