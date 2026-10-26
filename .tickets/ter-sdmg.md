---
id: ter-sdmg
status: in_progress
deps: [ter-q218]
links: []
created: 2026-10-26T13:03:44Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
tags: [assets, experiment]
---
# Try a human in a spacesuit as a scripted model, with the procedural modeling skill

An experiment that the user asked for on 2026-10-26: after ter-q218 merges, one background agent (Opus, high effort) follows .claude/skills/procedural-modeling/SKILL.md and tries to build a human in a spacesuit as a scripted model. It may download Blender add-ons that generate geometry from parameters, such as a human or face generator, from their official sources, pinned by version and hash so that the build stays reproducible. It may not use a finished model of a human, a suit or a helmet. The user asked it to be persistent and really try, and also said that a failure is fine: an honest report of how far it got, with renders, is a good result.

