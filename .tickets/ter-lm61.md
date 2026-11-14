---
id: ter-lm61
status: closed
deps: [ter-2zga]
links: []
created: 2026-11-12T07:03:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Bloom as one chain of compute dispatches: 0.13 to 0.20 ms with the patched MoltenVK

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 4. Bloom runs 11 render passes (one per level down and up). As a compute chain it joins one compute encoder with the patched MoltenVK; on stock it gains nothing, so it depends on the patched MoltenVK ticket.


## Notes

**2026-11-12T19:26:56Z**

Built and measured on 2026-11-12, on main c87eac88f. The result is no gain, so the build stays out of main.

- Time, Mac, take 9 under the exclusive GPU lock: the frame span as a window is 19.930 ms against 19.938 ms. With the pass time stamps it is 20.339 ms against 20.253 ms. Both differences are inside the spread of two runs of one build.
- The bloom pays 0.370 ms against 0.435 ms: its work rose from 0.301 to 0.350 ms, and the idle time before its encoders fell from 0.133 to 0.020 ms. The tone map's idle time rose by 0.04 ms.
- The reason: the breaks before the bloom's render encoders cost about 12 µs each, and a small dispatch costs 10 to 18 µs at any size.
- Image: 139 of 139 stills of 25 scripts are the same as main's, byte for byte.
- Kept on main: the test EveryBloomLevelHoldsTheDesignsChainOverTheHdrImage with BloomChainModel, which compares every bloom level with a CPU model of the chain. It passes on the render-pass bloom.
- The record and the patch: docs/research/frame-time-sweep-2026-11-11/bloom-compute-ter-lm61/. The addendum's section A12 holds the lesson for the other encoder proposals.

Closed without a merge of the build.
