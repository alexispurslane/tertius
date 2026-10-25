#!/usr/bin/env python3
"""Tool code. Trims a script to a few frames, and reads a Metal System Trace of a headless Tertius run into a table of
the GPU time and the limiters of each pass.

tools/gpu-trace.sh records the trace and calls this script. Mac only: it reads the tables of `xcrun xctrace export`.

    python3 tools/gpu-trace.py trim <script> <start> <frames> <out.json>
    python3 tools/gpu-trace.py report <trace> <gpu-timings.json> <out-dir> [--process Tertius.Demo]

trim keeps the frames from frame <start> (counted from 0 over the script's captures and film frames) to <start> +
<frames>. A film frame before <start> becomes a run step of its ticks, so the world reaches the same state without a
render. The steps after the last kept frame go.

How the encoders map to the passes (tools/README.md, "GPU trace"): MoltenVK turns each vkCmdWriteTimestamp2 into an
encoder of its own, "Record GPU Counter Sample BlitEncoder". The renderer writes one stamp before its first pass and one
after each pass, so in a command buffer with passes + 1 such encoders, the encoders between stamp k and stamp k + 1 are
pass k. The pass names come from the run's gpu-timings.json, in recording order.

report writes <out-dir>/passes.md, the table, and <out-dir>/passes.json, every counter of every pass, and prints the
table. It exports the trace's tables to <out-dir>/export first, unless they are there.
"""

import argparse
import bisect
import json
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

STAMP = "Record GPU Counter Sample BlitEncoder"
BEFORE = "(before the first pass)"
AFTER = "(after the last pass)"
FRAME = "frame span"


def export(trace: Path, schema: str, out: Path) -> Path:
    """Exports one table of the first run of the trace to an XML file in out, unless the file is there already."""
    path = out / f"{schema}.xml"
    if not path.exists():
        xpath = f'/trace-toc/run[@number="1"]/data/table[@schema="{schema}"]'
        with path.open("wb") as file:
            subprocess.run(
                ["/usr/bin/xcrun", "xctrace", "export", "--input", str(trace), "--xpath", xpath],
                stdout=file,
                check=True,
            )
    return path


def rows(path: Path):
    """Yields each row of an exported table as {mnemonic: (fmt, text)}, with the id and ref sharing of xctrace resolved."""
    ids: dict[str, tuple[str | None, str | None]] = {}
    columns: list[str] = []

    def value(element) -> tuple[str | None, str | None]:
        ref = element.get("ref")
        if ref is not None:
            return ids[ref]
        for child in element:
            value(child)
        found = (element.get("fmt"), element.text)
        if element.get("id") is not None:
            ids[element.get("id")] = found
        return found

    for _, element in ET.iterparse(path, events=("end",)):
        if element.tag == "mnemonic":
            columns.append(element.text)
        elif element.tag == "row":
            yield dict(zip(columns, (value(child) for child in element)))
            # The ids keep the shared values, so the elements themselves can go.
            element.clear()


def of_process(row, process: str) -> bool:
    return (row.get("process", (None, None))[0] or "").startswith(process + " (")


def passes_by_encoder(encoders: Path, process: str, passes: list[str]):
    """Maps each encoder of the process to its pass name, and counts the command buffers that fit the passes."""
    by_buffer: dict[str, list[tuple[int, str, str]]] = defaultdict(list)
    for row in rows(encoders):
        if not of_process(row, process):
            continue
        indexed = row["encoder-label-indexed"][0] or ""
        index = int(indexed[1 : indexed.index("]")])
        by_buffer[row["cmdbuffer-id"][0]].append((index, row["encoder-id"][0], row["encoder-label"][0] or ""))
    named: dict[str, tuple[str, str]] = {}
    skipped = 0
    for buffer, listed in by_buffer.items():
        listed.sort()
        if sum(1 for _, _, label in listed if label == STAMP) != len(passes) + 1:
            # A command buffer of the start, such as an upload, records no frame graph.
            skipped += 1
            continue
        stamps = -1
        for _, encoder, label in listed:
            if label == STAMP:
                stamps += 1
                continue
            if stamps < 0:
                name = BEFORE
            elif stamps < len(passes):
                name = passes[stamps]
            else:
                name = AFTER
            named[encoder] = (buffer, name)
    return named, len(by_buffer) - skipped, skipped


def union(intervals: list[tuple[int, int]]) -> list[tuple[int, int]]:
    """The union of [start, end) intervals in nanoseconds, as sorted intervals that do not overlap."""
    merged: list[tuple[int, int]] = []
    for start, end in sorted(intervals):
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], end))
        else:
            merged.append((start, end))
    return merged


def gpu_intervals(intervals: Path, process: str, named):
    """The GPU intervals of each pass in each command buffer: {(buffer, pass): [(start, end)]}, in nanoseconds."""
    by_pass: dict[tuple[str, str], list[tuple[int, int]]] = defaultdict(list)
    for row in rows(intervals):
        if not of_process(row, process):
            continue
        found = named.get(row["encoder-id"][0])
        if found is None:
            continue
        start = int(row["start"][1])
        by_pass[found].append((start, start + int(row["duration"][1])))
    return {key: union(spans) for key, spans in by_pass.items()}


def counter_means(info: Path, values: Path, windows: dict[tuple[str, str], list[tuple[int, int]]]):
    """The mean of each counter over the samples that fall inside each pass's GPU intervals: {pass: {counter: mean}}."""
    names = {row["counter-id"][1]: row["name"][1] for row in rows(info)}
    flat = sorted((start, end, name) for (_, name), spans in windows.items() for start, end in spans)
    starts = [start for start, _, _ in flat]
    sums: dict[str, dict[str, float]] = defaultdict(lambda: defaultdict(float))
    counts: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    for row in rows(values):
        time = int(row["timestamp"][1])
        at = bisect.bisect_right(starts, time) - 1
        if at < 0 or time >= flat[at][1]:
            continue
        name = flat[at][2]
        counter = names[row["counter-id"][1]]
        sums[name][counter] += float(row["value"][1])
        counts[name][counter] += 1
    return {name: {counter: sums[name][counter] / counts[name][counter] for counter in sums[name]} for name in sums}


def summary(times: list[float]) -> dict:
    """The count, mean, median and nearest-rank 95th percentile of times in milliseconds, as gpu-timings.json has them."""
    ordered = sorted(times)
    rank = -(-95 * len(ordered) // 100)
    return {
        "frames": len(ordered),
        "meanMs": statistics.fmean(ordered),
        "medianMs": statistics.median(ordered),
        "p95Ms": ordered[rank - 1],
    }


def limiters(counters: dict[str, float]) -> list[tuple[str, float]]:
    """The limiter counters of a pass, highest first."""
    found = [(name.removesuffix(" Limiter"), value) for name, value in counters.items() if name.endswith(" Limiter")]
    return sorted(found, key=lambda pair: -pair[1])


def trim(args) -> int:
    script = json.loads(args.script.read_text())
    steps = []
    frame = 0
    end = args.start + args.frames
    for step in script["steps"]:
        if frame >= end:
            break
        kind = step["do"]
        if kind == "capture":
            if frame >= args.start:
                steps.append(step)
            frame += 1
        elif kind == "film":
            skipped = min(max(args.start - frame, 0), step["frames"])
            kept = min(step["frames"] - skipped, end - frame - skipped)
            if skipped > 0:
                steps.append({"do": "run", "ticks": skipped * step["every"]})
            if kept > 0:
                steps.append({**step, "frames": kept})
            frame += skipped + kept
        else:
            steps.append(step)
    script["steps"] = steps
    args.out.write_text(json.dumps(script, indent=2))
    if frame <= args.start:
        print(f"{args.script} has only {frame} frames, none from frame {args.start}.", file=sys.stderr)
        return 1
    return 0


def report(args) -> int:
    # The exported tables, which gpu-trace.sh deletes after the report: the counters of 120 frames export to gigabytes.
    exports = args.out / "export"
    exports.mkdir(parents=True, exist_ok=True)
    timings = json.loads(args.timings.read_text())
    passes = [entry["pass"] for entry in timings["passes"]]

    named, frames, skipped = passes_by_encoder(
        export(args.trace, "metal-application-encoders-list", exports), args.process, passes
    )
    if frames == 0:
        print(f"No command buffer of {args.process} holds {len(passes) + 1} timestamp encoders.", file=sys.stderr)
        return 1
    windows = gpu_intervals(export(args.trace, "metal-gpu-intervals", exports), args.process, named)

    order = [BEFORE, *passes, AFTER]
    busy: dict[str, list[float]] = defaultdict(list)
    for (_, name), spans in windows.items():
        busy[name].append(sum(end - start for start, end in spans) / 1e6)
    spans_of_frames: dict[str, list[tuple[int, int]]] = defaultdict(list)
    for (buffer, name), spans in windows.items():
        if name in passes:
            spans_of_frames[buffer].extend(spans)
    frame_spans = [(max(end for _, end in spans) - min(start for start, _ in spans)) / 1e6 for spans in spans_of_frames.values()]

    info = export(args.trace, "gpu-counter-info", exports)
    has_counters = any(True for _ in rows(info))
    counters = counter_means(info, export(args.trace, "gpu-counter-value", exports), windows) if has_counters else {}

    report = {"device": timings["device"], "width": timings["width"], "height": timings["height"]}
    report["commandBuffers"] = {"frames": frames, "skipped": skipped}
    report["frame"] = summary(frame_spans)
    report["passes"] = [
        {"pass": name, **summary(busy[name]), "counters": counters.get(name, {})} for name in order if busy[name]
    ]
    # A pass of the frame graph that gave the GPU no encoder in any frame, such as the outlines with none to draw.
    report["idle"] = [name for name in passes if not busy[name]]
    (args.out / "passes.json").write_text(json.dumps(report, indent=2))

    lines = [
        f"{timings['device']}, {timings['width']} x {timings['height']}: {frames} frames, {skipped} other command buffers.",
        "Times are the GPU busy time of each pass's encoders, in milliseconds.",
        "",
        "| Pass | Frames | Mean | Median | p95 | Top limiter | Second limiter | Read GB/s | Write GB/s |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for entry in report["passes"]:
        top = limiters(entry["counters"])
        cells = [f"{name} {value:.0f} %" for name, value in top[:2]] + ["-"] * (2 - len(top[:2]))
        read = entry["counters"].get("GPU Read Bandwidth")
        write = entry["counters"].get("GPU Write Bandwidth")
        lines.append(
            f"| {entry['pass']} | {entry['frames']} | {entry['meanMs']:.3f} | {entry['medianMs']:.3f} | {entry['p95Ms']:.3f}"
            f" | {cells[0]} | {cells[1]} | {'-' if read is None else f'{read:.1f}'} | {'-' if write is None else f'{write:.1f}'} |"
        )
    frame = report["frame"]
    lines.append(
        f"| {FRAME} | {frame['frames']} | {frame['meanMs']:.3f} | {frame['medianMs']:.3f} | {frame['p95Ms']:.3f} | | | | |"
    )
    if report["idle"]:
        lines += ["", f"Passes with no GPU work in any frame: {', '.join(report['idle'])}."]
    if not has_counters:
        lines += ["", "The trace holds no GPU counters, so the table has no limiters (tools/README.md, \"GPU trace\")."]
    table = "\n".join(lines) + "\n"
    (args.out / "passes.md").write_text(table)
    print(table, end="")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(required=True)
    trimming = commands.add_parser("trim")
    trimming.add_argument("script", type=Path)
    trimming.add_argument("start", type=int)
    trimming.add_argument("frames", type=int)
    trimming.add_argument("out", type=Path)
    trimming.set_defaults(run=trim)
    reporting = commands.add_parser("report")
    reporting.add_argument("trace", type=Path)
    reporting.add_argument("timings", type=Path, help="the run's gpu-timings.json, which names the passes")
    reporting.add_argument("out", type=Path)
    reporting.add_argument("--process", default="Tertius.Demo")
    reporting.set_defaults(run=report)
    args = parser.parse_args()
    return args.run(args)


if __name__ == "__main__":
    sys.exit(main())
