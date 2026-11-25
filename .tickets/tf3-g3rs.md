---
id: tf3-g3rs
status: closed
deps: [ter-fe63]
links: []
created: 2026-11-24T16:15:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# The adaptive reflection share costs 0.67 to 0.94 ms of GPU frame time, most of it where the share is 0: place it per pass and cut the share-0 work

**2026-11-25 · Superseded by ter-xzj7.** Use the old observations as controls after isolating which pass produces the filmed berth grain. A result on the old test scene alone would not settle the film defect.

Found by the stage 2 review of ter-fe63 (finding F1), 2026-11-24.

Measured in Release under the exclusive GPU lock at 1920 x 1080 and a render scale of 2/3, against unpatched main 56feef7d5 (docs/research/reflection-mixture-adaptive/production/README.md):
- the sunlit nose: +0.817 ms (+5.52 %);
- the bay: +0.944 ms (+6.38 %);
- rect-lamps: +0.673 ms (+4.54 %), although its glossy floor's share settles at 0.

Every run held the GPU clock at 1296 MHz. The branch was at commit 945f36790.

So most of the cost does not depend on the share. Candidates for that fixed part:
- the guided density of every rough block reflection ray at a share of 0, which the tile's evidence needs;
- the BRDF evaluated again in shareEvidenceOf;
- the four image reads of the bilinear share per ray;
- the 'reflection share' dispatch.

The epic's goal is a frame under 12 ms. The user delegated the acceptance of measured costs to the parent orchestrator on 2026-11-24, so the cost does not hold ter-fe63's merge. This ticket looks for the cheaper path.

Do: place the cost per pass with a Metal System Trace (tools/gpu-trace.sh), since MoltenVK's pass stamps latch late. Then try a cheaper share-0 path, for example evidence from 1 in N blocks while a tile sits at 0, and keep the share map and the nose's specks unchanged.

