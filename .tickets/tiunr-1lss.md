---
id: tiunr-1lss
status: open
deps: []
links: [tiunr-5jqs]
created: 2026-11-08T18:19:55Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# The plate's wide glow from rays read about 2.5 times the tube light's direct light

Found 2026-11-08 by tiunr-5jqs. Before the bar held a tube light, the capture plate's wide glow came from screen-tracer rays that hit the emissive bar. After, it comes from the tube light's direct diffuse and specular, and reads about 40% of the old level (videos in /private/tmp/tertius-tube-light-20261108/videos/, frames *-t6.png). A GPU test of the tube light against the C# integral passes, so the ray path is the suspect: block rays, the history fix, spatial reuse or reconstruction may spread or add light. Both paths estimate the same integral, so they must agree. Measure the ray path against the same integral, for example with a dim emitter that holds no light, and find the excess. This matters for every surface that rays light: bounce light and reflections of lit surfaces.

