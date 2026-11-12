---
id: t8-pkag
status: open
deps: [ter-net6, ter-j4h9, ter-qjyo, ter-8esk, t8-kpoe, t8-en95, t8-6qne, t8-sjli, t8-ol7x, t8-xp96, t8-1gk3]
links: [ter-net6, ter-j4h9, ter-qjyo, ter-8esk, t8-kpoe, t8-en95, t8-6qne, t8-sjli, t8-ol7x, t8-xp96, t8-1gk3]
created: 2026-11-12T21:17:43Z
type: epic
priority: 2
assignee: Alexis Purslane
---
# The test suites pass on the Linux card: Host, Rendering and Slop on the RTX 2000 Ada

Make the Checked suites pass on the cloud's RTX 2000 Ada worker (x86-64, NVIDIA 595.91.07, 16 GB). Each failure gets a ticket that records its kind: (a) a value that one machine recorded, which may differ across machines and architectures (memory determinism-per-computer, ter-zkdz), or (b) a real fault on a regular Vulkan driver or on Linux. Members: ter-net6, ter-j4h9, ter-qjyo, ter-8esk and the tickets of the other Rendering failures and of the Slop failure.

