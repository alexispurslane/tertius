---
id: ter-3o6h
status: open
deps: []
links: []
created: 2026-10-21T00:24:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [demo]
---
# A rendezvous plan whose window closes within 2 degrees of 0 starts its transfer already in the coast

Found by ter-lz8u. RendezvousPlan still requires WindowCloses above KeplerOrbit.LambertRefusedAngle (1 degree), but the autopilot now coasts within TransferGuidance.CoastAngle (2 degrees) of the arrival point. A window that closes between 1 and 2 degrees starts the transfer inside the coast, so the shuttle never steers. It no longer throws. Raise the guard to CoastAngle, with a test that refuses such a plan. The demo's window closes at 150 to 170 degrees, so the demo is not affected. Demo work, no engine change.

