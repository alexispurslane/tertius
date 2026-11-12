---
id: ter-libf
status: closed
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

## Notes

**2026-11-12T03:20:00Z**

Result of the bring-up (2026-11-12). Two pods: RTX 2000 Ada (Secure, EUR-IS-1, $0.24/h, 02:06 to 02:43 UTC) and L4 (Secure, US-MO-2, $0.59/h, 02:44 to 03:17 UTC), both with driver 595.91.07 and CUDA 13.2. No A4000, A4500 or RTX 4000 Ada was in stock in either cloud. About $0.48 in total.

What built and ran: the dev shell from `tools/runpod/provision.sh` (197 to 216 s on a fresh pod), the Release demo (42 to 57 s), `vulkaninfo` on the NVIDIA GPU, the deck's still, the deck's timing, 12 survey views, and the full Checked test suite.

The same work as the Mac. The deck still of `deck.json` matches the Mac's still of the merged frame-time cuts: mean difference 0.12/255, 0.04 % of pixels above 2 levels, none above 10. Both run the same 27 passes at 1280 x 720 rendered and 1920 x 1080 output with FSR 3.1. The Mac trace's build fixed the exposure, so it lacks `exposure` and `exposure adapt` (0.02 ms on the pod).

Stages against the Mac trace of main (`/private/tmp/tertius-sweep-20261111/trace/deck-maind-1`, 119 frames, one frame in flight). Mac: each pass's own trace time; the composite-to-tone-map group by the Mac's stamps, because its passes overlap in the trace. Pods: the engine's per-pass stamps, the mean of three runs with the Mac trace's script and options. Marked: a ratio more than 30 % from the frame's ratio without capture.

| Stage | Mac ms | RTX 2000 Ada ms | ÷ Mac | L4 ms | ÷ Mac |
|---|---:|---:|---:|---:|---:|
| Shadow maps and local shadows | 0.58 | 0.30 | 0.52 | 0.22 | 0.38 marked |
| Environment faces and prefilter | 0.76 | 0.17 | 0.23 marked | 0.16 | 0.22 |
| G-buffer, surface test, depth pyramid | 0.69 | 0.53 | 0.77 marked | 0.34 | 0.50 marked |
| Direct light and light pyramid | 4.20 | 1.83 | 0.44 | 0.92 | 0.22 |
| Ray plan | 0.89 | 0.27 | 0.30 | 0.19 | 0.21 |
| Reflection rays | 2.67 | 1.08 | 0.40 | 0.60 | 0.22 |
| Diffuse rays and screen trace resolve | 1.64 | 0.78 | 0.48 | 0.46 | 0.28 |
| Indirect diffuse: accumulate and filter | 4.72 | 2.71 | 0.57 marked | 1.64 | 0.35 marked |
| Indirect specular: accumulate | 1.40 | 0.57 | 0.41 | 0.40 | 0.29 |
| Composite through tone map | 5.19 | 2.04 | 0.39 | 1.25 | 0.24 |
| Capture (headless readback) | 0.03 | 2.57 | marked | 3.27 (0.9 to 4.5) | marked |
| Frame span | 24.37 | 12.89 | 0.53 | 9.51 | 0.39 |
| Frame span without capture | 24.34 | 10.33 | 0.42 | 6.24 | 0.26 |

The top five stages are the same five on all three machines; on both cards the indirect diffuse stage is first and the composite group second, the reverse of the Mac. The mean distance of a stage's ratio from the frame's ratio is 24 % on the RTX 2000 Ada and 27 % on the L4. Recommendation: the RTX 2000 Ada (16 GB pool), because it tracks the Mac as well as the L4, its readback was steady and it is cheaper.

Clocks: RTX 2000 Ada SM 2205 to 2460 MHz under load, memory 7001 MHz, 38 to 51 C, up to 68.4 W of 70 W. L4 SM 1950 to 2040 MHz, memory 6251 MHz, up to 67.8 W of 72 W. `nvidia-smi -lgc` on both: "The current user does not have permission to change clocks for GPU ..." (exit code 4).

Survey views on the RTX 2000 Ada against the Mac survey of 2026-11-09 (older code; the pod's numbers hold the 2.6 ms readback): 0.42 to 0.98 of the Mac's median, exit-deck 13.18 against 30.39 ms.

Linux failures:
- Fixed: the static TLS reserve of glibc, which failed the 38th vkCreateInstance of a process as ErrorIncompatibleDriver (`native/host-nvidia-vulkan.sh`). With it, the L4 ran the Host tests with 3 failures instead of 96. The three capture tests with an index error came from this failure too.
- Fixed: `/homeless-shelter` in `provision.sh`, and the floating registry `nixpkgs` that `nix develop <profile>` downloaded.
- Tickets: ter-net6 (out of device memory with parallel tests on 16 GB), ter-zkdz (four tests hold the Mac's results; per the user's rule of 2026-11-12, they must compare two runs on one machine), ter-qjyo (FSR constant 1 ulp), ter-j4h9 (closing card), ter-fw73 (the readback in the frame span), ter-az8f (no clock on Linux), ter-8esk (sphere light reference image), and a note on ter-cgif (FSR reference image, largest FLIP 0.315).
- Not finished: the Rendering tests with the TLS fix on the L4 ran more than 12 minutes without an end (16 s on the RTX 2000 Ada without the fix, where most of them failed early). The pod was deleted before the cause was found. The next Linux run of the Rendering tests needs `--long-running 60`.

For ter-94uo: `tools/runpod/Dockerfile` runs `provision.sh`; the image then starts the shell offline. Bake a built Release demo into the image too: a job's CPU part (scene load and simulated ticks) was most of each run, about 4 s for 1.6 s of GPU work on the deck, and a serverless worker bills that time at the GPU rate. Run tests with `--max-threads 2` on a 16 GB card.
