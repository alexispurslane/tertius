---
id: ter-k7fd
status: closed
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

## Notes

**2026-11-12T18:10:16Z**

Merged on main as 51f2d5e18, with two hotfixes for Runpod's account quota of 10 workers summed over all endpoints' maximums: 16c38d5b4 (create a new endpoint with the room that the quota leaves, grow it later) and 51c162ab9 (free idle endpoints of old images before the quota is counted, and fall back to a busy endpoint of a fitting image). The example job ran on commit 16c38d5b4: a Release demo build in 47 s and 51 s of execution, counted at $0.019, after 1,125 s in the queue for an RTX 2000 Ada. t8-yd4j then added the CPU kind on the same design.
