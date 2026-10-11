---
id: ter-spka
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T13:52:11Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Blueprints.Coerce rounds a fractional value for an integer field

A value of 1.5 silently becomes 2. Reject a fractional value instead.

