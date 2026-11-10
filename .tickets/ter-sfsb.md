---
id: ter-sfsb
status: open
deps: []
links: []
created: 2026-11-10T13:25:05Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The reflection plate test measures no ray noise since the plate's emitter holds a tube light

Since commit 972d42f16 (2026-11-08, ticket tiunr-5jqs), the emitter of tools/Tertius.ReflectionReference's plate scene holds a tube light. An entity that holds a light sends no glow to traced rays, so the plate's reflection of the emitter comes from the tube light's analytic direct specular, which has no noise. On main the plate's reflected-light file stays near zero (at most 0.14 against 1,029 on 2026-11-07), and the temporal noise of both plates reads 0.001 W/(m² sr). So the test of 2026-11-07 (docs/plans/reflection-reconstruction-campaign.md:545 to 640) no longer measures the noise of the reflection rays, and a change to the ray plan cannot be judged by it. Found by ticket ter-4oow.

## Design

Proposed change, tried in a scratch copy for ter-4oow: give the --history captures a plate emitter that holds no light, as on 2026-11-07. Remove the line scene.Lights.Add(EmitterLight(...)) in Program.Live.cs and the update scene.Lights[0] = EmitterLight(...) in Program.History.cs for these captures, or add an option that selects the glowing emitter. The scratch copy reproduced main's rough plate of 2026-11-07 exactly: noise 5.529, error 29.022; smooth noise 0.998, error 51.44. Keep the tube-light emitter for the captures that test the tube light.

## Acceptance Criteria

--history static rough uncapped on main gives a noise of 5.5 and an error of 29.0 W/(m² sr) again, and the smooth plate 1.0 and 51.4.

