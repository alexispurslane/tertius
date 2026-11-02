---
id: ter-fz34
status: open
deps: []
links: []
created: 2026-11-02T02:06:42Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [tools, testing]
---
# Offload the CPU-only test runs and Stryker runs to a remote machine service

The user's request of 2026-11-01, after a Stryker calibration of ter-hxca drove the 1-minute load of this 10-core Mac to 142 and 173 while three builders worked. Find a streamlined, trustworthy service (the user named exe.dev as an example) that runs the CPU-only test suites (Core, Simulation, Physics, Unions, Style, Slop, and the CPU tests of Rendering and Host) and the Stryker.NET runs of tools/stryker on rented Linux machines, so that this Mac keeps its cores for builds, GPU captures and render tests. Research first: candidates, cost, trust, start time, persistent caches (the Nix store and ~/.nuget), API or CLI control, and whether a candidate offers Linux machines with a Vulkan device (lavapipe at least) for the render tests, which would also test the portable-Vulkan rule. Then two designs for the user (for example one on-demand machine per run against one persistent machine with a queue). No code or data leaves this machine and no account is made before the user approves the service.

