---
id: t2-lcmo
status: open
deps: []
links: []
created: 2026-11-12T22:18:30Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance, lights]
---
# Key and fill: the pixels with no corner on their surface shade every lamp in full, about 0.19 ms of the deck's direct pass

Found in t2-vsqh (ARCHITECTURE.md, 'Key and fill'). A pixel counts only the corners of its tile that lie on its own surface: near its plane, and with a normal within about 26 degrees (FacingFloor = 0.9). Where the summed weight falls under 1/16 (TrustedWeight), the pixel takes an open share of every lamp's full response. This repaired the lost highlights of thin edges. On the Apple M1 Max it costs 0.19 ms of the deck's direct pass: 3.07 ms without the test of the normal and the open share, 3.24 ms with them. The likely cause is that the opened pixels shade every lamp in full, and it is not measured which pixels those are. Ideas: count the opened pixels of the deck first. Then try a lower FacingFloor, a lower TrustedWeight, or a corner sample that prefers the surface that covers most of its tile. Each needs the image check beside the reference (deck sills, hall pillars) and the register limit of r115.

## Acceptance Criteria

The share of opened pixels on the deck is known. Either a variant gains at least 0.1 ms of the deck's direct pass on the Mac with no visible change beside every light pair, or the ticket closes with the measurement.

