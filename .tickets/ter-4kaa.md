---
id: ter-4kaa
status: closed
deps: []
links: []
created: 2026-10-17T00:20:12Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, contacts]
---
# A body that lands from within Jolt's speculative distance bounces and lands twice when the pair's restitution is above zero

From ter-ediu: when a landing step starts within Jolt's 2 cm speculative contact distance and the pair's restitution is above zero (Jolt takes the larger of the pair, and the default floor has 0.1), Jolt bounces the body from where it is, a documented artifact. The body ends the step outside the 2 cm and lands a second time, so Collision and CollisionEnded report two landings. It happens with or without the instant velocity rule, at different drop heights. Contact counts that no test asserts moved in about ten scenes when ter-ediu changed where landing steps start (998 to 1031 contacts in all). Decide the fix, for example a lower default restitution, a minimum bounce speed, or a hold that merges a start within a few ticks of its own end.


## Notes

**2026-10-17T01:25:16Z**

Decided with the user (2026-10-17), from two designs: (1) A minimum rebound speed. Both contact callbacks set a contact's combined restitution to 0 when restitution times the approach speed is below 1 m/s, so every bounce that remains rises at least 5 cm and leaves Jolt's 2 cm speculative band, and a drop reports the same number of landings wherever its landing step begins. Ends still arrive at once. Rejected: merging an end and a restart within 0.25 s, which delays every end and still varies for some heights. (2) The default restitution of a body becomes 0, as in Jolt, so default content never bounces and costs nothing in the check.

**2026-10-17T05:13:32Z**

Merged with the contact sweep. After the review, the rule computes the rebound that Jolt will produce, u = e * (approach - a * dt), at the fastest contact point, and keeps the restitution when nothing pulls the bodies together (a <= 0) or when the bounce rises at least 4 cm, twice Jolt's speculative distance (u^2 >= 2 * a * 0.04). At 1 g that is about 0.89 m/s. A fixed 1 m/s failed above about 1.5 g and removed bounces in zero gravity. Follow-up: ter-h78w.
