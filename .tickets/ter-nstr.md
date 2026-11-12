---
id: ter-nstr
status: in_progress
deps: []
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Fewer GPU dispatches: one dispatch for FSR's 13 clears, and the depth and light pyramids in fewer dispatches

Sweep rows 2 and 4 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11. Exact image. Expected gain on the deck: 0.15 to 0.30 ms for the clears and 0.10 to 0.15 ms for the pyramids. Each dispatch behind a barrier is its own Metal encoder under MoltenVK 1.4.2, and each encoder boundary idles the GPU about 17 us (2.32 ms over 139 boundaries on the deck), so fewer dispatches also cut that idle time on the Mac. Images must stay bit-identical; measure the deck before and after.


## Notes

**2026-11-12T07:34:16Z**

Scope change from the orchestrator on 2026-11-12: ter-nstr builds only the FSR clears (one compute dispatch, sweep addendum A6 proposal 2). The pyramid half waits for the user's decision on the patched MoltenVK, because the patch already keeps the pyramids' 13 barriers in one encoder, so the pyramid half saves about 0 ms with the patch and 0.10 to 0.15 ms only on stock MoltenVK. It is worth building only if the user rejects the patch. Ticket te-r7nt holds it, with the design notes.
