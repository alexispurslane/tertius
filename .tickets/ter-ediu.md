---
id: ter-ediu
status: closed
deps: []
links: []
created: 2026-10-16T23:00:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics]
---
# Give Jolt chord velocities for every velocity that gameplay sets, spawns or hands off

Design: docs/plans/spin-gravity.md. Section 'The velocity convention'. A velocity that gameplay sets is an instant velocity; give Jolt the chord (the instant velocity minus the body's acceleration times half a tick), and the same for angular velocity. A thrown crate then lands where the instant velocity says, with or without spin. Seven existing tests encode Jolt's convention and change on purpose, each with its reason; the spike also reported each box's landing twice in two ContactTests, which is a bug of the conversion and must not happen.

