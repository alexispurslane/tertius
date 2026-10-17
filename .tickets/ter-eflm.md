---
id: ter-eflm
status: closed
deps: []
links: []
created: 2026-10-17T11:35:06Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [tools]
---
# Run the project in JetBrains Rider with the dev shell's SDK and libraries

Rider used an SDK that it downloaded to ~/.dotnet (11.0.100-rc.2 and 9.0.100) instead of the dev shell's 11.0.100-rc.1, and it had none of the dev shell's variables, so a build could not find the Jolt library and a test run could not find Vulkan. Rider has the hardened runtime, so macOS removes DYLD_* variables from it and from every test that it starts. Fix: tools/rider.sh starts Rider inside the dev shell and keeps .nix/dotnet-sdk, a stable link to the SDK and a garbage-collector root; the flake exports the SDK as packages.dotnet-sdk; the build copies the Vulkan loader next to each output that sets UsesVulkan, from TERTIUS_VULKAN_DIR, as it copies the Jolt library; tools/README.md gives the one-time Rider settings.


## Notes

**2026-10-17T11:54:09Z**

2026-10-17: tools/rider.sh starts Rider EAP (/Applications/Rider EAP.app, 2026.3 EAP 7, build 263.7031.38) by default. The stable Rider 2026.2.3 does not understand C# 15 unions and marked every union pattern match as an error; the EAP does not. RIDER still selects another Rider.
