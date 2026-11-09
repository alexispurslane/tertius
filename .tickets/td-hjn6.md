---
id: td-hjn6
status: closed
deps: []
links: [ter-18hc, ter-9371]
created: 2026-11-09T02:04:37Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# The slow history's clamp cuts the light that the diffuse filter moves into a quiet pixel

Found by ter-9371 (2026-11-08). The diffuse filter writes its first step's light into the diffuse slow history (SVGF's feedback). Light that the filter moves into a quiet pixel, whose fast histories form a narrow box, leaves that box, and the slow history's clamp (accumulate_spatial.comp, step 4) cuts it. On the orbit cabin at a fixed exposure without FSR, the filtered image's linear mean is -5.3 % (ceiling), -2.7 % (floor) and -9.2 % (left wall) of the unfiltered one. With the clamp off, the filter keeps the light within about 5 % (-4.1 %, +4.8 %, -4.5 %). With FSR the means stay within +3.1 % to -3.2 %. Tried and rejected in ter-9371: moving the fast history by the same light (a self-sustaining blur of an image without noise), and a luminance stop on the smaller variance (worse loss and specks). The clamp itself loses 30 % to 45 % of the cabin's light with or without the filter (ter-18hc), so the fix likely belongs to that ticket. Measurements and scripts: /private/tmp/tertius-diffuse-filter-20261108/.


## Notes

**2026-11-09T12:08:39Z**

2026-11-09: closed by the user's decision to keep the clamp (see ter-18hc's note). The loss is the clamp's known bias on rare bright samples, measured in /private/tmp/tertius-diffuse-clamp-20261108/.
