---
id: ter-nims
status: closed
deps: []
links: []
created: 2026-10-18T17:50:48Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [simulation, host, refactor]
---
# Make InputEvent a custom union, with a JSON converter that keeps the script format

Found by the survey of kind enums (2026-10-18). InputEvent (src/Tertius.Core/Messaging/EngineEvents.cs:168) carries InputKind: KeyDown and KeyUp use Code (a key), MouseDown and MouseUp use Code (a mouse button), MouseMove and Scroll use Delta. 3 branch sites (PersonControl.cs:118, Carry.cs:89 and :94). Constraints: an event payload must be unmanaged and fit EventTypeRegistry.MaxPayloadSize; scripts and recordings serialize payloads with System.Text.Json (IncludeFields, camelCase, UnmappedMemberHandling = Disallow), and ten files in scripts/ plus ScriptRecorder.cs:48 use {"kind":...,"code":...} and {"kind":...,"delta":[...]}. So the union needs a custom JsonConverter that keeps those keys, and the cases keep int codes because Core has no Silk.NET reference. No change of behavior; every script must still parse and play the same.

