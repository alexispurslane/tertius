---
id: ter-nw4a
status: open
deps: []
links: [ter-eqtb]
parent: ter-b2da
created: 2026-11-02T18:08:19Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# A reflected outline at a depth edge shows as large pixel stairs on a floor seen at a grazing angle

Asked by the user on 2026-11-02 about the floor film's stair steps in ter-eqtb's sheet at the old limit 0.6 (captures/eqtb/fix/sheets/shot-floor06-mutual.png, crops in captures/ring/). What remains after ter-eqtb's bounded planes: a pixel at a depth edge keeps one depth over the half of its square past its center line, so the edge of a reflected surface is a staircase of whole pixels. A floor seen at a grazing angle stretches each pixel of that staircase into a long step, and each floor pixel takes one ray with a hard hit, so a floor of roughness 0.38 shows the staircase sharply where a 0.38 lobe gives a soft outline. On demo-exit's floor film at limit 0.6 the steps lie from y 540 to 600 (ter-eqtb's notes). At today's limit 0.37 that floor (paint, 0.38) takes no SSR and shows no steps (captures/ring/floor-steps-037-vs-06-2x.png). It shows on surfaces below 0.37 that reflect a depth edge at a grazing angle, at a size that shrinks with the roughness, and it stands in the way of a higher limit. Design it twice before a build, for example: an outline as soft as the cone's footprint at the hit, against a place of the edge inside the pixel. The user's decisions hold: nothing accumulates over frames, no stochastic SSR (ter-rnjk closed).


## Notes

**2026-11-02T18:11:44Z**

2026-11-02, the user's decision: this ticket belongs to the correctness epic ter-b2da and blocks the performance work behind it. The user's reasons: the steps look horrendous, they point to deeper issues with SSR, and the fix can change the cost, so it must come before performance work. Priority 1.
