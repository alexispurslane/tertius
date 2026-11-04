"""Tool code (tools/README.md, "GPU calibration"): the tables of the calibration rounds that rounds.sh wrote.

Usage: python3 -I tools/gpu-calibration/analyze.py <out folder>

Every figure pools all rounds. A median comes with the 10th and 90th percentiles, by nearest rank:

- Probes: each probe's dispatch times of each API and MoltenVK setting, its rate at the median and its share of the peak,
  and the range of the per-run medians.
- Clock: gpu-clock's mean clock over the active time of each run kind, and the share of the active time in each state.
- Pulse: the short FMA probe beside the demo, against a sixteenth of the sustained FMA probe's median.
- Upscalers: FSR 3.1 and MetalFX, the pooled frames and the per-round difference of the run medians.
- Demo: the frame span of each demo run, from the run's own GPU timings.
- Load: the 1-minute load at the start and the end of the runs.
"""

import re
import statistics
import sys
from collections import defaultdict
from pathlib import Path

FREQUENCIES = [388.8, 486.0, 648.0, 777.6, 972.0, 1296.0]


def at(values, fraction):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]


def spread(values):
    return f"{at(values, 0.5):.3f} ({at(values, 0.1):.3f} to {at(values, 0.9):.3f})"


def runs(folder):
    """Yields (round, run name, text) for every log of the rounds."""
    for path in sorted(folder.glob("*-*.log")):
        match = re.match(r"(\d+)-(.+)\.log$", path.name)
        if match:
            yield int(match[1]), match[2], path.read_text()


def probe_lines(text):
    """Yields (probe, api, median, times) for each probe line of a log and its times line."""
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = re.search(r"probe=(\S+) api=(\S+).* median=([0-9.]+)", line)
        if match and index + 1 < len(lines) and "times=" in lines[index + 1]:
            times = [float(value) for value in lines[index + 1].split("times=", 1)[1].split()]
            yield match[1], match[2], float(match[3]), times


def clock(text):
    match = re.search(r"^run seconds=\S+ active_ms=(\S+) mhz=(\S+) states=(\S+)", text, re.M)
    if not match:
        return None
    return float(match[1]), float(match[2]), [float(share) for share in match[3].split(",")]


def main() -> int:
    folder = Path(sys.argv[1])
    work = {"fma": 2**20 * 1024 * 32, "fma-rolled": 2**20 * 1024 * 32}
    work |= {"texture-cached": 4096 * 256 * 512, "texture-stream": 4096 * 256 * 64, "bandwidth": 2**30}
    peaks = {"FLOP": 3072 * 2 * 1.296e9, "texel": 192 * 1.296e9, "B": 400e9}
    units = {"fma": "FLOP", "fma-rolled": "FLOP", "texture-cached": "texel", "texture-stream": "texel"}
    units["bandwidth"] = "B"

    probes = defaultdict(list)
    medians = defaultdict(list)
    clocks = defaultdict(list)
    loads = []
    spans = []
    pulses = []
    upscalers = defaultdict(dict)
    upscaler_frames = defaultdict(list)
    first = defaultdict(list)
    for round_number, name, text in runs(folder):
        loads += [float(value) for value in re.findall(r"load averages?: ([0-9.]+)", text)]
        setting = {"probes-vulkan-fastmath-never": " fast math never", "probes-vulkan-no-argument-buffers": " no argument buffers"}.get(name, "")
        for probe, api, median, times in probe_lines(text):
            key = (probe.replace("-cold", "").replace("-sustained", ""), api + setting, probe.split("-")[-1] if probe.endswith(("cold", "sustained")) else "")
            probes[key] += times
            medians[key].append(median)
            if probe.endswith("cold"):
                first[api].append(times[0])
        if (found := clock(text)) is not None:
            clocks[name].append(found)
        if match := re.search(r"GPU frame span: mean ([0-9.]+) ms, median ([0-9.]+) ms", text):
            spans.append((name, float(match[1]), float(match[2])))
        if match := re.search(r"(FSR 3.1|MetalFX temporal) from .*?median ([0-9.]+) ms", text):
            upscalers[match[1]][round_number] = float(match[2])
            frames = re.search(r"frame times, ms: (.*)", text)
            upscaler_frames[match[1]] += [float(value) for value in frames[1].split()]
        if name == "demo-pulse":
            pulse = folder / f"{round_number}-demo-pulse.pulse"
            pulses += [float(value) for value in re.findall(r"ms=([0-9.]+)", pulse.read_text())]

    print("## Probes\n")
    print("| Probe | API and setting | Kind | Dispatches | Median, p10 to p90 (ms) | Run medians (ms) | Rate at the median | Share of peak |")
    print("|---|---|---|---:|---|---|---|---:|")
    for (probe, api, kind), times in sorted(probes.items()):
        median = at(times, 0.5)
        rate = work[probe] / (median * 1e-3)
        unit = units[probe]
        print(
            f"| {probe} | {api} | {kind or 'warm'} | {len(times)} | {spread(times)} | {min(medians[(probe, api, kind)]):.3f} to "
            f"{max(medians[(probe, api, kind)]):.3f} | {rate:.4g} {unit}/s | {rate / peaks[unit]:.3f} |"
        )
    print("\nFirst dispatch after 10 s of idle, ms:", {api: spread(values) for api, values in first.items()})

    print("\n## Clock\n")
    print("| Run | Runs | Mean clock over the active time, MHz | Active time (ms) | Share of the active time at " + ", ".join(f"{f:.0f}" for f in FREQUENCIES) + " MHz |")
    print("|---|---:|---|---|---|")
    for name, found in sorted(clocks.items()):
        shares = [statistics.mean(states[i] for _, _, states in found) for i in range(len(FREQUENCIES))]
        print(
            f"| {name} | {len(found)} | {spread([mhz for _, mhz, _ in found])} | {spread([active for active, _, _ in found])} | "
            + ", ".join(f"{share:.3f}" for share in shares)
            + " |"
        )

    sustained = probes.get(("fma", "metal", "sustained"))
    if pulses and sustained:
        full = at(sustained, 0.5) / 16
        print("\n## Pulse\n")
        print(f"The pulse probe beside the demo: {len(pulses)} probes, {spread(pulses)} ms, fastest tenth {at(pulses, 0.1):.3f} ms.")
        print(f"A sixteenth of the sustained FMA probe's median: {full:.3f} ms. Ratio of the median: {full / at(pulses, 0.5):.3f}.")

    print("\n## Upscalers\n")
    for name, frames in upscaler_frames.items():
        print(f"{name}: {len(frames)} frames, {spread(frames)} ms, run medians {sorted(upscalers[name].values())}")
    rounds = sorted(set(upscalers["FSR 3.1"]) & set(upscalers["MetalFX temporal"]))
    differences = [upscalers["FSR 3.1"][r] - upscalers["MetalFX temporal"][r] for r in rounds]
    ratios = [upscalers["FSR 3.1"][r] / upscalers["MetalFX temporal"][r] for r in rounds]
    print(f"FSR minus MetalFX per round, ms: {[round(d, 3) for d in differences]}, median {statistics.median(differences):.3f}")
    print(f"FSR over MetalFX per round: median {statistics.median(ratios):.3f}, {min(ratios):.3f} to {max(ratios):.3f}")

    print("\n## Demo frame span\n")
    for name in sorted({name for name, _, _ in spans}):
        means = [mean for each, mean, _ in spans if each == name]
        print(f"{name}: {len(means)} runs, mean span {spread(means)} ms")
    print(f"\nLoad (1 minute) at the starts and ends of the runs: {min(loads):.2f} to {max(loads):.2f}, median {statistics.median(loads):.2f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
