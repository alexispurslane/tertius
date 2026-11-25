---
id: tf3-t9wo
status: closed
deps: []
links: []
created: 2026-11-24T17:16:21Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, flaky]
---
# The curved-mirror edge test failed once in a full Debug coverage run of Rendering and passed three times alone

**2026-11-25 · Retired.** This test or host failure was reported under load or in an isolated run. Keep its log. A repeat on the current build with the GPU reserved warrants a new regression ticket.

Found during ter-fe63's coverage run, 2026-11-24, on branch t3/adaptive-reflection-ray-guiding at 945f36790.

EnvironmentLightingTests.ACurvedMirrorCutByTheScreensEdgeShowsTheStarsMeanInItsFirstAndLastColumnsAndRows failed once in a full Debug run of tests/Tertius.Rendering.Tests with --coverage (Microsoft code coverage), at a host load of about 5 to 11: 'the first pixel that differs is (0, 0)'. It passed in the full Checked and Release suites on the same commit, and three times alone in Debug.

The test's mirrors sit below the reflection share's roughness ramp (start 0.1), so ter-fe63's share does not apply to them.

Do: rerun the test 20 to 50 times alone and several times in the full Debug suite with and without coverage, find the cause of the difference at pixel (0, 0) (a race between two passes, or a reading of an image that a pass did not write), and fix it.

