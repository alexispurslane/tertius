---
id: ter-x51t
status: open
deps: [ter-t0bh]
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling, docs]
---
# Docs site: DocFX

Add docfx to the dev shell, a docfx.json that reads the compiled assemblies and XML files, and the Markdown docs (ARCHITECTURE.md, GUIDE.md, docs/plans). tools/docs.sh builds and can serve the site. Document it in tools/README.md.

## Acceptance Criteria

tools/docs.sh builds the site with no broken cref links; the API pages show the doc comments.

