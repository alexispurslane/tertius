# Spin gravity

Epic: `ter-s9m0`.

A station can be a semantic frame that spins. Characters and objects inside it then stand, walk, fall, throw and jump correctly, because physics applies the forces of a turning frame. The same forces act in every frame that turns, such as a ship that turns with its orbit.

The spike on the branch `spike-spin-gravity` showed that Jolt handles this. Its numbers come from a station of radius 100 m that spins at 0.31321 rad/s, which gives 9.81 m/s² at the floor. A Sonnet reader recomputed every expected value from straight-line motion in inertial space.

## Terms

| Term | Meaning |
|---|---|
| Turning frame | A frame whose axes turn relative to inertial space: a Spin frame, a Kepler frame that faces its parent, or a frame that physics drives and that turns |
| Inertial angular velocity | A frame's angular velocity relative to inertial space, summed along its chain of frames, in the frame's axes |
| Rotation terms | The accelerations that a turning frame adds to a body in it: Coriolis, centrifugal and Euler |
| Chord velocity | Jolt's meaning of a stored velocity: the displacement over the last tick, divided by the tick. It is the velocity half a tick earlier. |
| Instant velocity | The velocity at one moment. A velocity that gameplay sets has this meaning. |

## Decisions

The user made the decisions marked (User).

| Question | Decision |
|---|---|
| Which frames get the rotation terms | Every turning frame (User) |
| Jolt's velocity convention | The engine corrects it everywhere. A velocity that gameplay sets or spawns is an instant velocity, and the engine gives Jolt the matching chord velocity. (User) |
| Tidal gravity in semantic frames | Unchanged: a semantic frame still applies no tidal gravity to its contents |

The project scope rule "Only the linear (proper) acceleration of a frame acts on its contents" changes with this epic: the rotation terms act too.

## The rotation terms

Take a body at position `r` relative to a turning frame's origin, with velocity `v` relative to the frame. Besides gravity and the frame's linear proper acceleration, it gets this acceleration:

```
a = −2 ω × v − ω × (ω × r) − α × r
```

where

- `ω` = the frame's inertial angular velocity.
- `α` = the frame's angular acceleration, zero for a steady spin.
- `−2 ω × v` = the Coriolis term.
- `−ω × (ω × r)` = the centrifugal term.
- `−α × r` = the Euler term.

Jolt stores velocity as a chord, so the first-order terms above leave a free body slightly off its inertial line. The spike used the exact update of a free body in a turning frame instead. With `R` the rotation by `−|ω| dt` about `ω`, the velocity after one step is `R² v − (R − I)² r / dt − α × r dt`. Physics computes it in doubles, adds the field, narrows once, and applies the change with the gravity force. With it, a dropped crate landed at 0.5607786 s and 0.176458 m antispinward, as the inertial line says, to the last digit.

The frame tree sums the inertial angular velocity along each frame's chain in doubles. The angular acceleration is the change between steps. A change of a frame's angular velocity wakes its sleeping bodies, as a change of its proper acceleration does today. In the spike, sleepers then stayed at rest in inertial space while the station spun up around them, and the Euler force matched to 0.01%.

Jolt applies a contact's velocity change in the frame's axes at the start of the step. But the chord is in the axes at the end of the step. So physics turns that change by the previous step's rotation. Without this, a resting body carries a small sideways force.

A body's velocity drifts by float rounding over long runs. A velocity shadow in doubles, with position feedback, kept a free body within 1 mm over 10 minutes in the spike.

Since `ter-xdt8`, the frame tree derives a frame's proper acceleration from its step record: the frame's acceleration minus the gravity that acts at its place, which is its semantic parent's field there minus the parent's own proper acceleration (see "The proper acceleration of a frame" in [Rails components](rails-components.md)). That rule has no rotation terms, because the bodies in a frame feel none yet. Once the rotation terms act on the bodies in a turning frame, they act at the place of a frame in it too. So this epic adds them to the gravity in that rule, with the child frame's place and its velocity relative to the turning parent. Physics measures a dynamic hull's proper acceleration from Jolt's change of velocity and the force that it applied, so such a hull takes the terms through that force.

## The velocity convention

A velocity that gameplay sets, at spawn, through `SetVelocity` or at a hand-off, is an instant velocity. Jolt reads a stored velocity as a chord, half a tick behind. So the engine gives Jolt the chord: the instant velocity minus the body's acceleration times half a tick, and the same for angular velocity. Without this, a thrown crate lands 2.5 cm off at 60 Hz, with or without spin. A body at rest in inertial space inside a station also drifts by up to 8 cm/s.

This changes all physics, not only spin. Seven existing tests encode Jolt's convention and change on purpose, each with its reason. Two of them also reported each box's landing twice. That was not the conversion. At the old default restitution of 0.1, Jolt bounced each box from within its speculative contact distance, so whether a box landed twice depended on where its landing step began. Since `ter-4kaa` a contact whose rebound is below 1 m/s does not bounce, and the default restitution is 0.

## Conversions between frames

The motion of a body converts between two frames at a hand-off. When the frame that a full sample or a step record is relative to changes, the sample converts (`MotionSample.Converted`). That happens at steps 5 and 7b of the sync point, where a motion intent converts with the rest of its intent (`ConvertIntent`). It also happens at step 2, where a following frame that jumps takes the record that physics released (`JumpFollowingFrame`). Both conversions use the relative motion of the two frames, `FrameMotion`, for the pose, the velocities, the angular velocity and the angular acceleration. The rails components (`ter-6r8c`) and their review left out the terms that a turning frame adds to the accelerations. This epic adds them. Each place below changes in the same way, so that the hand-off and the motion intents keep one rule.

| Place | What it converts | Terms that it leaves out |
|---|---|---|
| `FrameMotion.AngularAccelerationToParent` and `AngularAccelerationFromParent` | The angular acceleration of a hull at a hand-off, and of a sample | The cross term `ω × (R ω_body)`, whose inputs both callers have today, and the frame's angular acceleration `α`, which the bubble's step record holds at a hand-off |
| `FrameTree.AccelerationRelative` | The acceleration of the origin of one frame relative to another, level by level | At each turning level: the centrifugal term `ω × (ω × r)` and the Coriolis term `2 ω × v` of the child's origin, whose place and velocity the tree keeps, and the Euler term `α × r` |
| `MotionSample.Converted`, through `ConvertIntent` and `JumpFollowingFrame` | The acceleration of a full sample or a step record: turned, plus `AccelerationRelative` | The same three terms at the sample's place and velocity, which it has |

where

- `ω` = the angular velocity of the turning frame relative to the other frame, in the other frame's axes.
- `α` = the angular acceleration of the turning frame relative to the other frame.
- `R` = the rotation from the turning frame's axes into the other frame's axes.
- `ω_body` = the body's angular velocity in the turning frame.
- `r`, `v` = the place and the velocity of the point in the turning frame, in the other frame's axes.

The Euler terms need the angular acceleration of a semantic frame. `AccelerationRelative` also leaves out the linear acceleration of a semantic frame on its path. Only the step record of a step in flight holds them: a full sample in the owner's motion intent, or the record that physics measures for a dynamic hull. Since `ter-xdt8`, no store keeps them after the sync point. The frame tree row keeps only the record's pose, instant velocity and angular velocity, and the frame's proper acceleration, which the tree derives from the record. The decision "What the frame tree row keeps of a step record" in [Rails components](rails-components.md) gives the reason. When its design needs one, spin gravity adds its own store for them. The linear term is not a rotation term, so it needs a ticket of its own.

## Characters

- A character's velocity gets the field and the rotation terms each step.
- Its up and its standing test read the field plus the centrifugal term. Up follows that gravity in the air too. So a person who jumps across the middle of a station lands on the far floor and stands.
- A standing character is pressed along the ground normal and moves along the ground plane, so that it does not creep after a landing.

In the spike, a person stood for 600 of 600 ticks, walked both ways, and jumped and landed 7 µm from the predicted place. Walking at 6 m/s, it weighed 977.6 N spinward and 450.1 N antispinward, against 969.0 N and 442.9 N from `m (ω r ± v)² / r`.

## Numeric frames inside turning frames

A group with a body that thrusts, such as any character, gets a falling numeric frame from 50 m/s relative to the station. A group of free bodies only gets one from 1,000 m/s (see [Numeric frames](numeric-frames.md), "Who gets a numeric frame"). The frame's origin moves on its inertial line: the frame integrates it in the turning frame, with the same terms at the origin. Its members then feel only the difference. For them, the Coriolis term reduces to `−2 ω × v_local`.

One case failed in the spike and must be fixed. A person crossed the middle at 40 m/s and got a falling frame five ticks before landing. It landed 0.7 m deep in the floor and went through it. With falling frames off, it landed correctly.

## Content

- A floor that bends with the station is a mesh of at least 256 faces per ring, or smooth. On 64 faces, walking gave one-tick weight spikes of up to 6 times body weight.
- Two limits of Jolt also appear without spin. A slow rolling ball speeds up to 6 cm/s until it sleeps. A character on a seam between two faces shakes by 0.1 to 0.4 mm.

## Tests

The spike's experiments become the acceptance tests, each against the inertial line, worked out by hand:

- rest, a drop, throws and a free orbit,
- a stack, sleep and tumbling,
- a character that stands, walks and jumps,
- a spin-up, and a hand-off out of the station,
- determinism across 4 threads. A checked-in station scene with a script shows a person on the inside of the ring, and the work ends with screenshots of it.

## Order of the work

1. The velocity convention, everywhere, with the changed tests and their reasons.
2. The rotation terms for bodies in every turning frame. This includes the wake on a change of angular velocity, the contact turn, the double velocity shadow and the terms of "Conversions between frames".
3. Characters in turning frames.
4. Numeric frames inside turning frames, with the landing case.
5. The station scene, its checked-in test and screenshots, and the decisions in ARCHITECTURE.md and CLAUDE.md.
