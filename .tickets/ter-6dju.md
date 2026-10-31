---
id: ter-6dju
status: open
deps: []
links: []
created: 2026-10-31T22:14:02Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, allocation]
---
# Detach JoltPhysicsSharp's body activation listener, so that the contact callbacks are the only managed code on Jolt's job threads

Found in the review of ter-a00h. JoltPhysicsSharp's PhysicsSystem constructor gives every system its own managed body activation listener (JPH_PhysicsSystem_SetBodyActivationListener with _bodyActivationListenerHandle, also in the 2.23.0 DLL). joltc calls it through ManagedBodyActivationListener::s_Procs, a table for the whole process, on Jolt's job threads whenever a body wakes or falls asleep. Tertius replaces only the contact listener (FrameWorld.CreateSystem, listener.Attach). No code in src or tests subscribes to OnBodyActivated or OnBodyDeactivated, so each activation is a call into C# that does nothing. StepAllocations (tests/Tertius.Testing) counts only the three contact callbacks on Jolt's job threads, so an allocation in an activation callback, for example after a JoltPhysicsSharp upgrade, would not count. The fix is a production change: in FrameWorld.CreateSystem, beside listener.Attach, detach the activation listener (JPH_PhysicsSystem_SetBodyActivationListener with null). Then the contact callbacks are the only managed code on Jolt's threads, and the doc of StepAllocations can drop its exception. Make sure that no Jolt behavior depends on the listener first.

