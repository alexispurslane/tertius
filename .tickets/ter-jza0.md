---
id: ter-jza0
status: open
deps: []
links: []
created: 2026-11-13T02:32:02Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# A rough metal at the hand-over roughness keeps a coarse grain in a settled still: the left mirror ball

Measured 2026-11-12 by the sweep agent (one workbench job, /private/tmp/tertius-sweep-20261111/grain/left-ball-2x.png): the left ball of scripts/mirror-balls.json (white metal, roughness 0.5) shows blobs 3 to 6 pixels wide on its shaded half, grain 10.9 to 12.0 levels RMS, the same before and after blue noise (ter-s97m) and the same at 33 and at 132 draws. Likely cause, not tested: 0.5 is the screen tracer's MaxRoughness, so the ball's specular light comes from the diffuse rays (cosine samples weighted to a GGX lobe, a high-variance estimate), and the history stops averaging at 32 draws, so the noise of a still never falls below that of about 63 effective samples. Two candidate fixes to measure, beside the truth: (1) once Design X (t7-3e22) gives reliable change evidence, a longer history for pixels without evidence, which would also lower the steady bounce mottle; (2) sample the rough specular lobe near the hand-over from the GGX lobe instead of the cosine lobe. Judge by crops beside the truth and the daub and grain scores.

