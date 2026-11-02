---
id: ter-s6zr
status: in_progress
deps: [ter-0pj8, ter-55xu]
links: [ter-bytc, ter-b1j3]
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

**2026-11-02T17:16:48Z**

2026-11-02: the probe launched as workflow run efa3cac1-dd4f-40cd-bc78-443f263f4140, version 869b0b07-3413-4b06-8707-a88a91bf6e91, on branch ter-s6zr-corrected-read at ec384e78 (brief /tmp/briefs/s6zr-probe.txt). Candidates: S (the solid rule, control), K (SSR's slab, today's read after a pass behind), L (the slab, the read of the last place seen in front), R (a limit on the near read's sharpness), and a fix of the skin line if none removes it.

**2026-11-02T18:37:19Z**

2026-11-02, the user's choice after the probe (run efa3cac1, head f2c6511ed, sheets in captures/s6zr/sheets/probe): hold the corrected read for one cone model. No one-ray candidate removes the ghosts, the door's overshoot (S 110, R0 92, M 93, reference 65.5) or the pillar's foot; only many rays (M, 32 rays, +195 ms) remove them. All three come from one ray per lobe, the same cause as SSR's artifacts 3 and 5. Next: if ter-nw4a's cone coverage (the share of the cone's footprint on each surface) works for SSR, a design gives the map's search the same model, so that one lobe model serves both readers; then this branch continues with that design and Q2 (the near-entry hit, probes/q2.patch), and merges. If cone coverage fails, the user chooses again between merging S + Q2 and keeping today's read. Rejected for now: merge S + Q2 at once (+1.1 to +2.0 ms per frame, with the ghosts and the foot). Settled by the orchestrator inside the merge design: no R0. The probe's side finding, the dark lamp glow in the faces, is ticket ter-b1j3. ter-s6zr now depends on ter-nw4a; ter-g6af and ter-pn1w wait.

**2026-11-02T19:45:32Z**

2026-11-02, the user's choice after ter-nw4a's probe: ter-nw4a's design A (the neighbors' rays inside the footprint) does not carry over to the map's search, so the next step is a probe of the moment form on this branch: the distance cube stores the mean and the mean square of w per texel (RG16F or RG32F), and a Chebyshev bound gives the share of the cone in front of the ray point, as variance shadow maps do. Rejected for now: merge S + Q2, and keep today's read.

**2026-11-02T19:47:06Z**

2026-11-02: the brief of the moment-form probe is /tmp/briefs/s6zr-moments.txt. It launches when a builder slot frees (four workflows run now: ter-nw4a, ter-ipc1, ter-b1j3, ter-cvyx).

**2026-11-02T20:59:21Z**

2026-11-02: the moment probe launched as workflow run e7fd2065-e609-4bb1-bd3d-48a09b901e3e, version 869b0b07-3413-4b06-8707-a88a91bf6e91, on branch ter-s6zr-corrected-read (brief /tmp/briefs/s6zr-moments-launch.txt). It merges main first (ter-b1j3 is in main since 9fda8f414), so lobenorm counts each lamp once (ref1), then probes the moment form with RG16F and RG32F against S + Q2 and M, and ends at a fork.

**2026-11-02T22:27:21Z**

2026-11-02, the user's choice after the moment probe (run e7fd2065, head f99de1704, sheets in captures/s6zr/sheets/moments): S + Q2, the solid rule as built plus the near-sample hit. Rejected: the moment form V32 + Q2, and keeping today's read (the orchestrator and the probe had recommended today's read). The user was told: S + Q2 removes the seam lines for about 0.05 ms over S; the soft ghosts on far walls, the door's overshoot (110 against ref1's 64) and the pillar's sharp foot stay; 11 acceptance misses, mean box error 4.87 against ref1; +2.7 ms on the ambient pass over today's read. The user also keeps the distance cube (ter-55xu). The dependency on ter-nw4a is removed: the map takes no cone model, and the build's one owner of the lobe's cone covers ter-nw4a's copy if ter-nw4a merges first.

**2026-11-02T22:28:21Z**

2026-11-02: the build of S + Q2 launched as workflow run 113a5f6f-0c2a-4d78-8c6d-f370cf1120f3, version 869b0b07-3413-4b06-8707-a88a91bf6e91, on branch ter-s6zr-corrected-read (brief /tmp/briefs/s6zr-build-sq2.txt). It merges main, ports Q2 with its C# rule and twin test, makes the one owner of the lobe's cone cover ter-nw4a's site if ter-nw4a is in main, and records the known limits and the accepted misses.
