#!/usr/bin/env python3
"""Tool code. The tests of the summaries of tools/gpu-calibration/analyze.py, on synthetic logs of gpu-clock, of
MoltenVK's performance log and of the busy sampler.

    nix develop --command python3 -I tools/test_gpu_calibration.py

The standard library only. The file loads analyze.py and fsr-passes.py by their paths, because a hyphen in a folder or
file name stops an import.
"""

import importlib.util
import unittest
from pathlib import Path

def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parent / "gpu-calibration" / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


analyze = _load("analyze", "analyze.py")
fsr_passes = _load("fsr_passes", "fsr-passes.py")
work = _load("work", "work.py")


def sample(t: float, active: float, mhz: float) -> str:
    return f"sample t={t:.3f} active={active:.3f} mhz={mhz:.1f} states=0,0,0,0,0,1"


PRESENT = "[mvk-info] Performance statistics reporting every: 60 frames, avg FPS: 119.34, elapsed time: 3401380.902 seconds:"


class WindowSummary(unittest.TestCase):
    """window_summary: the steady span of a window run starts 5 s after the start and ends at the last sample before
    MoltenVK's last log of presented frames, after which the demo tears down."""

    def test_a_sample_after_ten_seconds_counts_in_the_steady_span(self):
        log = "\n".join([sample(4.0, 1.0, 648.0), sample(9.5, 1.0, 1000.0), sample(19.0, 1.0, 1296.0), PRESENT])
        summary = analyze.window_summary(log, "")
        self.assertEqual(summary["steady_samples"], 2)
        self.assertAlmostEqual(summary["steady_mhz"], 1148.0)

    def test_samples_after_the_last_log_of_presented_frames_do_not_count(self):
        log = "\n".join([sample(6.0, 1.0, 1296.0), PRESENT, sample(7.0, 0.2, 648.0)])
        summary = analyze.window_summary(log, "")
        self.assertEqual(summary["steady_samples"], 1)
        self.assertAlmostEqual(summary["steady_mhz"], 1296.0)

    def test_the_steady_clock_weights_each_sample_by_its_active_share(self):
        log = "\n".join([sample(6.0, 1.0, 1296.0), sample(7.0, 0.5, 648.0), PRESENT])
        summary = analyze.window_summary(log, "")
        self.assertAlmostEqual(summary["steady_mhz"], (1296.0 + 0.5 * 648.0) / 1.5)
        self.assertAlmostEqual(summary["steady_active"], 0.75)

    def test_the_busy_share_averages_the_busy_samples_of_the_steady_span(self):
        log = "\n".join([sample(6.0, 1.0, 1296.0), sample(12.0, 1.0, 1296.0), PRESENT, sample(13.0, 0.1, 648.0)])
        busy = "\n".join(["busy t=3.000 percent=10", "busy t=6.000 percent=90", "busy t=11.500 percent=100", "busy t=12.500 percent=0"])
        self.assertAlmostEqual(analyze.window_summary(log, busy)["steady_busy"], 95.0)


class PooledProbes(unittest.TestCase):
    """pooled_probes: the dispatch times of each probe name pooled over logs, with the share of the peak at the pooled
    median, from the work that each probe line implies (its rate times its median)."""

    def test_the_share_follows_the_pooled_median(self):
        logs = [
            "probe=fma-v4-s1 api=metal dispatches=2 median=10.0000 p10=10.0000 p90=10.0000 rate=1.0e+12 FLOP/s share=0.5000\ntimes=10.0 10.0",
            "probe=fma-v4-s1 api=metal dispatches=2 median=20.0000 p10=20.0000 p90=20.0000 rate=5.0e+11 FLOP/s share=0.2500\ntimes=20.0 20.0 20.0",
        ]
        pooled = analyze.pooled_probes(logs)
        self.assertEqual(pooled["fma-v4-s1"]["dispatches"], 5)
        self.assertAlmostEqual(pooled["fma-v4-s1"]["median"], 20.0)
        self.assertAlmostEqual(pooled["fma-v4-s1"]["share"], 0.25)


STAMP = "Record GPU Counter Sample BlitEncoder"
DISPATCH = "vkCmdDispatch ComputeEncoder"
CLEAR = "vkCmdClearColorImage RenderEncoder"


def buffer(name: str, frames: list, start: int = 0):
    """The encoders of a command buffer, (buffer, order, label, intervals in ns), of frames given as (dispatches, clears):
    for each frame a stamp of 1 ns, the exposure step and the reactive mask, the clears, then the other dispatches, each
    10 ns long, and a stamp at the end."""
    encoders = []
    time = start
    for dispatches, clears in frames:
        encoders.append((STAMP, [(time, time + 1)]))
        time += 1
        for label in [DISPATCH, DISPATCH] + [CLEAR] * clears + [DISPATCH] * (dispatches - 2):
            encoders.append((label, [(time, time + 10)]))
            time += 10
    encoders.append((STAMP, [(time, time + 1)]))
    return [(name, order, label, spans) for order, (label, spans) in enumerate(encoders)]


class FsrPassFrames(unittest.TestCase):
    """pass_frames: each frame of FSR's batched measurement lies between two stamps of a command buffer, and its
    dispatches in order are the exposure step, AMD's reactive mask generator and the seven passes."""

    def test_the_dispatches_of_a_frame_take_the_names_of_the_passes_in_their_order(self):
        frames = fsr_passes.pass_frames(buffer("a", [(9, 1)]), other=[], skip_buffers=0)
        self.assertEqual([name for name in frames[0] if name in fsr_passes.PASSES], fsr_passes.PASSES)
        self.assertEqual(frames[0]["Accumulate"], 10)
        self.assertEqual(frames[0]["(clears and constants)"], 10)

    def test_a_frame_with_another_count_of_dispatches_has_no_values(self):
        frames = fsr_passes.pass_frames(buffer("a", [(8, 1)]), other=[], skip_buffers=0)
        self.assertEqual(frames, [])

    def test_a_frame_that_another_process_overlaps_has_no_values(self):
        encoders = buffer("a", [(9, 1), (9, 1)])
        second_start = encoders[11][3][0][0]
        frames = fsr_passes.pass_frames(encoders, other=[(second_start + 5, second_start + 6)], skip_buffers=0)
        self.assertEqual(len(frames), 1)

    def test_the_warm_up_buffers_have_no_values(self):
        encoders = buffer("a", [(9, 1)]) + buffer("b", [(9, 1)], start=10_000)
        self.assertEqual(len(fsr_passes.pass_frames(encoders, other=[], skip_buffers=1)), 1)

    def test_a_command_buffer_without_stamps_does_not_count_as_a_warm_up_buffer(self):
        upload = [("upload", 0, "vkCmdCopyBufferToImage BlitEncoder", [(0, 5)])]
        encoders = upload + buffer("a", [(9, 1)], start=100) + buffer("b", [(9, 1)], start=10_000)
        self.assertEqual(len(fsr_passes.pass_frames(encoders, other=[], skip_buffers=1)), 1)

    def test_a_frames_span_runs_from_its_stamp_to_the_next(self):
        frames = fsr_passes.pass_frames(buffer("a", [(9, 1)]), other=[], skip_buffers=0)
        self.assertEqual(frames[0]["(frame span)"], 101)


class FsrTopClockFrames(unittest.TestCase):
    """top_clock_frames: the frames that ran at the top clock, whose summed pass time lies within 10 % of the 10th
    percentile of all frames. Under a trace the clock moves between 972 and 1296 MHz, and the frames at 972 MHz take at
    least 1.25 times as long."""

    def test_the_slow_cluster_is_left_out(self):
        fast = [{name: 100 for name in fsr_passes.PASSES} for _ in range(3)]
        slow = [{name: 125 for name in fsr_passes.PASSES} for _ in range(7)]
        self.assertEqual(fsr_passes.top_clock_frames(fast + slow), fast)

    def test_a_frame_within_ten_percent_of_the_fastest_counts(self):
        frames = [{name: time for name in fsr_passes.PASSES} for time in (100, 100, 109, 125, 125, 125, 125, 125, 125, 125)]
        self.assertEqual(len(fsr_passes.top_clock_frames(frames)), 3)


AIR = """define void @main0(ptr %0) {
  %2 = fmul fast <4 x float> %a, %b
  %3 = fadd fast float %c, %d
  %4 = tail call fast <4 x float> @air.fma.v4f32(<4 x float> %2, <4 x float> %2, <4 x float> %2)
  %5 = tail call <4 x float> @air.sample_texture_2d.v4f32(ptr %t, ptr %s, <2 x float> %u)
  %6 = tail call <4 x float> @air.read_texture_2d.v4f32(ptr %t, <2 x i32> %i)
  tail call void @air.write_texture_2d.v4f32(ptr %t, <2 x i32> %i, <4 x float> %6)
  %7 = extractelement <4 x float> %5, i64 0
  %8 = load float, ptr addrspace(1) %p
  br label %9

9:
  %10 = icmp ult i32 %x, 4
  br i1 %10, label %9, label %11

11:
  ret void
}
"""


class CountedWork(unittest.TestCase):
    """count_air: a kernel's lane operations count each instruction's vector width, its texture reads and writes count
    one per call, and a branch back to an earlier block counts as a loop."""

    def test_an_operation_counts_its_vector_width(self):
        self.assertEqual(work.count_air(AIR)["main0"]["alu"], 4 + 1 + 4 + 1)

    def test_each_sample_and_read_counts_as_one_texture_read(self):
        self.assertEqual(work.count_air(AIR)["main0"]["texture"], 2)

    def test_a_write_counts_apart_from_the_reads(self):
        self.assertEqual(work.count_air(AIR)["main0"]["writes"], 1)

    def test_a_branch_back_to_an_earlier_block_is_a_loop(self):
        self.assertEqual(work.count_air(AIR)["main0"]["loops"], 1)


class Roofline(unittest.TestCase):
    """roofline: the prediction of a pass is the largest of its three quotients of work over the measured rates."""

    def test_the_largest_quotient_is_the_prediction_and_names_the_bound(self):
        terms = work.roofline(alu=work.ALU_RATE * 1e-3, texture=work.TEXEL_RATE * 2e-3, bytes_moved=work.BYTE_RATE * 0.5e-3)
        self.assertAlmostEqual(terms["prediction_ms"], 2.0)
        self.assertEqual(terms["bound"], "texture")


if __name__ == "__main__":
    unittest.main()
