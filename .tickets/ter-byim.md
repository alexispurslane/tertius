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

**2026-11-12T18:18:23Z**

2026-11-12, from ter-s97m's hardening: the mutant M11 of ter-s97m (the shared pool's sampled images per frame set 6 -> 5) survived on MoltenVK. The frame set takes 7 sampled images (three tables, the specular map, the irradiance cube, the distance cube and the blue-noise mask), and the pool held 6 per frame; on main before ter-s97m it took 6 and the pool held 5. The constant 20 of the G-buffer, post and atlas sets appears to hold enough spare images to hide that, also on a driver that counts each type. ter-s97m added DescriptorLayoutsTests.EachFrameInFlightGrowsTheSharedPoolByWhatItsFrameSetAndDeformSetTake, which pins only the per-frame part of each size (pool at 3 frames minus pool at 2 = frame set + deform set). This ticket's ledger still has to cover the other terms, the 20 among them.

**2026-11-14T16:42:09Z**

2026-11-14, evidence from the face pass's merge of main (sweep-pw52): the face pass and key and fill each raised the shared pool's hand-kept maxSets from 10 to 11 for one new set each; git saw two equal edits and kept 11, although the sum is 12. The sampled-image count needed the same hand sum (now 20 + 1 + 4). A count derived from the set layouts cannot drift in a merge this way.

**2026-11-14, the count after that merge**

The face branch set the count of sets to 12 by hand, in its merge commit a82ed9aa2. The listed mutant C04 of ticket t2-zcr4 sets it back to 11, and `EnvironmentFaceTests` passes on it.

The reason is a spare set. Eleven sets exist once per renderer, and each has one call that allocates it: the G-buffer, post, atlas, face surface, meter, exposure, prefilter, scatter, shadow bounds, key fill tiles and light sets. So the constant of 12 holds one set more than a renderer takes. Main had the same spare before both merges: a constant of 10 for nine such sets.

So the 11 that git kept was enough by this count, and the 12 keeps the spare of the base. A count that comes from the set layouts settles whether that spare has a reason.
