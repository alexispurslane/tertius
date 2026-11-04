---
id: ter-byim
status: open
deps: []
links: [ter-cikj]
created: 2026-11-04T13:06:07Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, portability]
---
# Derive each descriptor pool's sizes from its set layouts, and refuse a set past a type's size

Found on 2026-11-04 by the review of ter-cikj. DescriptorLayouts.CreatePools keeps maxSets and six per-type descriptor counts of the shared pool as separate hand-kept sums of one list of sets, and its comments list those sets again for each type. ter-cikj made each pool refuse a set past its maxSets itself, because the spec says an allocation past maxSets or past a type's size 'may fail' (vkAllocateDescriptorSets). The per-type half is still left to the driver: on 2026-11-04 both MoltenVK and KosmicKrisp gave EnvironmentMapPlumbingTests 61 prefilter sets, more than the shared pool's size of samplers. So a miscounted type size passes on both Mac drivers and can fail on a driver that counts types.

Scope: derive maxSets and the size of each descriptor type of each pool from one list of (set layout, count), read from the bindings of the layouts. Let Allocate refuse a set past a type's size from the same ledger, so that every driver refuses at the same point. Design it twice first: this changes how CreatePools is written.


## Notes

**2026-11-04T13:37:55Z**

2026-11-04: from ter-cikj's hardening. The mutants C04 and C07 of ter-cikj (a pool that gives one set past its maxSets) survive on both Mac drivers, because EnvironmentMapPlumbingTests fills the pool until any refusal. When this ticket's ledger makes every driver refuse at one point, a test can pin the exact count of sets that fit, and that test kills them.
