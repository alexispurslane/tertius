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


## Notes

**2026-11-02T02:59:44Z**

2026-11-01: research done (delegated, GPT 6.1 SOL, high), condensed in docs/research/remote-runners-2026-11-01.md with its sources. Main results: the cheapest serious option is a Hetzner CCX53 per run (32 dedicated vCPU, $1.02 an hour, about $122 a month at 4 hours a day); a persistent Hetzner AX102 costs about $302 a month; exe.dev about $264 a month at the same use; a Linux workstation of the user's own costs about $82 a month over 36 months and can also hold a real Vulkan GPU, which no rented CPU VM offers. Six test projects can move first (Core, Simulation, Physics, Unions, Style, Slop); Rendering and Host stay on the Mac until a filter separates their CPU tests. The research also found Stryker.NET issue #3832 (wrong kills under MTP at a concurrency above 1), which ter-hxca's continuation now handles. Next: two designs for the user, then a small pilot after the user approves a provider. No account exists and no code left the Mac.
