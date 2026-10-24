---
id: ter-7oi8
status: open
deps: [ter-c3rf]
links: []
created: 2026-10-22T12:58:26Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, rails]
---
# Kepler attitude as a union

KeplerOrbit keeps AxialTilt, SpinRate and SpinPhaseAtEpoch beside Attitude, and they mean something only for KeplerAttitude.Spin (docs/plans/union-tooling.md, section Closed sets that are not unions yet). Make the attitude a union of a spin case and a facing case.


## Design sketch (2026-10-24)

```csharp
// Sketch
public readonly record struct Spin(DQuaternion AxialTilt, double Rate, double PhaseAtEpoch);
public readonly record struct FacesParent;
[GeneratedUnion(typeof(Spin), typeof(FacesParent))]
public readonly partial struct KeplerAttitude;

public record struct KeplerOrbit(double SemiMajorAxis, double Eccentricity, double Inclination,
    double LongitudeOfAscendingNode, double ArgumentOfPeriapsis, double MeanAnomalyAtEpoch, double Epoch,
    KeplerAttitude Attitude);
```

The enum KeplerAttitude goes away. Only C# code in Demo builds orbits today, so no content JSON changes.

Fork to decide when the ticket starts: what the empty attitude means. Today the default orbit has Spin with a zero quaternion as its tilt, which is not a rotation. Options: a ZeroCase that means no spin at the identity tilt, or no ZeroCase, so that an orbit without an attitude fails at its first read.

## Notes

**2026-10-24T19:39:05Z**

The user approved the design sketch above as it stands on 2026-10-24. Build from it. The forks that it lists stay open: design each one twice when the ticket starts, and let the user choose.
