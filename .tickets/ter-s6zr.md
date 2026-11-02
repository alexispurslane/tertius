---
id: ter-s6zr
status: open
deps: [ter-0pj8, ter-55xu]
links: []
created: 2026-11-02T12:51:56Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The corrected read: every reader of the ambient specular term reads the map where its ray ends

Ticket 3 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 3', 'The search' and 'Every reader'). mapHit, the corrected environmentSpecular through ambientTerms, ShadingInput.position, and the specular occlusion over the whole corrected term (the user's answer A to open question 2). A ray that meets no stored surface reads exactly today's texel. Acceptance: GPU tests 16 to 21 pass in Checked and Release, each red first; changed reference images reviewed with the three images; screenshots of the terminal floor, the floor film and pbr-spheres before and after.


## Notes

**2026-11-02T14:23:54Z**

2026-11-02, from ter-0pj8's build, review and fix (run 81bbe30d), for the GLSL mapHit: (1) Copy MapSearch.Hit as it ends on ter-0pj8's branch: the near sample t0 = 1.0/64.0 first, then t = k/8 for k = 1..8, front first (a behind sample counts only after a front one), the first bracket stops the march, exactly 3 regula falsi steps, then a last secant place with no read; at most 12 reads; precise on every line of the sweep, the clearance and the secant. (2) The near sample is not special: same clearance, same cone level, same short-lookup rule. Its cone level is NOT always 0: it is above 0 at W = 128 for roughness above about 0.61, and at W = 1024 above about 0.22. (3) Do not rely on the near sample leaving the even samples' hits unchanged: where t0 lies in front and t = 1/8 lies behind, the bracket [t0, 1/8] wins and replaces a later hit (decision 16, the bar test). (4) A sky whose every w is 0 gives d bit for bit with reach 1. (5) The C# twin computes in double; test 20 compares within 1e-3. (6) On the shaded point's own plane a mirror ray has c(t) = t, so the near sample's margin is 1/64: a relative error of w above about 1.6 % (a stale face, the bilinear error at grazing views) drops the near sample, and the near wall's base is lost again, with no false self-hit. (7) Regula falsi creeps toward a hard depth edge and stops up to about 3 level-0 texels short in the near bracket, about 1.3 in [1/8, 1/4] ('Known limits').
