---
id: ter-s6zr
status: in_progress
deps: [ter-0pj8, ter-55xu]
links: [ter-bytc]
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

**2026-11-02T15:21:43Z**

2026-11-02: launched as workflow run 81862872-c55e-40a2-b270-d8bd69b9db11, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), branch ter-s6zr-corrected-read, base f9c571f74. It also builds ter-bytc (one cone, SSR's width).

**2026-11-02T17:15:22Z**

2026-11-02, the user's choice after the build (run 81862872, head ec384e78) stopped at the acceptance fork: probe, then merge. The corrected read missed the acceptance captures: the terminal door 109.7 against the reference's 65.5 (bound 44 to 87), ghosts of near objects on far walls from the solid rule (decision B: the terminal's pillar and kiosk, deck-late's glass building), a bright line at deck-late's floor-wall seam from the level-0 skin, a too-sharp reflection of the pillar's foot from the near read, and +1.7 ms per frame on the deck (ambient pass +2.7 ms). The user was told that changing the march's settings cannot remove the ghosts. Rejected: merge as built and fix later. A continuation on this branch probes candidate rules for the space behind a stored surface (the solid rule as the control, SSR's slab with today's read after a pass behind, the slab that reads the last surface seen in front) and a limit on the near read's sharpness, and the user chooses from sheets. ter-g6af and ter-pn1w wait for the merge.
