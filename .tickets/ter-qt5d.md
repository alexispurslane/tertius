---
id: ter-qt5d
status: open
deps: [ter-q218]
links: []
created: 2026-10-26T12:43:56Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
tags: [assets]
---
# Where third-party binary sources live, now that the knot has no Git LFS

Decision 21 of docs/plans/asset-pipeline.md put binary source files (texture images, .glb downloads from Poly Haven) in Git LFS. The knot has no LFS, so ter-q218 replaced decision 21 with 'no binary build output goes into git' and left the third-party sources open. Choose with the user: plain git with a size limit, a fetch from the provenance records at import, or another remote. Procedural models need none of this, because their scripts are the only source.


## Notes

**2026-10-26T12:47:09Z**

2026-10-26, user: fetch from provenance. Git keeps each third-party binary source's provenance record (URL, license, hash); the importer downloads it on the first build, checks the hash and caches it under bin/assets. Record this in docs/plans/asset-pipeline.md beside decision 21 after ter-q218 merges, because ter-q218 rewrites decision 21.
