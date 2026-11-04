"""Tool code (tools/README.md, "GPU calibration"): the tables of the calibration runs.

Usage:
    python3 -I tools/gpu-calibration/analyze.py rounds <out folder of rounds.sh>
    python3 -I tools/gpu-calibration/analyze.py upscalers <out folder of upscalers.sh>
    python3 -I tools/gpu-calibration/analyze.py probes <folder of probe logs, such as runs of probes fma-variants>
    python3 -I tools/gpu-calibration/analyze.py windows <out folder of window.sh>
    python3 -I tools/gpu-calibration/analyze.py window <log of window.sh> <busy samples of window.sh>

"probes" pools the probe lines of every log in a folder. "window" prints the summary line of one window run, which window.sh prints after each run. "windows" prints the table of
every window run in a folder, and "upscalers" the table of the upscaler runs. "rounds" prints the tables of the
calibration rounds. Every figure of "rounds" pools all rounds. A median comes with the 10th and 90th percentiles, by
nearest rank:

- Probes: each probe's dispatch times of each API and MoltenVK setting, its rate at the median and its share of the peak,
  and the range of the per-run medians.
- Clock: gpu-clock's mean clock over the active time of each run kind, over the whole run and over the timed span
  (tools/gpu_clock.py), and the share of the active time of the whole run in each state.
- Pulse: the short FMA probe beside the demo, against a sixteenth of the sustained FMA probe's median.
- Upscalers: FSR 3.1's timing test and MetalFX, run as two of the twelve runs of each round, the pooled frames and the
  per-round difference of the run medians. The upscaler table of the report comes from "upscalers", from runs of their
  own.
- Demo: the frame span of each demo run, from the run's own GPU timings.
- Load: the 1-minute load at the start and the end of the runs.
"""

import importlib.util
import re
import statistics
import sys
from collections import defaultdict
from pathlib import Path

# The record of the clock over a timed span. python3 -I leaves the script's folder out of the import path, so the
# module loads by its path.
_spec = importlib.util.spec_from_file_location("gpu_clock", Path(__file__).parent.parent / "gpu_clock.py")
gpu_clock = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gpu_clock)

FREQUENCIES = [388.8, 486.0, 648.0, 777.6, 972.0, 1296.0]


at = gpu_clock.at


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


PRESENTED = "Performance statistics reporting every"
STEADY_FROM = 5.0


def window_summary(log, busy):
    """The summary of one window run of window.sh, from its log and its busy samples.

    The steady span runs from 5 s after gpu-clock's start, after the window opened and the governor settled, to the
    last sample before MoltenVK's last log of presented frames. The samples after it hold the demo's teardown.
    steady_mhz is the mean clock over the active time of the steady samples, steady_active their mean active share,
    and steady_busy the mean of the busy samples in the steady span, in percent.
    """
    lines = log.splitlines()
    found = gpu_clock.samples(log)
    presents = [index for index, line in enumerate(lines) if PRESENTED in line]
    before_last = [each for each in found if presents and each.line < presents[-1]]
    end = before_last[-1].t if before_last else float("inf")
    steady = [each for each in found if STEADY_FROM <= each.t <= end]
    steady_clock = gpu_clock.clock_of(steady)
    busy_values = []
    for line in busy.splitlines():
        if match := re.match(r"^busy t=([0-9.]+) percent=([0-9]+)", line):
            if STEADY_FROM <= float(match[1]) <= end:
                busy_values.append(float(match[2]))
    elapsed = [float(match) for match in re.findall(r"elapsed time: ([0-9.]+)", log)]
    swapchain = re.search(r"Created [0-9]+ swapchain images with size \(([0-9]+), ([0-9]+)\)", log)
    run = clock(log)
    frame = re.findall(r"Frame interval +avg: ([0-9.]+)", log)
    drawable = re.findall(r"Retrieve a CAMetalDrawable +avg: ([0-9.]+)", log)
    return {
        "swapchain": f"{swapchain[1]}x{swapchain[2]}" if swapchain else None,
        "run_mhz": run[1] if run else None,
        "steady_samples": len(steady),
        "steady_mhz": None if isinstance(steady_clock, gpu_clock.Unknown) else steady_clock.mhz,
        "steady_active": statistics.mean(each.active for each in steady) if steady else None,
        "steady_busy": statistics.mean(busy_values) if busy_values else None,
        "fps": 60 * (len(elapsed) - 1) / (elapsed[-1] - elapsed[0]) if len(elapsed) > 1 else None,
        "frame_ms": float(frame[-1]) if frame else None,
        "drawable_ms": float(drawable[-1]) if drawable else None,
    }


def pooled_probes(texts):
    """Pools each probe's dispatch times over the logs texts: {name: {dispatches, median, p10, p90, share}}. The share of
    the peak at the pooled median comes from the work that each probe line implies, its rate times its median, and from
    its share of the peak at its rate."""
    times = defaultdict(list)
    per_unit = {}
    for text in texts:
        lines = text.splitlines()
        for index, line in enumerate(lines):
            match = re.search(r"probe=(\S+) .*median=([0-9.]+) .*rate=([0-9.e+]+) \S+ share=([0-9.]+)", line)
            if not match or index + 1 >= len(lines) or not lines[index + 1].startswith("times="):
                continue
            rate = float(match[3])
            per_unit[match[1]] = (rate * float(match[2]), float(match[4]) / rate)
            times[match[1]] += [float(value) for value in lines[index + 1].split("=", 1)[1].split()]
    pooled = {}
    for name, found in times.items():
        work, share_per_rate = per_unit[name]
        median = at(found, 0.5)
        pooled[name] = {
            "dispatches": len(found),
            "median": median,
            "p10": at(found, 0.1),
            "p90": at(found, 0.9),
            "share": work / median * share_per_rate,
        }
    return pooled


def probes_table(folder):
    """Prints each probe's pooled times over the logs of folder, with its share of the peak at the pooled median and at
    the 10th percentile."""
    pooled = pooled_probes(path.read_text() for path in sorted(Path(folder).glob("*.log")))
    print("| Probe | Dispatches | Median (p10 to p90), ms | Share of the peak at the median | At the 10th percentile |")
    print("|---|---:|---|---:|---:|")
    for name, found in pooled.items():
        print(
            f"| {name} | {found['dispatches']} | {found['median']:.3f} ({found['p10']:.3f} to {found['p90']:.3f}) | "
            f"{found['share']:.3f} | {found['share'] * found['median'] / found['p10']:.3f} |"
        )
    return 0


def number(value, digits):
    return "" if value is None else f"{value:.{digits}f}"


def window(log_path, busy_path):
    """Prints the summary line of one window run."""
    busy = Path(busy_path)
    summary = window_summary(Path(log_path).read_text(), busy.read_text() if busy.exists() else "")
    print(" ".join(f"{key}={number(value, 3) if isinstance(value, float) else value}" for key, value in summary.items()))
    return 0


def windows(folder):
    """Prints the table of every window run in folder, one row per run, in the order of view, size and round."""
    print("| Run | Swapchain | Steady samples | Windowed clock, MHz | Share of 1293 MHz | Busy share, % | Frames per second | Wait for a drawable, ms |")
    print("|---|---|---:|---:|---:|---:|---:|---:|")
    found = []
    for path in sorted(Path(folder).glob("*.log")):
        if not re.match(r"\d+-", path.name):
            continue
        summary = window_summary(path.read_text(), path.with_suffix(".busy").read_text() if path.with_suffix(".busy").exists() else "")
        if summary["swapchain"] is None:
            continue
        found.append((path.stem.split("-", 1)[1], summary["swapchain"], path.stem, summary))
    for _, swapchain, name, summary in sorted(found):
        share = summary["steady_mhz"] / 1293 if summary["steady_mhz"] else None
        print(
            f"| {name} | {swapchain} | {summary['steady_samples']} | {number(summary['steady_mhz'], 1)} | {number(share, 3)} | "
            f"{number(summary['steady_busy'], 1)} | {number(summary['fps'], 1)} | {number(summary['drawable_ms'], 2)} |"
        )
    return 0


def upscalers(folder):
    """Prints the table of the upscaler runs of upscalers.sh: per kind, the pooled frame times, the range of the run
    medians, the clock of the whole run and, where the run prints markers, the clock over its timed span."""
    times = defaultdict(list)
    run_medians = defaultdict(list)
    run_clocks = defaultdict(list)
    span_clocks = defaultdict(list)
    for _, name, text in runs(Path(folder)):
        median = re.search(r"median ([0-9.]+) ms, 10th percentile", text)
        frames = re.search(r"frame times, ms: (.*)", text)
        if not median or not frames:
            continue
        run_medians[name].append(float(median[1]))
        times[name] += [float(value) for value in frames[1].split()]
        if (found := clock(text)) is not None:
            run_clocks[name].append(found[1])
        if isinstance(span := gpu_clock.span_clock(text), gpu_clock.Known):
            span_clocks[name].append(span.mhz)
    print("| Kind | Frames or batches | Median (p10 to p90), ms | Run medians, ms | Clock of the whole run, MHz | Clock over the timed span, MHz |")
    print("|---|---:|---|---|---|---|")
    for name in sorted(times):
        print(
            f"| {name} | {len(times[name])} | {spread(times[name])} | {min(run_medians[name]):.3f} to {max(run_medians[name]):.3f} | "
            f"{spread(run_clocks[name]) if run_clocks[name] else ''} | {spread(span_clocks[name]) if span_clocks[name] else ''} |"
        )
    return 0


def main() -> int:
    command = sys.argv[1]
    if command == "window":
        return window(sys.argv[2], sys.argv[3])
    if command == "windows":
        return windows(sys.argv[2])
    if command == "upscalers":
        return upscalers(sys.argv[2])
    if command == "probes":
        return probes_table(sys.argv[2])
    if command != "rounds":
        print(__doc__, file=sys.stderr)
        return 2
    return rounds(Path(sys.argv[2]))


def rounds(folder) -> int:
    work = {"fma": 2**20 * 1024 * 32, "fma-rolled": 2**20 * 1024 * 32}
    work |= {"texture-cached": 4096 * 256 * 512, "texture-stream": 4096 * 256 * 64, "bandwidth": 2**30}
    peaks = {"FLOP": 3072 * 2 * 1.296e9, "texel": 192 * 1.296e9, "B": 400e9}
    units = {"fma": "FLOP", "fma-rolled": "FLOP", "texture-cached": "texel", "texture-stream": "texel"}
    units["bandwidth"] = "B"

    probes = defaultdict(list)
    medians = defaultdict(list)
    clocks = defaultdict(list)
    span_clocks = defaultdict(list)
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
        if isinstance(span := gpu_clock.span_clock(text), gpu_clock.Known):
            span_clocks[name].append(span.mhz)
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
    print("| Run | Runs | Mean clock over the active time, MHz | Clock over the timed span, MHz | Active time (ms) | Share of the active time at " + ", ".join(f"{f:.0f}" for f in FREQUENCIES) + " MHz |")
    print("|---|---:|---|---|---|---|")
    for name, found in sorted(clocks.items()):
        shares = [statistics.mean(states[i] for _, _, states in found) for i in range(len(FREQUENCIES))]
        print(
            f"| {name} | {len(found)} | {spread([mhz for _, mhz, _ in found])} | {spread(span_clocks[name]) if span_clocks[name] else ''} | "
            f"{spread([active for active, _, _ in found])} | "
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
