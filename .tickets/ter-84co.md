---
id: ter-84co
status: closed
deps: [ter-dtw2]
links: []
created: 2026-10-11T14:29:38Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics]
---
# A character's contact starts only when Jolt finds a contact for its inner body

Round eight's review: a character standing on a box, a ball that shoves a character, and a slow push are never reported, because the character's 0.02 m padding keeps the inner-body contact at the edge of Jolt's speculative distance. Start a held pair from the character's own contact list too.

