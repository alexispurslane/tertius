---
id: ter-wnrh
status: closed
deps: []
links: []
created: 2026-10-20T19:54:34Z
type: chore
priority: 2
assignee: Alexis Purslane
tags: [docs]
---
# ARCHITECTURE.md: the paragraph on intents in Ownership of transforms contradicts the sync point's steps 5 and 7b

ARCHITECTURE.md, section "Ownership of transforms", the paragraph that begins "An intent is relative to self.Frame" (near line 146), says that the sync point converts an intent with the frames' relation at the end of the tick, and that each case misses by how far the two frames move relative to each other in one step. Section "Rails components and motion intents", steps 5 and 7b, and the code (World.ConvertMovedIntents in World.Sync.cs, FrameTree.MotionRelativeInStep, LevelInStep) convert by the relation at the end of the step that applies the intent, with a kinematic hull's own end pose. The engine test AnEndPoseThatConvertsAtAHandOffOutOfAMovingFrameConvertsByWhereThatFrameEndsTheStep pins the code. Rewrite the paragraph to match the code. Found by the live world design (ter-0pvs).

