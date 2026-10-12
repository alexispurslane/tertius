---
id: ter-swgm
status: open
deps: []
links: []
created: 2026-10-12T13:00:57Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [physics, core, rendering, slice]
---
# A frame's rotation from Jolt is not unit length, which puts objects in a parent frame off by about a meter

Found by two investigators of the user's report: a crate thrown out of the ship jumped up and down by 20 to 25 px per tick at about 30 m.

Cause: Geometry.ToDouble (src/Tertius.Physics/Internal/Geometry.cs:21) widens Jolt's float rotation to double without normalizing it, so |q|² - 1 is between -7.3e-8 and +2.1e-7, a new value every tick. The ship's hull rotation becomes the ship frame's local pose in earth (ReadPrimary, WriteFrames, World.Sync SetLocalPose). DPose.Inverse and Compose (src/Tertius.Core/Math/Pose.cs:65-73) rotate positions with that rotation as given, while DQuaternion.Rotate assumes unit length. PoseTree.ComputeRelativeTo (src/Tertius.Core/Trees/PoseTree.cs:264), walking up from the ship to earth, then puts earth off by eps·(R⁻¹S - S), about 1 to 2 m with |S| = 6.77e6 m, mostly along the ship's +Y. Every object in earth is off by that much in a camera in the ship's frame. PoseTree.Relative and Snapshot.TryGetPoseIn have the same mismatch.

It shows when the camera is in the ship's frame and the object is in earth: just after a thrown crate crosses its hand-off point at 102.35 m while the person is still within 102.9 m. A camera in earth sees no error. Verified: normalizing in ToDouble alone takes the error from 0.82 m rms (1.95 m max) to 1e-5 m, and the rendered crate moves smoothly through the hand-off.

Fix: normalize where rotations leave Jolt, make DPose's Compose and Inverse consistent for a rotation that is not unit length, make unit-length rotations an invariant of the pose tree's local poses, and add regression tests (snapshot positions and TryGetPoseIn against a double-precision truth, camera in the ship and object in earth).

## Acceptance Criteria

With the camera in the ship's frame and a crate in earth 20 to 50 m away, the crate's snapshot position equals the double-precision truth within 1e-4 m on every tick, and so does Snapshot.TryGetPoseIn.

