---
id: tiunr-zoec
status: open
deps: []
links: [tiunr-4b2g]
created: 2026-11-08T03:37:16Z
type: task
priority: 3
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# No GPU test runs the single-diffuse-history prototype

Found 2026-11-08 in the mutation run of tiunr-4b2g. The opt-in prototype TERTIUS_INDIRECT_LIGHT_PROTOTYPE=1 selects accumulate_diffuse_single_temporal.comp and accumulate_diffuse_single_spatial.comp. Only IndirectLightPrototypeTests covers it, and those tests run on the CPU and check the selection alone. Mutant S21 of tiunr-4b2g (the prototype reads the sample place instead of readPlace) survived for this reason. If the prototype stays, add a GPU test of a still scene under FSR jitter in which the prototype's history reads its own texel. If the research drops the prototype, close this ticket with it.

