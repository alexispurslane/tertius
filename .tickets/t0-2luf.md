---
id: t0-2luf
status: open
deps: []
links: []
created: 2026-11-15T09:40:44Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# FSR 3.1 darkens a noisy bounce image: with the diffuse filter off, a bounce history of 4 draws shows 5 to 15 % less light through FSR than at native resolution

Found by the diagnosis t8-69v7 (docs/research/t8-69v7-length-light/README.md, 'The second cause'). Lamp hall still rect-lamps at 4.08 stops, no jitter, clamps off, diffuse filter off, scene light, bounce history capped at 4 draws against no cap: 94.8 / 89.8 % (ceiling / walls) and 84.9 % of the whole image with FSR, 100.4 / 100.9 % at native resolution. The bounce signal alone loses up to 33 % over the whole image. Caps of 8 and 16 draws lose less (98.1 / 96.2 and 99.3 / 98.6). With the filter on, FSR does not change the cap's effect, so no shipping configuration shows it. Which step inside FSR removes the light is not measured. It matters for any configuration that shows a noisy bounce image through FSR.

