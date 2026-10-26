---
id: ter-q218
status: in_progress
deps: []
links: []
created: 2026-10-26T12:01:41Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-kge6
---
# Procedural models: Blender scripts as the source, the Blender MCP and a modeling skill

The user's decisions of 2026-10-26: a model's only source is a folder of Blender Python scripts; the .blend, .glb, baked textures and views are build outputs. The importer builds a scripted model into the cooked folder (the knot has no Git LFS, so no binary goes into git). Agents model through the official Blender Lab MCP server, registered in the project's .mcp.json, with execute_blender_code. The Nostromo moves in as the first scripted model and the worked example of a project skill. Gives: the assets dev shell (Blender 5.2.2, blender-mcp 1.0.3), .mcp.json, tools/blender (the shared library, the build tool, setup), the .claude/skills/procedural-modeling skill, the Nostromo under content/tertius.demo/assets/ships/nostromo.model/, and the scripted model format in docs/plans/asset-pipeline.md.

