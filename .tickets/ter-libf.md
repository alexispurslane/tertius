---
id: ter-libf
status: in_progress
deps: []
links: []
created: 2026-11-12T01:50:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# Build and run the renderer on a Runpod Linux NVIDIA GPU, and record the image recipe

Nobody has built or run Tertius on Linux. Bring it up on one Runpod pod (A4000 class, Secure Cloud, about $0.25 an hour, at most $3 for this ticket): the Nix dev shell on x86_64-linux, NVIDIA's Vulkan driver file in VK_ICD_FILENAMES, the native libraries (Jolt, FSR), a headless still of the deck and one timing run with the clocks logged. Compare the frame times view by view with the Mac. Fix Linux-only failures or record them as tickets. Deliver the container recipe that the serverless image will use.

