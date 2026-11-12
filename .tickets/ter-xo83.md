---
id: ter-xo83
status: open
deps: []
links: []
created: 2026-11-12T10:23:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Reflection resolve: each shared ray counts with the pixel's own density (fewer daubs near contacts)

Sweep addendum A7 variant ownw1 (/private/tmp/tertius-sweep-20261111/addendum.md). Main's one-block resolve divides each shared ray by the density of the pixel that traced it; ownw1 uses the receiving pixel's own lobe density for the ray's direction. On flat surfaces nothing changes (deck floor equal to two decimals; paint and plain floor equal). Near a contact (roughness 0.1) the daub score after 60 draws falls 0.0261 -> 0.0104 and grain 1.69 % -> 1.23 %. Cost: one density evaluation per shared ray, not yet timed. Image change, so stills and a film against main; the mottle agent (t7-3e22) owns the reflections' clamp, so coordinate. Proposed for the user's morning list.

