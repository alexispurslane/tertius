---
id: ter-48qn
status: in_progress
deps: [ter-zzyr, ter-63wr]
links: []
created: 2026-10-25T14:21:50Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Make BloomSetting a [GeneratedUnion] of record structs, like the other renderer settings

Bloom (ter-63wr) built its setting as a built-in union of sealed record classes, with checks in the constructors and an empty default that the renderer refuses. The exposure, SSAO and SSR settings use the form that STYLE.md's table gives for struct cases: a [GeneratedUnion] of positional readonly record structs, with a valid zero case (the off case) and a Problem check that CheckOptions calls. Settled by the orchestrator on 2026-10-25 so that the epic's settings share one form. Change BloomSetting.cs, RendererOptions.cs, CheckOptions and BloomSettingTests to that form, with NoBloom as the zero case. Starts after the exposure ticket (ter-zzyr) merges, so that its setting is the model on main.

## Acceptance Criteria

BloomSetting is a [GeneratedUnion] with NoBloom as its zero case and a Problem check, every bloom test passes, and the look does not change.

