---
id: t8y-3mbv
status: open
deps: []
links: []
created: 2026-11-12T18:04:49Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-vy3m
---
# Two endpoints of one kind and image after a timed-out create, and concurrent pushes of one hidden ref

Review of t8-yd4j. (1) With a random name per creation, a create_endpoint call that times out after Runpod made the endpoint lets the next job create a second one of the same kind and image; route keeps one id per image, and step 1 never deletes the other, so it holds quota until removed by hand. Delete idle duplicates of the best image, keeping the one with the most workers. (2) cloud.py's three gpu_job.py processes push the same refs/gpu-jobs ref at about the same time, outside the lock; a GitHub lock error would reject one module's job. Treat a push failure as success when ls-remote shows the ref at that commit.

