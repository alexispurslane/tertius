---
id: t0-gbgj
status: open
deps: [t0-5tyj]
links: []
created: 2026-11-22T11:15:34Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# The combined check of Design X with the back-face layer: the line's head merged with the new main, in the cabin, rect-lamps, the bay film and the deck's frame time

From the parent orchestrator, 2026-11-22. The back-face line (branch ter-kh4g-back-face-depth-layer, a8e34541a) is finished, and its merge into main waits for the user's decision. It changes the hit decision of the screen walk, not the accumulation. Its own takes show rect-lamps the same within 1 %, the deck at the noise floor, and the cabin's window frames brighter by 20 of 255 levels. Its cost is +0.18 to +0.27 ms in rooms on the M1 Max and +0.75 ms in the crowd.

The acceptance record of Design X (t0-5tyj) compares the line's head with main c4c1f1f2f, and it stays valid as the record of Design X against its base. This ticket is one short check for the time after both merges are decided, not a second full record.

Start it only when the parent orchestrator says that main moved.

The check: merge the new main into the line's branch. On that merged head, against the new main:
- The still rect-lamps and the switch cabin (the room of scene orbit with the camera person), with the still's specks, blotch and daub and the switch's draws to settle.
- The film rect-bay: level, last-frame specks, flicker and lag error.
- The deck's frame span, three rounds in turn at a low load. The two allowances add: Design X's 0.30 ms and the layer's own cost.
- The Checked suite and then the Release suite.
Pictures first, each beside the new main. A take where the merged head is worse than the record of t0-5tyj says so plainly.

Acceptance: a short record in docs/research with the four takes and both suites, and no new fault by the user's bar against the new main.

