# Remote machines for the CPU-only tests and the Stryker runs (ter-fz34)

Research of 2026-11-01 by a delegated researcher (GPT 6.1 SOL), read and condensed by the orchestrator. The researcher read the repository and public sources only. It made no account, sent no code and ran no build. Prices are published rates on 2026-11-01, without tax, storage, traffic or public addresses. "Unverified" marks what the public sources did not settle.

Terms:

- A vCPU is the processor unit that a provider sells. It is not a common measure of speed across providers. For example, a 32-thread Ryzen server has 16 physical cores.
- 120 hours means 4 hours a day for a 30-day month. 720 hours means a machine that stays on all month.

## What a remote machine must run

Six test projects run on the CPU only and can move first: Core, Simulation, Physics (with the double-precision Jolt library), Unions, Style and Slop. Rendering and Host mix CPU tests and Vulkan tests, and no reliable filter tells them apart today, so both stay on the Mac at first.

The remote machine needs the repository's Nix dev shell, not only a .NET SDK. `flake.nix` declares `x86_64-linux` and `aarch64-linux`, and `native/joltc.nix` builds Jolt from pinned sources with the host's library extension. So both Linux architectures look intended, but nobody built or tested them yet. `Directory.Build.targets` falls back to `linux-x64` outside the shell, so Arm runs must use the shell.

The shell has a Vulkan loader but no Linux Vulkan driver: its Linux `VK_ICD_FILENAMES` is empty. Lavapipe is Mesa's Vulkan driver that runs on the CPU. Render tests on Linux need lavapipe or a real GPU driver, and a test of the renderer on that driver.

`tools/test-all.sh` starts every project and configuration together, and each test host runs xunit's parallel threads. A remote run must bound three things apart: the jobs on the machine, the test hosts per job, and the xunit threads per test host.

## Stryker.NET findings

- Stryker.NET 5 has no coordinator that splits one run across machines. Its `mutate` setting can give each machine disjoint files. But each part repeats the build, the initial test run and the coverage capture, and the reports stay separate.
- [Issue #3832](https://github.com/stryker-mutator/stryker-net/issues/3832), open on 2026-11-01: under the MTP runner at a concurrency above 1, 5.0.0 reports killed mutants as survived, and the count changes from run to run. Concurrency 1 gives the right result. The orchestrator read the issue page and made sure of these facts. ter-hxca's continuation settles the official run on it.
- The changed-line selection (`tools/changed_lines.py`) reads committed changes only, so a remote run works on commits, not on a dirty worktree.

## The candidates

The price is for 32 vCPU and about 64 GiB for one hour, where that size exists.

| Candidate | What you rent | 32 vCPU for 1 hour | 120 h / 720 h a month | Start and persistence | Trust (published) |
|---|---|---:|---:|---|---|
| [exe.dev](https://exe.dev/pricing) | A persistent Linux VM over SSH | $1.68 | about $264 / $1,209, with the $75 Work plan | Persistent disk, start time unverified | Bold Software, Inc., California, SOC 2 unverified |
| [Hetzner CCX53](https://www.hetzner.com/cloud/) | An x86-64 VM with 32 dedicated vCPU, 128 GB | $1.02 | $122 / $635 (monthly cap) | Hourly, deleting the VM ends the charge, snapshots keep caches | Hetzner Online GmbH, Germany, ISO 27001 |
| [Hetzner AX102](https://www.hetzner.com/dedicated-rootserver/ax102/) | A dedicated server, Ryzen 7950X3D, 16 cores / 32 threads | not rented by the hour | $302 a month, plus $149 setup | Always warm, delivery time unverified | As above |
| [Hetzner CAX](https://www.hetzner.com/cloud/) | Arm64 VMs with shared vCPU, 16 at most | $0.16 for two | $19 / $97 | Shared CPU, so no sustained throughput | As above |
| [Ubicloud](https://www.ubicloud.com/docs/about/pricing) | x86-64 VMs up to 60 vCPU | $1.00 for two 16-vCPU VMs | $120 / $718 | Hourly, start time unverified | Jurisdiction and attestation unverified |
| [AWS EC2](https://aws.amazon.com/ec2/pricing/on-demand/) | VMs, C7g Arm, C7a x86, C8g Arm | $1.16 to $1.64 | $139 to $197 / $835 to $1,182 | Per second, more account and IAM work | Extensive SOC and ISO reports |
| [Fly.io Machines](https://fly.io/docs/about/pricing/) | x86-64 microVMs | about $1.47 | about $176 / $1,056 | Fast start, a single 32-vCPU shape unverified | Fly.io, Inc., California |
| [Namespace](https://namespace.so/pricing) | Managed Linux VMs, x86-64 and Arm64, up to 64 vCPU | $2.88 | $346 / $2,074, the $250 plan covers 120 h | Per minute, root access, cache volumes | Namespace Labs, California, SOC 2 Type II |
| [Depot](https://depot.dev/pricing), [Blacksmith](https://www.blacksmith.sh/pricing), [GitHub larger runners](https://docs.github.com/en/billing/reference/actions-runner-pricing) | Managed CI runners or dev machines | $2.40 to $5.76 | $288 to $691 / more | Built around CI jobs, awkward for a local worktree | SOC 2 Type II for Depot and Blacksmith |
| [nixbuild.net](https://docs.nixbuild.net/pricing/) | Remote Nix builds, not a VM | €3.84 per 32 used core-hours | €461 / more | Needs the tests packaged as Nix builds | Swedish terms |
| [Modal](https://modal.com/pricing), [Daytona](https://www.daytona.io/pricing), [E2B](https://e2b.dev/pricing), [Fly Sprites](https://sprites.dev/) | Sandboxes | $2.63 to $3.81 | $316 to $457 / more | Nix permissions and sizes unverified | SOC 2 Type II for Modal and E2B |
| A Linux workstation on the LAN | Your own machine, 16 to 32 cores | none | about $82 a month for a $2,500 machine over 36 months with power (all inputs unverified) | Always warm, local caches | No third party, can also hold a real Vulkan GPU |

The full VMs (Hetzner, Ubicloud, AWS, exe.dev) fit Nix best. Some sandboxes need a single-user Nix install or Nix's build sandbox turned off, which is unverified for each product. No ordinary CPU VM in the list offers a Vulkan GPU. Hetzner's GEX44 is a dedicated NVIDIA server, and AWS, Daytona and Modal offer GPUs, but none was checked for the graphics driver that the renderer needs.

## The researcher's two designs

| | Design A: one machine per run | Design B: one machine that stays on, with a queue |
|---|---|---|
| Machine | A script creates a Hetzner CCX53 from a warm image, runs one job and deletes the machine | One Hetzner AX102, a queue on it admits jobs |
| Cost | about $122 a month for 120 active hours, plus the image's storage | about $302 a month, plus $149 setup |
| Wait before the first test | VM creation plus cache restore (unmeasured) | Queue wait only, tools stay warm |
| Capacity | 32 dedicated vCPU, 128 GB | 16 physical cores, 32 threads |
| Main risks | A forgotten machine keeps billing, account quota, a cold image must build Jolt | Admin, patches and backups, a long Stryker run delays the queue |

Both designs share these rules:

1. You approve the provider before any source leaves the Mac.
2. A job sends a frozen, reviewed source snapshot with a content hash, and no Git hosting credentials.
3. The remote side builds with the pinned Linux Nix shell. It never reuses the Mac's `bin`, `obj` or native libraries.
4. Each job has its own source and output folders. Only the Nix store and the NuGet cache are shared.
5. One CPU budget covers builds, tests and Stryker, with one Stryker run at a time.
6. A Linux CPU pass never stands in for the Mac's GPU tests. The test record would need the OS and the architecture before it can count a remote pass.
7. Machines expire and logs have a retention rule.
8. A small pilot measures correctness and speed before any lasting purchase.

## Open questions from the research

1. Low commitment at about $120 to $150 a month of active rental, or a machine that stays warm at about $300 a month?
2. May the source run in Germany or Finland, or must it stay in the United States?
3. May a full Linux pass of the CPU projects satisfy the Mac's commit hook, once the record names the OS and the architecture?
4. May a job send Git history (a bundle), or only a minimal source snapshot?
5. Is a Linux workstation of your own worth it, because it can also test the portable-Vulkan rule on real hardware?
6. Which assurance do you need from a smaller provider: a public security statement, a SOC 2 report, or a written answer on staff access and deletion?
