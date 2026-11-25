---
id: t88-vj2g
status: closed
deps: []
links: []
created: 2026-11-25T05:40:57Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [tests, flaky, rendering]
---
# SpacewalkSliceTests.FromEarthThePersonSeesThePortFaceAndTheHatchAtTheirSizeAtItsDistanceAboveEarth failed once in a full Host run under load: the sill's pixel span came out as -1280

**2026-11-25 · Retired.** This test or host failure was reported under load or in an isolated run. Keep its log. A repeat on the current build with the GPU reserved warrants a new regression ticket.

Seen on ter-9wwx (2026-11-25, 00:45), in a full Checked run of Tertius.Host.Tests at a load of about 7, while no other GPU run held the lock: 'Expected (last - first + 1) to be within 2u from 61 because the hatch's sill is 1.2 m wide, but found -1280.' A span of -1280 means that the scan of the image found no pixel of the sill at all, not a sill of the wrong size. The class passed 3 of 3 runs alone right after, on the same build. The change under test (a refactor of the Demo's cabin spawn) does not touch the spacewalk scene.

Wanted: find what leaves the capture without the sill (a capture before the scene draws, a readback of a stale image, or a cut that the exposure has not settled), and make the test wait for it or report the image when it finds no sill.

