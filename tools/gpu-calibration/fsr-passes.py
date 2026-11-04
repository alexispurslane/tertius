"""Tool code (tools/README.md, "GPU calibration"): the GPU time of each of FSR's passes, from a Metal System Trace of
FSR's batched measurement that fsr-trace.sh records.

Usage: python3 -I tools/gpu-calibration/fsr-passes.py <run.trace> <out folder>

The measurement submits 45 batches of 8 frames, each batch in one command buffer, with a timestamp before each frame
and after the last. MoltenVK writes each timestamp as an encoder of its own, "Record GPU Counter Sample BlitEncoder",
and ends its compute encoder at each pipeline barrier. FSR's Record puts a barrier before each dispatch, so a frame
between two stamps holds nine compute encoders in order: the exposure step, AMD's reactive mask generator and the seven
passes of Fsr3Upscaler.UpscalePasses. Its other encoders are AMD's clears and the update of the constants.

A pass's time is its busy time: the union of its encoder's GPU intervals. The script leaves out:
- the first 8 command buffers with stamps, the warm-up batches that the measurement does not time;
- a frame with another count of dispatches;
- a frame in which another process's GPU interval overlaps the frame's span, because the GPU then runs both, and the
  other work slows the pass. Other sessions' test runs take no GPU lock.

It prints the median (10th to 90th percentile) of each pass over the kept frames, and of the frame span, the time from
the frame's stamp to the next stamp, and it writes the times of every kept frame to <out>/passes.json.

Under a trace, the clock moves between the states of 972 and 1296 MHz from one frame to the next, and the times of each
pass fall into two clusters. The second table holds the frames at the top clock: those whose summed pass time lies
within 10 % of the 10th percentile of all kept frames. A frame at 972 MHz takes at least 1.25 times as long. If
<out>/run.log holds gpu-clock's samples and the measurement's markers, the script also prints the clock over the timed
span beside the clock that the two clusters imply, the share of top-clock frames times 1296 MHz plus the rest times 972.
"""

import importlib.util
import json
import re
import statistics
import sys
from collections import defaultdict
from pathlib import Path

PASSES = ["Exposure", "GenerateReactive", "PrepareInputs", "LumaPyramid", "ShadingChangePyramid", "ShadingChange",
          "PrepareReactivity", "LumaInstability", "Accumulate"]
STAMP = "Record GPU Counter Sample BlitEncoder"
DISPATCH = "vkCmdDispatch ComputeEncoder"
OTHER = "(clears and constants)"
SPAN = "(frame span)"
WARM_UP_BUFFERS = 8


def busy(intervals):
    """The length of the union of [start, end) intervals."""
    total = 0
    end = None
    for start, stop in sorted(intervals):
        if end is None or start > end:
            total += stop - start
            end = stop
        elif stop > end:
            total += stop - end
            end = stop
    return total


def pass_frames(encoders, other, skip_buffers):
    """The times of each kept frame, [{pass name or OTHER or SPAN: ns}].

    encoders holds (command buffer, order, label, [(start, end)]) for each encoder of the measurement's process, in
    nanoseconds. order sorts the encoders of a command buffer in recording order. other holds the GPU intervals of every
    other process. The first skip_buffers command buffers with stamps, in the order of their first start, hold the
    warm-up.
    """
    by_buffer = defaultdict(list)
    for buffer, order, label, spans in encoders:
        by_buffer[buffer].append((order, label, spans))
    # Only a command buffer with stamps holds a batch. The others, such as the uploads, are no warm-up batch.
    batches = [listed for listed in by_buffer.values() if any(label == STAMP for _, label, _ in listed)]
    buffers = sorted(batches, key=lambda listed: min(start for _, _, spans in listed for start, _ in spans))
    others = sorted(other)
    kept = []
    for listed in buffers[skip_buffers:]:
        listed.sort()
        stamps = [index for index, (_, label, _) in enumerate(listed) if label == STAMP]
        for first, last in zip(stamps, stamps[1:]):
            inside = listed[first + 1 : last]
            dispatches = [spans for _, label, spans in inside if label == DISPATCH]
            if len(dispatches) != len(PASSES):
                continue
            start = listed[first][2][0][0]
            end = listed[last][2][0][0]
            if any(a < end and b > start for a, b in others):
                continue
            times = {name: busy(spans) for name, spans in zip(PASSES, dispatches)}
            times[OTHER] = busy([interval for _, label, spans in inside if label != DISPATCH for interval in spans])
            times[SPAN] = end - start
            kept.append(times)
    return kept


def top_clock_frames(frames):
    """The frames whose summed pass time lies within 10 % of the 10th percentile of frames: the frames that ran at the top
    clock, when at least a tenth of the frames did."""
    sums = sorted(sum(each[name] for name in PASSES) for each in frames)
    bound = 1.1 * sums[min(len(sums) - 1, len(sums) // 10)]
    return [each for each in frames if sum(each[name] for name in PASSES) <= bound]


def trace_encoders(trace: Path, out: Path):
    """The encoders of the process with the most stamps in the trace's GPU intervals, and the intervals of every other
    process. The trace's list of encoders holds only the launched process, gpu-clock, so the encoders come from the
    labels and ids of the intervals."""
    spec = importlib.util.spec_from_file_location("gpu_trace", Path(__file__).parent.parent / "gpu-trace.py")
    gpu_trace = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gpu_trace)
    table = gpu_trace.export(trace, "metal-gpu-intervals", out)
    spans = defaultdict(list)
    labels = {}
    stamps = defaultdict(int)
    for row in gpu_trace.rows(table):
        process = row.get("process", (None, None))[0] or ""
        start = int(row["start"][1])
        interval = (start, start + int(row["duration"][1]))
        label = row["event-label"][0] or ""
        match = re.search(r":(.*?)\s+\(", label)
        key = (process, row["cmdbuffer-id"][0], row["encoder-id"][0])
        spans[key].append(interval)
        labels[key] = match[1].strip() if match else label
        if labels[key] == STAMP:
            stamps[process] += 1
    measured = max(stamps, key=stamps.get)
    encoders = []
    other = []
    for key, intervals in spans.items():
        if key[0] == measured:
            # An encoder's id grows in the order of recording within its command buffer.
            encoders.append((key[1], int(key[2], 0) if key[2] else 0, labels[key], intervals))
        else:
            other += intervals
    return measured, encoders, other


def spread(values):
    ordered = sorted(values)
    at = lambda fraction: ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]
    return f"{at(0.5) / 1e6:.4f} ({at(0.1) / 1e6:.4f} to {at(0.9) / 1e6:.4f})"


def main() -> int:
    trace = Path(sys.argv[1])
    out = Path(sys.argv[2])
    exports = out / "export"
    exports.mkdir(parents=True, exist_ok=True)
    process, encoders, other = trace_encoders(trace, exports)
    frames = pass_frames(encoders, other, WARM_UP_BUFFERS)
    (out / "passes.json").write_text(json.dumps({"process": process, "frames": frames}))
    top = top_clock_frames(frames)
    print(f"{process}: {len(frames)} frames kept of the timed batches, with no other process on the GPU")
    print(f"{len(top)} of them, {len(top) / len(frames):.3f}, ran at the top clock")
    log = out / "run.log"
    if log.exists():
        spec = importlib.util.spec_from_file_location("gpu_clock", Path(__file__).parent.parent / "gpu_clock.py")
        gpu_clock = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(gpu_clock)
        span = gpu_clock.span_clock(log.read_text()).describe()
        implied = len(top) / len(frames) * 1296 + (1 - len(top) / len(frames)) * 972
        print(f"The clock over the timed span: {span}. The clock that the clusters imply: {implied:.1f} MHz")
    print("\n| Pass | Busy time of every kept frame, median (p10 to p90), ms | At the top clock, ms |")
    print("|---|---|---|")
    for name in PASSES + [OTHER, SPAN]:
        print(f"| {name} | {spread([each[name] for each in frames])} | {spread([each[name] for each in top])} |")
    for label, chosen in (("every kept frame", frames), ("the frames at the top clock", top)):
        medians = sum(statistics.median(each[name] for each in chosen) for name in PASSES + [OTHER])
        print(f"\nThe sum of the medians of the passes, the clears and the constants, {label}: {medians / 1e6:.4f} ms")
    return 0


if __name__ == "__main__":
    sys.exit(main())
