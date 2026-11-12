---
id: ter-k7fd
status: in_progress
deps: [ter-a5ev]
links: []
created: 2026-11-12T14:32:51Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# GPU jobs fetch their commits from GitHub, and each dev shell gets its own endpoint

Alexis's design of 2026-11-12. A job names its trees' commits: the queue script scans the commits that GitHub lacks for secrets, pushes each to a hidden ref, and the worker fetches it from the public repository without docs/ and .tickets/. The image changes only with the dev shell (flake.nix, flake.lock, native/) or the worker's own code. A job runs only on an endpoint whose image has the dev shell of its commits; the script creates that endpoint from an existing image, or refuses and names the missing image. Runpod's list of endpoints is the one store of the live endpoints. An endpoint with no job in flight that no job needs is deleted. GitHub is now origin's fetch remote, and the knot a push mirror.
