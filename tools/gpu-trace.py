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
neighbors with it, as paired medians with block bootstrap standard errors. --control PASS gives the same table for a
trace whose frames all run PASS, with each odd frame against its two even neighbors, so it shows the noise of the
method.
per_frame computes the quantities of each frame, summarize and paired reduce them to the statistics, and table formats
passes.md. tools/test_gpu_trace.py tests them and the pure functions below them.
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
BLOCK_PAIRS = 6
"""The number of consecutive pairs in each block of the paired mode's moving-block bootstrap.

Neighboring pairs share a frame, so their differences correlate, and pairs two apart share none. A block of 6 pairs
keeps 5 of every 6 neighbor correlations, and also the correlation of a drift within the 13 frames that it spans. The
rule: the block length that brings the smaller of two mean ratios closest to 1. The ratios are those of the error to
the real standard error of the paired median, in the simulation of ter-2fri with 100 pairs and independent frame noise.
One ratio has noise in every frame, as in the rows of the other passes, and one has none in the frames without the
pass, as in the row of the pass itself. For blocks of 1, 4, 5, 6, 8 and 10 pairs, the smaller ratio, always the second,
averaged 0.792, 0.954, 0.960, 0.964, 0.954 and 0.945 over two sets of traces, highest at 6. Longer blocks leave too few
blocks in 100 pairs. 5 and 6 differ by less than the noise of the simulation, and 6 also keeps more of a slow drift.
"""
MIN_ERROR_PAIRS = 5 * BLOCK_PAIRS
"""The fewest differences for which the paired mode gives a standard error: 5 blocks.

With fewer blocks, the moving-block bootstrap error is much too small. In the simulation of ter-2fri, its mean ratio to
the real standard error with noise only in the frames with the pass was 0.54 for 12 pairs, 0.74 for 18, 0.82 for 24
and 0.85 for 30, and it nears 1 only slowly above that.
"""
SEED = 1
"""The seed of the paired mode's bootstrap, fixed so that two reports of one trace give the same standard errors."""
LISTED = 10
"""The number of frames that a line of passes.md names before it gives only the count of the rest."""


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
    """The GPU intervals of each pass in each command buffer, {(buffer, pass): [(start, end)]}, and the first GPU start
    of each frame of the trace, {buffer: start}, in nanoseconds.

    A frame of the trace is a command buffer of the process whose GPU intervals hold a timestamp encoder, because the
    renderer writes stamps in each frame's command buffer and in no other. The table labels each interval with its
    encoder, so a frame counts even when the trace lost its rows in the encoder list, which named comes from. Then the
    frame has no intervals of a pass, and it keeps its place in the order of the frames (per_frame).
    """
    by_pass: dict[tuple[str, str], list[tuple[int, int]]] = defaultdict(list)
    first: dict[str, int] = {}
    stamped: set[str] = set()
    for row in rows(table):
        if not of_process(row, process):
            continue
        buffer = row["cmdbuffer-id"][0]
        start = int(row["start"][1])
        first[buffer] = min(first.get(buffer, start), start)
        if STAMP in (row["event-label"][0] or ""):
            stamped.add(buffer)
        found = named.get(row["encoder-id"][0])
        if found is not None:
            by_pass[found].append((start, start + int(row["duration"][1])))
    return {key: union(spans) for key, spans in by_pass.items()}, {buffer: first[buffer] for buffer in stamped}


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


def per_frame(intervals: dict, passes: list[str], between: "tuple[str, str] | None" = None,
              starts: "dict[str, int] | None" = None) -> dict:
    """The quantities of each frame of the trace, in ns, that summarize and paired reduce.

    intervals and passes are those of summarize. starts holds the first GPU start of each frame of the trace (see
    gpu_intervals), also of a frame in which no pass has an interval because the trace lost its encoders. Without
    starts, the frames are the command buffers in which a pass of the frame graph has GPU work. The frames are in the
    order of their first GPU start, so a frame that the report cannot read keeps its place, and the frames after it
    keep their index. Returns "rows", {row: one (busy, alone) per frame}, with None for a frame in which the row has no
    interval, for each of BEFORE, passes and AFTER that has an interval in some frame, in that order; "span", the frame
    span of each frame, None for a frame without an interval of a pass; and "window" when between names two passes, the
    window of each frame or None (see window).
    """
    frames = frames_by_buffer(intervals, passes)
    first = {buffer: min(start for spans in frames[buffer].values() for start, _ in spans) for buffer in frames}
    first |= starts or {}
    order = sorted(first, key=first.get)
    times = busy_and_alone(intervals)
    found: dict = {"rows": {}, "span": [frame_span(frames[buffer]) if buffer in frames else None for buffer in order]}
    for row in [BEFORE, *passes, AFTER]:
        values = [times.get((buffer, row)) for buffer in order]
        if any(value is not None for value in values):
            found["rows"][row] = values
    if between:
        found["window"] = [window(frames.get(buffer, {}), *between) for buffer in order]
    return found


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


def summarize(intervals: dict, passes: list[str], counters: dict, between: "tuple[str, str] | None" = None,
              starts: "dict[str, int] | None" = None) -> dict:
    """The statistics of the passes over the frames, in ms, as passes.json holds them.

    intervals maps each (command buffer, pass) to its GPU intervals in ns, with BEFORE and AFTER for the encoders around
    the passes. passes names the passes in recording order, and counters maps a pass name to its counter means. starts
    is that of per_frame. Returns "frame", the summary of the frame spans; "passes", one entry per pass with GPU work, in
    the order BEFORE, passes, AFTER, with the summary of its busy time and an "alone" and a "shared" summary; "window"
    when between names two passes, with "from", "to", "missing" (the frames without a window) and its summary when a
    frame has one; "unread", the index of each frame of starts that has no interval of a pass, which no statistic
    counts; and "idle", the passes with no GPU work in any frame. The shared time of a frame is the busy time minus the
    time alone.
    """
    frames = per_frame(intervals, passes, between, starts)
    spans = [span / 1e6 for span in frames["span"] if span is not None]
    summarized: dict = {"frame": summary(spans), "passes": []}
    for name, values in frames["rows"].items():
        found = [value for value in values if value is not None]
        summarized["passes"].append(
            {
                "pass": name,
                **summary([busy / 1e6 for busy, _ in found]),
                "alone": summary([alone / 1e6 for _, alone in found]),
                "shared": summary([(busy - alone) / 1e6 for busy, alone in found]),
                "counters": counters.get(name, {}),
            }
        )
    if between:
        first, second = between
        times = [time / 1e6 for time in frames["window"] if time is not None]
        summarized["window"] = {"from": first, "to": second, "missing": len(spans) - len(times)}
        if times:
            summarized["window"] |= summary(times)
    summarized["unread"] = [index for index, span in enumerate(frames["span"]) if span is None]
    # A pass of the frame graph that gave the GPU no encoder in any frame, such as the outlines with none to draw.
    summarized["idle"] = [name for name in passes if name not in frames["rows"]]
    return summarized


def bootstrap_error(values: list[float], resamples: int, seed: int) -> float:
    """The moving-block bootstrap standard error of the median of values, in the unit of the values. values is a series
    in its order, such as the paired differences in the order of the trace.

    values holds at least BLOCK_PAIRS values. Each of the resamples joins blocks of BLOCK_PAIRS consecutive values, each
    from a start drawn with replacement, until it holds len(values) values, and drops the values past that count. The
    error is the standard deviation of the medians of the resamples. The draws come from random.Random(seed), so the same
    arguments give the same error.
    """
    draws = random.Random(seed)
    medians = []
    for _ in range(resamples):
        resample: list[float] = []
        while len(resample) < len(values):
            start = draws.randrange(len(values) - BLOCK_PAIRS + 1)
            resample += values[start:start + BLOCK_PAIRS]
        medians.append(statistics.median(resample[:len(values)]))
    return statistics.stdev(medians)


def pairs(marks: "list[bool | None]") -> "tuple[list[tuple[int, int, int]], list[int]]":
    """Pairs each frame marked False with its two neighbors, when both are marked True.

    marks holds one mark per frame, in the order of the trace: True for a frame on the "with" side, False for a frame on
    the "without" side, None for a frame that no pair can use. Returns the pairs as (before, frame, after) index
    triples, and the indices of the frames left out: each frame not marked True that makes no pair, which includes
    each frame marked None.
    """
    found: list[tuple[int, int, int]] = []
    left_out: list[int] = []
    for index, mark in enumerate(marks):
        if mark is True:
            continue
        inside = 0 < index < len(marks) - 1
        if mark is False and inside and marks[index - 1] is True and marks[index + 1] is True:
            found.append((index - 1, index, index + 1))
        else:
            left_out.append(index)
    return found, left_out


def paired_median(values: "list[float | None]", triples: "list[tuple[int, int, int]]") -> dict:
    """The paired statistic of one quantity, in the unit of values. values holds the quantity of each frame in the
    order of the trace, None where the frame has none, and triples the pairs that pairs returned.

    Each pair whose three frames all have a value gives the mean of the two neighbors minus the frame. Returns "pairs",
    the number of these differences; "leftOut", the middle frame of each other pair; when there is a difference,
    "medianMs", their median; and, when there are at least MIN_ERROR_PAIRS differences, "standardErrorMs", its
    moving-block bootstrap standard error (bootstrap_error) from RESAMPLES resamples with the seed SEED. The bootstrap
    takes the differences in the order of triples, the order of the trace. A pair left out leaves a gap, and a block can
    hold the differences on its two sides. Those two share no frame, and the block keeps them as they are, so it adds no
    correlation that the trace does not have.
    """
    differences: list[float] = []
    left_out: list[int] = []
    for before, frame, after in triples:
        if None in (values[before], values[frame], values[after]):
            left_out.append(frame)
        else:
            differences.append((values[before] + values[after]) / 2 - values[frame])
    found: dict = {"pairs": len(differences), "leftOut": left_out}
    if differences:
        found["medianMs"] = statistics.median(differences)
    if len(differences) >= MIN_ERROR_PAIRS:
        found["standardErrorMs"] = bootstrap_error(differences, RESAMPLES, SEED)
    return found


def paired(intervals: dict, passes: list[str], name: str, control: bool = False,
           between: "tuple[str, str] | None" = None, starts: "dict[str, int] | None" = None) -> dict:
    """The paired statistics of a trace whose frames alternate with and without pass name, or of its control, in ms, as
    passes.json holds them under "paired".

    intervals, passes and starts are those of summarize, and the frames those of per_frame, counted from 0. Without
    control, each frame in which name has no GPU work pairs with its two neighbors when name has GPU work in both. With
    control, each odd frame pairs with its two even neighbors, and a frame in which name has no GPU work pairs with none,
    so the control compares only frames that all run name. A frame that the report cannot read (summarize, "unread")
    is in no pair, so the control's odd frames after it are those of the trace. Each pair gives the mean of the two
    neighbors minus the frame: with name minus without it. A pass with no GPU work in a frame takes 0 ms there.

    Returns "pass", "control", "resamples", "blockPairs" (BLOCK_PAIRS), "seed" and "minErrorPairs" (MIN_ERROR_PAIRS);
    "frames", the number of frames; "pairs", the number of pairs; "leftOut", the frames not on the "with" side that make
    no pair (pairs), without the frames that the report cannot read; "parity", "odd", "even" or "odd and even", the
    parity of the paired frames, when there is a pair; "passes", one entry per row of per_frame, each with the
    paired_median of its "busy" time and its time "alone" (empty without a pair); "frame", the paired_median of the frame
    span; and "window" when between names two passes, with "from", "to" and the paired_median of the window.
    """
    frames = per_frame(intervals, passes, between, starts)
    read = [span is not None for span in frames["span"]]
    count = len(read)
    with_name = [value is not None for value in frames["rows"].get(name, [None] * count)]
    if control:
        marks = [index % 2 == 0 if runs else None for index, runs in enumerate(with_name)]
    else:
        marks = [runs if read[index] else None for index, runs in enumerate(with_name)]
    triples, left_out = pairs(marks)
    left_out = [index for index in left_out if read[index]]

    def ms(values: list) -> list:
        return [None if value is None else value / 1e6 for value in values]

    rows = []
    for row, values in frames["rows"].items() if triples else []:
        times = [(0, 0) if value is None else value for value in values]
        busy = paired_median(ms([busy for busy, _ in times]), triples)
        rows.append({"pass": row, "busy": busy, "alone": paired_median(ms([alone for _, alone in times]), triples)})
    result = {
        "pass": name,
        "control": control,
        "resamples": RESAMPLES,
        "blockPairs": BLOCK_PAIRS,
        "seed": SEED,
        "minErrorPairs": MIN_ERROR_PAIRS,
        "frames": count,
        "pairs": len(triples),
        "leftOut": left_out,
        "passes": rows,
        "frame": paired_median(ms(frames["span"]), triples),
    }
    if triples:
        result["parity"] = " and ".join(sorted({"odd" if frame % 2 else "even" for _, frame, _ in triples}, reverse=True))
    if between:
        result["window"] = {"from": between[0], "to": between[1], **paired_median(ms(frames["window"]), triples)}
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
    if report["unread"]:
        lines += [
            "",
            f"The report cannot read the passes of {frame_list(report['unread'])}: the trace lost some of their encoders,"
            " or they hold another number of timestamp encoders. Each keeps its place in the order of the frames and gives"
            " no values (tools/README.md, \"GPU trace\").",
        ]
    if report["idle"]:
        lines += ["", f"Passes with no GPU work in any frame: {', '.join(report['idle'])}."]
    if not has_counters:
        lines += ["", "The trace holds no GPU counters, so the table has no limiters (tools/README.md, \"GPU trace\")."]
    if "paired" in report:
        lines += ["", *paired_table(report["paired"])]
    return "\n".join(lines) + "\n"


def frame_list(indices: list[int]) -> str:
    """Names frames by their indices for a line of passes.md: all of them, or the count and the first LISTED."""
    shown = ", ".join(map(str, indices[:LISTED]))
    if len(indices) <= LISTED:
        return f"frames {shown}"
    return f"{len(indices)} frames, {shown} and {len(indices) - LISTED} more (passes.json lists all)"


def paired_table(found: dict) -> list[str]:
    """The lines of passes.md for the result of paired: a text that says what the pairs compare, the frames left out,
    and the table."""
    name = found["pass"]
    if found["control"]:
        lines = [
            f"Control: {found['pairs']} odd frames, each between two even frames, in a trace whose frames all run {name}."
            " Each pair gives the mean of the two even neighbors minus the odd frame, so the medians show the noise of"
            f" the paired method. A frame without GPU work of {name} pairs with none.",
        ]
        reason = f"an odd frame without two even neighbors that run {name}, or a frame without GPU work of {name}"
    else:
        lines = [
            f"Paired: {found['pairs']} frames without GPU work of {name}, each between two frames with it. Each pair gives"
            f" the mean of the two neighbors minus the frame, so a positive value is the time that {name} adds.",
        ]
        if found["pairs"]:
            lines[0] += f" The frames without {name} are the {found['parity']} frames."
        reason = "with no neighbor pair"
    lines.append(
        f"The paired median is the median of the pairs' differences in milliseconds, not the difference of two medians."
        f" Its standard error, in milliseconds, comes from a moving-block bootstrap of {found['resamples']} resamples, with"
        f" blocks of {found['blockPairs']} consecutive pairs and the seed {found['seed']}. Neighboring pairs share a frame,"
        " and the blocks keep that correlation. A drift that correlates pairs farther apart than a block still makes the"
        " errors too small, and a cycle of a pass's work over the frames can make them too small or too large: read them"
        f" beside the control's medians, which show the real noise floor. A row of fewer than {found['minErrorPairs']}"
        " pairs gives no standard error, because the bootstrap of so few blocks gives one that is much too small. The"
        " frames are counted from 0 in the order of their first GPU start (tools/README.md, \"GPU trace\")."
    )
    if found["pairs"]:
        lines += table_of_pairs(found)
    else:
        lines += ["", "No frame has a neighbor pair, so the report has no paired medians."]
    if found["leftOut"]:
        lines += ["", f"Left out, {reason}: {frame_list(found['leftOut'])}."]
    return lines


def table_of_pairs(found: dict) -> list[str]:
    """The lines of the paired table for a result of paired with at least one pair, and the pairs that its window row
    leaves out."""

    def cells(statistic: dict) -> str:
        error = f"{statistic['standardErrorMs']:.4f}" if "standardErrorMs" in statistic else ""
        return f"{statistic['medianMs']:+.4f} | {error}"

    lines = [
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
                "Left out of the window row, because a frame of the pair has no window: the pairs around"
                f" {frame_list(between['leftOut'])}.",
            ]
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
    named = [*(("--window", name) for name in args.window or []), ("--paired", args.paired), ("--control", args.control)]
    unknown = [(option, name) for option, name in named if name is not None and name not in passes]
    if unknown:
        for option, name in unknown:
            print(f"{option} {name}: {name} is not a pass of the run.", file=sys.stderr)
        print(f"The passes of {args.timings}: {', '.join(passes)}.", file=sys.stderr)
        return 1

    named, frames, skipped = passes_by_encoder(
        export(args.trace, "metal-application-encoders-list", exports), args.process, passes
    )
    if frames == 0:
        print(f"No command buffer of {args.process} holds {len(passes) + 1} timestamp encoders.", file=sys.stderr)
        return 1
    intervals, starts = gpu_intervals(export(args.trace, "metal-gpu-intervals", exports), args.process, named)
    if not any(name in passes for _, name in intervals):
        print(f"The trace holds no GPU intervals for the passes of {args.process}.", file=sys.stderr)
        return 1

    info = export(args.trace, "gpu-counter-info", exports)
    has_counters = any(True for _ in rows(info))
    counters = counter_means(info, export(args.trace, "gpu-counter-value", exports), intervals) if has_counters else {}

    report = {"device": timings["device"], "width": timings["width"], "height": timings["height"]}
    report["commandBuffers"] = {"frames": frames, "skipped": skipped}
    report |= summarize(intervals, passes, counters, args.window, starts)
    if args.paired or args.control:
        name = args.paired or args.control
        report["paired"] = paired(intervals, passes, name, args.control is not None, args.window, starts)
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
