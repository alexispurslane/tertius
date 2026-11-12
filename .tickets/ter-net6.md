---
id: ter-net6
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux]
---
# GPU tests run out of device memory on a 16 GB NVIDIA card when they run in parallel

On the RTX 2000 Ada pod of ter-libf (16 GB, driver 595.91.07, 2026-11-12), the first Linux run of `dotnet test --solution Tertius.slnx -c Checked` failed 148 tests with `vkAllocateMemory failed: ErrorOutOfDeviceMemory`. They are Host tests that start a renderer each: ScriptCommandCaptureTests (35), DemoLaunchTests (20), SpinStationSliceTests (13), DeepYardSliceTests, SpacewalkSliceTests, ThrowSliceTests, StationSliceTests and others. The Mac shares 64 GB with the GPU, so it never meets the limit. With `--max-threads 2`, no test ran out of memory. Decide between a cap on the renderers that the tests start at once (for example one xunit collection or a semaphore for GPU fixtures) and a smaller device budget per test renderer. The serverless workers of ter-vy3m have 16 to 24 GB, so this blocks a full test run there. Logs: /private/tmp/ter-libf/pod-out/tests-checked.log.

## Notes

**2026-11-12T03:15:00Z**

Second run, on the L4 pod (24 GB), with the static TLS fix of native/host-nvidia-vulkan.sh: no test ran out of device memory with the default parallelism. So the limit is near 16 GB. The 16 GB pool of the serverless workers (A4000, A4500, RTX 4000 Ada, RTX 2000 Ada) still needs the fix, or a test run with `--max-threads 2`.
