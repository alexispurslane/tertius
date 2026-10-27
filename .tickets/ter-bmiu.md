---
id: ter-bmiu
status: closed
deps: []
links: []
created: 2026-10-27T10:28:13Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-jwzd
tags: [content]
---
# Content formats: the attitude of an orbit, with a facing case

KeplerOrbit holds its attitude as the union KeplerAttitude (ter-7oi8): the case Spin(AxialTilt, Rate, PhaseAtEpoch) or the case FacesParent, and an orbit without an attitude is not valid. The orbit format of docs/plans/content-formats.md ("Frames on rails") still has the flat keys axialTilt, spinRate and spinPhaseAtEpoch in the kind "elements", and spinRate in the kind "circular". These keys build a Spin, so a scene file cannot give an orbit the facing case.

Decide the shape of the attitude in the file format (the user's choice), for example a field "attitude" with its own "kind" ("spin" with tilt, rate and phaseAtEpoch, or "facesParent"), or keep the flat spin keys and add a key for the facing case. Then update the format, its example scene and the content loader plan.


## Notes

**2026-10-27T11:52:15Z**

2026-10-27: the user chose the nested attitude: a field "attitude" with its own "kind": {"kind": "spin", "tilt", "rate", "phaseAtEpoch"} or {"kind": "facesParent"}, matching the union KeplerAttitude. The flat keys axialTilt, spinRate and spinPhaseAtEpoch go. An orbit without an attitude is refused, as KeplerOrbit.IsValid does.

**2026-10-27T11:56:07Z**

2026-10-27: merged (18766238), docs only: docs/plans/content-formats.md, the orbit format and its example scene. No other plan names the orbit keys.
