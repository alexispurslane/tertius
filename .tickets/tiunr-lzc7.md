---
id: tiunr-lzc7
status: closed
deps: []
links: [tiunr-okzm]
created: 2026-11-08T13:58:45Z
type: task
priority: 3
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# After a dimming and a brightening back, an emitter's border ring can keep a stale light-age record

**2026-11-25 · Research archived.** Preserve the candidate and its controls in the reflection-reconstruction study. Reconsider it only if the signal isolated on the filmed berth matches this experiment (ter-xzj7).

Predicted from the own-band rule of tiunr-okzm (screen_trace_plan.comp lightAgeOf), not measured. A light inside the 3 by 3 range but outside the pixel's own 2% and floor band keeps the last light, with no pending count. An emitter's border pixel has floor texels in its range, so after a 20% dimming it keeps the old light without a change. When the emitter brightens back, the first inner ring finds the border's stale light in its range and keeps the dimmed light. The ring then never commits either change, and receivers whose rays hit it keep stale light. Test: dim an emitter by 20% for 60 draws, brighten it back, and count border and first-ring pixels whose record light differs from their exact light by more than 2%.

