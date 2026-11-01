#!/usr/bin/env python3
"""Tool code. Trims a script to a few frames, and reads a Metal System Trace of a headless Tertius run into a table of
the GPU time and the limiters of each pass.

tools/gpu-trace.sh records the trace and calls this script. Mac only: it reads the tables of `xcrun xctrace export`.

    python3 tools/gpu-trace.py trim <script> <start> <frames> <out.json>
    python3 tools/gpu-trace.py report <trace> <gpu-timings.json> <out-dir> [--process Tertius.Demo] [--window FROM TO]
        [--paired PASS | --control PASS]

trim keeps the frames from frame <start> (counted from 0 over the script's captures and film frames) to <start> +
<frames>, or to the end of the script when <frames> is 0. A film frame before <start> becomes a run step of its ticks, so the world reaches the same state without a
render. The steps after the last kept frame go.

How the encoders map to the passes (tools/README.md, "GPU trace"): MoltenVK turns each vkCmdWriteTimestamp2 into an
encoder of its own, "Record GPU Counter Sample BlitEncoder". The renderer writes one stamp before its first pass and one
after each pass, so in a command buffer with passes + 1 such encoders, the encoders between stamp k and stamp k + 1 are
pass k. The pass names come from the run's gpu-timings.json, in recording order.

report writes <out-dir>/passes.md, the table, and <out-dir>/passes.json, every counter of every pass, and prints the
table. It exports the trace's tables to <out-dir>/export first, unless they are there. The table gives each pass's busy
time and its time alone, and passes.json also the shared time, the busy time minus the time alone. --window adds a row
with each frame's wall-clock GPU time from the end of pass FROM to the start of pass TO. --paired PASS adds a second
table for a trace whose frames alternate with and without PASS: each frame without it against the mean of its two
neighbors with it, as paired medians with bootstrap standard errors. --control PASS gives the same table for a trace
whose frames all run PASS, with each odd frame against its two even neighbors, so it shows the noise of the method.
summarize and paired compute the statistics, and table formats passes.md. tools/test_gpu_trace.py tests them and the
pure functions below them.
"""

import argparse
import bisect
import json
import math
import random
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
RESAMPLES = 2000
"""The number of bootstrap resamples behind each standard error of the paired mode."""
SEED = 1
"""The seed of the paired mode's bootstrap, fixed so that two reports of one trace give the same standard errors."""


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


def gpu_intervals(table: Path, process: str, named):
    """The GPU intervals of each pass in each command buffer: {(buffer, pass): [(start, end)]}, in nanoseconds."""
    by_pass: dict[tuple[str, str], list[tuple[int, int]]] = defaultdict(list)
    for row in rows(table):
        if not of_process(row, process):
            continue
        found = named.get(row["encoder-id"][0])
        if found is None:
            continue
        start = int(row["start"][1])
        by_pass[found].append((start, start + int(row["duration"][1])))
    return {key: union(spans) for key, spans in by_pass.items()}


def open_segments(intervals: dict):
    """Cuts the timeline at every start and end of the intervals, and names the keys open in each segment.

    intervals maps each key, such as a pass name or a (command buffer, pass) pair, to its [start, end) intervals. Returns
    the sorted cuts and, for the segment from cut i to cut i + 1, the keys whose intervals hold it. The intervals of two
    passes can overlap, and a long pass can hold a short one, so a time can lie in several passes.
    """
    events = sorted(
        (time, step, key)
        for key, spans in intervals.items()
        for start, end in spans
        for time, step in ((start, 1), (end, -1))
    )
    cuts: list[int] = []
    segments: list[frozenset] = []
    # Only the open keys stay in open_count, so each event costs the number of open keys, not of all keys seen.
    open_count: dict = defaultdict(int)
    for time, step, key in events:
        if not cuts or time != cuts[-1]:
            cuts.append(time)
            segments.append(frozenset())
        open_count[key] += step
        if open_count[key] == 0:
            del open_count[key]
        segments[-1] = frozenset(open_count)
    return cuts, segments


def busy_and_alone(intervals: dict[tuple[str, str], list[tuple[int, int]]]) -> dict[tuple[str, str], tuple[int, int]]:
    """The busy time and the time alone of each pass in each command buffer: {(buffer, pass): (busy, alone)}, in ns.

    The busy time is the length of the union of the pass's own intervals. The time alone is the part of it in which no
    other key of intervals has an interval open: no other pass, and no pass of another command buffer. So the busy time
    minus the time alone is the time that the pass shares with the other encoders that the table counts.
    """
    cuts, segments = open_segments(intervals)
    alone: dict[tuple[str, str], int] = defaultdict(int)
    for at, keys in enumerate(segments[:-1]):
        if len(keys) == 1:
            (key,) = keys
            alone[key] += cuts[at + 1] - cuts[at]
    return {key: (sum(end - start for start, end in union(spans)), alone[key]) for key, spans in intervals.items()}


def frame_span(frame: dict[str, list[tuple[int, int]]]) -> int:
    """The wall-clock GPU time of one frame from the first start of any of its passes to the last end, in ns. frame maps
    each pass of the frame that has GPU work to its intervals."""
    return max(end for spans in frame.values() for _, end in spans) - min(
        start for spans in frame.values() for start, _ in spans
    )


def frames_by_buffer(intervals: dict, passes: list[str]) -> dict[str, dict[str, list[tuple[int, int]]]]:
    """The frames of the trace: {command buffer: {pass: intervals}}, with only the passes of the frame graph that have
    GPU work in that frame, not the encoders before the first pass or after the last pass."""
    frames: dict[str, dict[str, list[tuple[int, int]]]] = defaultdict(dict)
    for (buffer, name), spans in intervals.items():
        if name in passes:
            frames[buffer][name] = spans
    return frames


def window(frame: dict[str, list[tuple[int, int]]], first: str, second: str) -> "int | None":
    """The wall-clock GPU time of one frame from the end of the last interval of pass first to the start of the first
    interval of pass second that starts at or after that end, in ns. frame maps each pass of the frame to its intervals.

    None when the frame has no interval of first, or no interval of second that starts at or after first's end.
    """
    if not frame.get(first):
        return None
    end = max(end for _, end in frame[first])
    starts = [start for start, _ in frame.get(second, []) if start >= end]
    return min(starts) - end if starts else None


def counter_means(info: Path, values: Path, intervals: dict[tuple[str, str], list[tuple[int, int]]]):
    """The mean of each counter over the samples that fall inside each pass's GPU intervals: {pass: {counter: mean}}.

    A sample inside the intervals of several passes counts for each of them.
    """
    names = {row["counter-id"][1]: row["name"][1] for row in rows(info)}
    by_name: dict[str, list[tuple[int, int]]] = defaultdict(list)
    for (_, name), spans in intervals.items():
        by_name[name].extend(spans)
    cuts, segments = open_segments(by_name)
    sums: dict[str, dict[str, float]] = defaultdict(lambda: defaultdict(float))
    counts: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    for row in rows(values):
        at = bisect.bisect_right(cuts, int(row["timestamp"][1])) - 1
        if at < 0:
            continue
        counter = names[row["counter-id"][1]]
        for name in segments[at]:
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


def summarize(intervals: dict, passes: list[str], counters: dict, between: "tuple[str, str] | None" = None) -> dict:
    """The statistics of the passes over the frames, in ms, as passes.json holds them.

    intervals maps each (command buffer, pass) to its GPU intervals in ns, with BEFORE and AFTER for the encoders around
    the passes. passes names the passes in recording order, and counters maps a pass name to its counter means. Returns
    "frame", the summary of the frame spans; "passes", one entry per pass with GPU work, in the order BEFORE, passes,
    AFTER, with the summary of its busy time and an "alone" and a "shared" summary; "window" when between names two
    passes, with "from", "to", "missing" (the frames without a window) and its summary when a frame has one; and "idle",
    the passes with no GPU work in any frame. The shared time of a frame is the busy time minus the time alone.
    """
    busy: dict[str, list[float]] = defaultdict(list)
    alone: dict[str, list[float]] = defaultdict(list)
    shared: dict[str, list[float]] = defaultdict(list)
    for (_, name), (busy_ns, alone_ns) in busy_and_alone(intervals).items():
        busy[name].append(busy_ns / 1e6)
        alone[name].append(alone_ns / 1e6)
        shared[name].append((busy_ns - alone_ns) / 1e6)
    frames = frames_by_buffer(intervals, passes)
    summarized = {"frame": summary([frame_span(frame) / 1e6 for frame in frames.values()])}
    summarized["passes"] = [
        {
            "pass": name,
            **summary(busy[name]),
            "alone": summary(alone[name]),
            "shared": summary(shared[name]),
            "counters": counters.get(name, {}),
        }
        for name in [BEFORE, *passes, AFTER]
        if busy[name]
    ]
    if between:
        first, second = between
        found = [window(frame, first, second) for frame in frames.values()]
        times = [time / 1e6 for time in found if time is not None]
        summarized["window"] = {"from": first, "to": second, "missing": len(found) - len(times)}
        if times:
            summarized["window"] |= summary(times)
    # A pass of the frame graph that gave the GPU no encoder in any frame, such as the outlines with none to draw.
    summarized["idle"] = [name for name in passes if not busy[name]]
    return summarized


def bootstrap_error(values: list[float], resamples: int, seed: int) -> float:
    """The bootstrap standard error of the median of values, in the unit of the values.

    Each of the resamples draws len(values) values from values with replacement. The error is the standard deviation of
    the medians of the resamples. The draws come from random.Random(seed), so the same arguments give the same error.
    """
    draws = random.Random(seed)
    return statistics.stdev(statistics.median(draws.choices(values, k=len(values))) for _ in range(resamples))


def pairs(marks: "list[bool | None]") -> "tuple[list[int], list[int]]":
    """Pairs each frame marked False with its two neighbors, when both are marked True.

    marks holds one mark per frame, in the order of the trace: True for a frame on the "with" side, False for a frame on
    the "without" side, None for a frame that no pair can use. Returns the indices of the paired frames, and the indices
    of the frames left out: each frame not marked True that has no neighbor pair, and each frame marked None.
    """
    paired_frames: list[int] = []
    left_out: list[int] = []
    for index, mark in enumerate(marks):
        if mark is True:
            continue
        inside = 0 < index < len(marks) - 1
        if mark is False and inside and marks[index - 1] is True and marks[index + 1] is True:
            paired_frames.append(index)
        else:
            left_out.append(index)
    return paired_frames, left_out


def paired_median(values: "list[float | None]", paired_frames: list[int]) -> dict:
    """The paired statistic of one quantity, in ms. values holds the quantity of each frame in ms, in the order of the
    trace, and paired_frames the indices of the paired frames that pairs returned. Each pair gives the mean of the frame's two neighbors minus the
    frame. Returns "pairs", the number of differences, and, when there is one, "medianMs", their median, and
    "standardErrorMs", its bootstrap standard error from RESAMPLES resamples with the seed SEED."""
    differences = [(values[index - 1] + values[index + 1]) / 2 - values[index] for index in paired_frames]
    found: dict = {"pairs": len(differences)}
    if differences:
        found["medianMs"] = statistics.median(differences)
        found["standardErrorMs"] = bootstrap_error(differences, RESAMPLES, SEED)
    return found


def paired(intervals: dict, passes: list[str], name: str, control: bool = False, between=None) -> dict:
    """The paired statistics of a trace whose frames alternate with and without pass name, or of its control, in ms, as
    passes.json holds them under "paired".

    intervals and passes are those of summarize. The frames are counted from 0 in the order of their first GPU start.
    Without control, each frame in which name has no GPU work pairs with its two neighbors when name has GPU work in
    both. With control, each odd frame pairs with its two even neighbors, and a frame in which name has no GPU work
    pairs with none, so the control compares only frames that all run name. Each pair gives the mean of the two
    neighbors minus the frame: with name minus without it. A pass with no GPU work in a frame takes 0 ms there.

    Returns "pass", "control", "resamples" and "seed"; "frames", the number of frames; "pairs" and "leftOut", the
    number of pairs and the frames that no pair holds; "passes", one entry per pass with GPU work in the order of
    summarize, each with the paired_median of its "busy" time and its time "alone" (empty without a pair); "frame",
    the paired_median of the frame span; and "window" when between names two passes, with "from", "to", the
    paired_median of the window over the pairs whose three frames all have a window, and "leftOut", the other pairs.
    """
    frames = frames_by_buffer(intervals, passes)
    order = sorted(frames, key=lambda buffer: min(start for spans in frames[buffer].values() for start, _ in spans))
    if control:
        marks = [index % 2 == 0 if frames[buffer].get(name) else None for index, buffer in enumerate(order)]
    else:
        marks = [bool(frames[buffer].get(name)) for buffer in order]
    paired_frames, left_out = pairs(marks)

    rows = []
    if paired_frames:
        times = busy_and_alone(intervals)
        for row in [BEFORE, *passes, AFTER]:
            found = [times.get((buffer, row), (0, 0)) for buffer in order]
            if any(busy for busy, _ in found):
                rows.append(
                    {
                        "pass": row,
                        "busy": paired_median([busy / 1e6 for busy, _ in found], paired_frames),
                        "alone": paired_median([alone / 1e6 for _, alone in found], paired_frames),
                    }
                )
    result = {
        "pass": name,
        "control": control,
        "resamples": RESAMPLES,
        "seed": SEED,
        "frames": len(order),
        "pairs": len(paired_frames),
        "leftOut": left_out,
        "passes": rows,
        "frame": paired_median([frame_span(frames[buffer]) / 1e6 for buffer in order], paired_frames),
    }
    if between:
        first, second = between
        windows = [window(frames[buffer], first, second) for buffer in order]
        whole = [index for index in paired_frames if None not in windows[index - 1 : index + 2]]
        result["window"] = {
            "from": first,
            "to": second,
            **paired_median([None if time is None else time / 1e6 for time in windows], whole),
            "leftOut": [index for index in paired_frames if index not in whole],
        }
    return result


def table(report: dict, has_counters: bool) -> str:
    """The text of passes.md for a report: the header facts of passes.json and the result of summarize."""
    buffers = report["commandBuffers"]
    lines = [
        f"{report['device']}, {report['width']} x {report['height']}: {buffers['frames']} frames,"
        f" {buffers['skipped']} other command buffers.",
        "The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK"
        " the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md,"
        " \"GPU trace\").",
        "",
        "The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds:"
        " no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and"
        " the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with"
        " other passes, and passes.json gives its summary as \"shared\".",
        "",
        "The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.",
        "",
        "| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms)"
        " | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for entry in report["passes"]:
        top = limiters(entry["counters"])
        cells = [f"{name} {value:.0f} %" for name, value in top[:2]] + ["-"] * (2 - len(top[:2]))
        read = entry["counters"].get("GPU Read Bandwidth")
        write = entry["counters"].get("GPU Write Bandwidth")
        own = entry["alone"]
        lines.append(
            f"| {entry['pass']} | {entry['frames']} | {entry['meanMs']:.3f} | {entry['medianMs']:.3f} | {entry['p95Ms']:.3f}"
            f" | {own['meanMs']:.3f} | {own['medianMs']:.3f} | {own['p95Ms']:.3f}"
            f" | {cells[0]} | {cells[1]} | {'-' if read is None else f'{read:.1f}'} | {'-' if write is None else f'{write:.1f}'} |"
        )
    frame = report["frame"]
    lines.append(
        f"| {FRAME} | {frame['frames']} | {frame['meanMs']:.3f} | {frame['medianMs']:.3f} | {frame['p95Ms']:.3f} | | | | | | | |"
    )
    if "window" in report:
        found = report["window"]
        label = f"{found['from']} end to {found['to']} start"
        if "frames" in found:
            lines.append(
                f"| {label} | {found['frames']} | {found['meanMs']:.3f} | {found['medianMs']:.3f} | {found['p95Ms']:.3f}"
                " | | | | | | | |"
            )
        if found["missing"]:
            lines += [
                "",
                f"{found['missing']} frames have no window from {found['from']} to {found['to']}: one of the two passes has no"
                f" GPU work in them, or {found['to']} starts only before {found['from']} ends (tools/README.md, \"GPU trace\").",
            ]
    if report["idle"]:
        lines += ["", f"Passes with no GPU work in any frame: {', '.join(report['idle'])}."]
    if not has_counters:
        lines += ["", "The trace holds no GPU counters, so the table has no limiters (tools/README.md, \"GPU trace\")."]
    if "paired" in report:
        lines += ["", *paired_table(report["paired"])]
    return "\n".join(lines) + "\n"


def paired_table(found: dict) -> list[str]:
    """The lines of passes.md for the result of paired: a text that says what the pairs compare, and their table."""
    name = found["pass"]
    if found["control"]:
        lines = [
            f"Control: {found['pairs']} odd frames, each between two even frames, in a trace whose frames all run {name}."
            " Each pair gives the mean of the two even neighbors minus the odd frame, so the medians show the noise of"
            f" the paired method. A frame without GPU work of {name} pairs with none.",
        ]
    else:
        lines = [
            f"Paired: {found['pairs']} frames without GPU work of {name}, each between two frames with it. Each pair gives"
            f" the mean of the two neighbors minus the frame, so a positive value is the time that {name} adds.",
        ]
    lines.append(
        f"The paired median is the median of the pairs' differences in milliseconds, not the difference of two medians."
        f" Its standard error, in milliseconds, comes from a bootstrap of {found['resamples']} resamples with the seed"
        f" {found['seed']}. The frames are counted from 0 in the order of their first GPU start (tools/README.md,"
        " \"GPU trace\")."
    )
    if not found["pairs"]:
        return [*lines, "", "No frame has a neighbor pair, so the report has no paired medians."]

    def cells(statistic: dict) -> str:
        return f"{statistic['medianMs']:+.4f} | {statistic['standardErrorMs']:.4f}"

    lines += [
        "",
        "The frame span row and the window row give a paired wall-clock GPU time in the busy columns.",
        "",
        "| Pass | Pairs | Busy paired median (ms) | Busy standard error (ms) | Alone paired median (ms)"
        " | Alone standard error (ms) |",
        "|---|---|---|---|---|---|",
    ]
    for entry in found["passes"]:
        lines.append(f"| {entry['pass']} | {found['pairs']} | {cells(entry['busy'])} | {cells(entry['alone'])} |")
    lines.append(f"| {FRAME} | {found['pairs']} | {cells(found['frame'])} | | |")
    if "window" in found:
        between = found["window"]
        if between["pairs"]:
            lines.append(f"| {between['from']} end to {between['to']} start | {between['pairs']} | {cells(between)} | | |")
        if between["leftOut"]:
            lines += [
                "",
                f"Left out of the window row, because a frame of the pair has no window: frames"
                f" {', '.join(map(str, between['leftOut']))}.",
            ]
    if found["leftOut"]:
        lines += ["", f"Left out, with no neighbor pair: frames {', '.join(map(str, found['leftOut']))}."]
    return lines


def trim(args) -> int:
    script = json.loads(args.script.read_text())
    steps = []
    frame = 0
    end = args.start + args.frames if args.frames > 0 else math.inf
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
    unknown = [name for name in [*(args.window or []), args.paired or args.control] if name and name not in passes]
    if unknown:
        print(f"{', '.join(unknown)} is not a pass of {args.timings}: {', '.join(passes)}.", file=sys.stderr)
        return 1

    named, frames, skipped = passes_by_encoder(
        export(args.trace, "metal-application-encoders-list", exports), args.process, passes
    )
    if frames == 0:
        print(f"No command buffer of {args.process} holds {len(passes) + 1} timestamp encoders.", file=sys.stderr)
        return 1
    intervals = gpu_intervals(export(args.trace, "metal-gpu-intervals", exports), args.process, named)
    if not any(name in passes for _, name in intervals):
        print(f"The trace holds no GPU intervals for the passes of {args.process}.", file=sys.stderr)
        return 1

    info = export(args.trace, "gpu-counter-info", exports)
    has_counters = any(True for _ in rows(info))
    counters = counter_means(info, export(args.trace, "gpu-counter-value", exports), intervals) if has_counters else {}

    report = {"device": timings["device"], "width": timings["width"], "height": timings["height"]}
    report["commandBuffers"] = {"frames": frames, "skipped": skipped}
    report |= summarize(intervals, passes, counters, args.window)
    if args.paired or args.control:
        report["paired"] = paired(intervals, passes, args.paired or args.control, args.control is not None, args.window)
    (args.out / "passes.json").write_text(json.dumps(report, indent=2))
    text = table(report, has_counters)
    (args.out / "passes.md").write_text(text)
    print(text, end="")
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
    reporting.add_argument(
        "--window",
        nargs=2,
        metavar=("FROM", "TO"),
        help="also report each frame's wall-clock GPU time from the end of pass FROM to the start of pass TO",
    )
    pairing = reporting.add_mutually_exclusive_group()
    pairing.add_argument(
        "--paired",
        metavar="PASS",
        help="also report each frame without GPU work of PASS against the mean of its two neighbors with it, for a"
        " trace whose frames alternate with and without PASS",
    )
    pairing.add_argument(
        "--control",
        metavar="PASS",
        help="the control of --paired, for a trace whose frames all run PASS: each odd frame against the mean of its"
        " two even neighbors",
    )
    reporting.set_defaults(run=report)
    args = parser.parse_args()
    return args.run(args)


if __name__ == "__main__":
    sys.exit(main())
