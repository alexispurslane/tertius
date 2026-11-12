---
id: ter-94uo
status: open
deps: [ter-libf]
links: []
created: 2026-11-12T01:50:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# Serverless image, job handler and the queue script with its caps and audit log

Build the serverless image from the bring-up's recipe, a handler that takes a source snapshot and a command list and returns logs and output files, and the one queue script that agents call. The queue script holds the key, enforces max workers, the execution timeout, $10 a day and $30 in total, and logs every job.

