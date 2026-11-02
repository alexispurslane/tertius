---
id: ter-16vv
status: open
deps: []
links: [ter-pywg]
created: 2026-11-02T23:28:56Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, tests]
---
# A slab entry exactly at the segment's end: the march meets the pixel there, the trace stops before it tests the pixel

Found on 2026-11-02 in ter-pywg's hardening, on the branch ter-pywg-edge-tie at 17bebacb1, with no change to src.

The trace and the march disagree when the ray reaches a pixel's slab exactly at the segment's end, s = End. MeetPixel clamps a pixel's stretch to End, and frontOut = 0 (or backOut = 0) counts as a meeting, so the march hits the pixel there. ScreenRay.Trace returns RayLeftTheSegment when s >= End at the top of its loop. So after a descent to the slab entry s = End, the walk stops before its level-0 test of that pixel. By a reading of the diff, ter-pywg did not change this check, so the base has it too. traceRay's loop condition s < ray.end is the GLSL twin.

Reproduction on a 16 x 16 sky with one surface at pixel (15, 14) of inverse depth 1.0625 (DepthPyramid.FromInverseDepths): new ScreenRay(Start: (12.25, 14.5), Step: (2, 0), StartInverseDepth: 1.5, StepInverseDepth: -0.25, End: 1.75, EndsAt: ScreenEdge). Every number is a binary fraction, so the tie is exact: w(1.75) = 1.0625. ScreenRayTests.March gives (15, 14, 0), and MeetPixel of (15, 14) gives the position (15.75, 14.5) with q = 0. Trace gives RayLeftTheSegment { End = ScreenEdge, Steps = 2 }.

ter-pywg's property test OnRaysOfBinaryFractionsWhoseEntriesAndCrossingsTieWithPixelEdgesTheTraceFindsThePixelThatTheMarchFinds (ScreenRayTests.RandomRays) found 48 such rays in 200,000 when End also lay on quarter steps. With End = k / 4 + 1 / 1024 it finds 0, so the test keeps End off the grid until this ticket decides. To decide: whether the segment's end point belongs to the segment (then the trace tests the pixel of an entry at End, in C# and GLSL alike), or the level-0 test counts a meeting only before End (then the march and MeetPixel change). Either way the reproduction becomes a test, and the property test takes End back onto the grid.

