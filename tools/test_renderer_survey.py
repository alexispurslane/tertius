#!/usr/bin/env python3
"""Tool code. The scripts and the measures of tools/renderer-survey.py, on synthetic scripts and images.

    nix develop --command python3 -I tools/test_renderer_survey.py

It needs NumPy, SciPy and Pillow, which the dev shell gives. The file loads renderer-survey.py by its path, because
python3 -I leaves the script's folder out of the import path and the file name holds a dash.
"""

import argparse
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import numpy as np

spec = importlib.util.spec_from_file_location("renderer_survey", Path(__file__).with_name("renderer-survey.py"))
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)


def script(*steps):
    return {"scene": "test", "window": {"width": 1280, "height": 720}, "steps": list(steps)}


RUN = lambda ticks: {"do": "run", "ticks": ticks}
CAPTURE = lambda name: {"do": "capture", "name": name}
FILM = lambda name, frames, every: {"do": "film", "name": name, "frames": frames, "every": every}
CAMERA = lambda entity: {"do": "camera", "entity": entity}
MARK = [{"do": "capture", "name": "mark"}]


def ticks_before_mark(built):
    ticks = 0
    for step in built["steps"]:
        if step == MARK[0]:
            return ticks
        if step["do"] == "run":
            ticks += step["ticks"]
        if step["do"] == "film":
            ticks += step["frames"] * step["every"]
    raise AssertionError("no mark")


class Views(unittest.TestCase):
    """views_script: a frame's state without a render, then the view's own steps."""

    def test_a_capture_after_a_run_keeps_the_run_and_drops_the_earlier_captures(self):
        source = script(CAMERA("a"), RUN(30), CAPTURE("one"), RUN(10), CAPTURE("two"), RUN(5), CAPTURE("three"))
        built, still = survey.views_script(source, {1: MARK})
        self.assertEqual(built["steps"], [CAMERA("a"), RUN(30), RUN(10), MARK[0]])
        self.assertTrue(still[1], "no frame was drawn before, so the view's first capture is a still")

    def test_a_film_frame_reaches_the_ticks_of_that_frame(self):
        source = script(RUN(4), FILM("f", 10, 3), CAPTURE("after"))
        built, _ = survey.views_script(source, {4: MARK})
        self.assertEqual(ticks_before_mark(built), 4 + 5 * 3, "film frame 4 shows the state after 5 of its steps")

    def test_the_steps_between_two_views_run_and_their_renders_do_not(self):
        source = script(CAPTURE("a"), RUN(2), CAMERA("b"), CAPTURE("b"))
        built, still = survey.views_script(source, {0: MARK, 1: MARK})
        self.assertEqual(built["steps"], [MARK[0], RUN(2), CAMERA("b"), MARK[0]])
        self.assertTrue(still[0])
        self.assertFalse(still[1], "two ticks after a drawn frame is no still: the step is under 0.09 s")

    def test_a_view_six_ticks_after_a_drawn_frame_is_a_still(self):
        source = script(CAPTURE("a"), RUN(6), CAPTURE("b"))
        _, still = survey.views_script(source, {0: MARK, 1: MARK})
        self.assertTrue(still[1])

    def test_a_frame_past_the_end_is_refused(self):
        with self.assertRaises(ValueError):
            survey.views_script(script(CAPTURE("a")), {1: MARK})

    def test_the_shown_still_is_the_first_capture_with_120_draws(self):
        self.assertEqual(survey.still_draws(True, survey.still_capture_index(True)), 132)
        self.assertEqual(survey.still_draws(False, survey.still_capture_index(False)), 133)

    def test_a_timing_film_draws_frames_one_tick_apart(self):
        built = survey.timing_script(script(RUN(100), CAPTURE("a")), 0)
        self.assertEqual(built["steps"][-1], FILM("t", survey.TIMING_FRAMES, 1))

    def test_a_motion_film_draws_a_frame_every_tick_through_a_script_film_of_six_ticks_per_frame(self):
        built = survey.motion_script(script(FILM("f", 10, 6)), 2, 12)
        start = [s for s in built["steps"] if s["do"] == "film"]
        before = sum(s["ticks"] for s in built["steps"] if s["do"] == "run")
        self.assertEqual(before, 3 * 6, "the state of frame 2")
        self.assertEqual(len(start), 12)
        self.assertTrue(all(f["frames"] == 1 and f["every"] == 1 for f in start), "no step between draws reaches 6 ticks")
        self.assertFalse([s for s in built["steps"][built["steps"].index(start[0]):] if s["do"] == "run"])

    def test_a_motion_film_draws_the_ticks_of_a_run_and_keeps_a_camera_step(self):
        built = survey.motion_script(script(CAPTURE("a"), RUN(3), CAMERA("b"), CAPTURE("c")), 0, 10)
        after = built["steps"]
        self.assertEqual([s["do"] for s in after], ["film", "film", "film", "film", "camera", "film"])
        self.assertTrue(all(s["every"] == 1 for s in after if s["do"] == "film"))


class MotionReferences(unittest.TestCase):
    def test_a_reference_reaches_the_state_of_its_film_frame_and_names_its_still_of_120_draws(self):
        motion = script(RUN(30), FILM("m0000", 1, 1), FILM("m0001", 1, 1), FILM("m0002", 1, 1))
        built, names = survey.motion_references_script(motion, [2])
        mark = built["steps"].index({"do": "capture", "name": "r0002-s00"})
        ticks = sum(s["ticks"] for s in built["steps"][:mark] if s["do"] == "run")
        self.assertEqual(ticks, 33, "the state of the third film frame")
        self.assertEqual(names[2], "r0002-s03", "the first capture is a still, so the fourth has 132 draws")


class Switches(unittest.TestCase):
    def test_the_switch_turns_the_lamps_off_for_200_draws_from_the_draw_of_its_film_frame(self):
        runs = survey.switch_runs({"select": "x:1:2", "sun": "off"})
        first = survey.STILL_DRAWS + survey.SWITCH_OFF
        self.assertEqual(runs["switch"]["TERTIUS_PROBE_LAMP_OFF"], f"{first}-{first + 200}")
        self.assertNotIn("TERTIUS_PROBE_LAMP_OFF", runs["on"])
        self.assertTrue(all(r["TERTIUS_PROBE_SUN"] == "off" for r in runs.values()))

    def test_the_film_follows_the_lamps_for_100_draws_after_they_come_back_on(self):
        film = survey.switch_script({"scene": "s", "camera": None, "ticks": 10})["steps"][-1]
        self.assertEqual(film["frames"] - survey.SWITCH_ON, 100)


WALK = {"scene": "s", "camera": "person", "ticks": 60, "key": 87, "stand": 180, "walk": 120, "after": 200}


def ticks_at_frame(built, film, frame):
    """The world ticks that the frame of the named film shows: the runs and the frames of every film before it."""
    ticks = 0
    for step in built["steps"]:
        if step["do"] == "run":
            ticks += step["ticks"]
        if step["do"] == "film":
            if step["name"] == film:
                if frame >= step["frames"]:
                    raise AssertionError(f"film {film} has {step['frames']} frames, none numbered {frame}")
                return ticks + (frame + 1) * step["every"]
            ticks += step["frames"] * step["every"]
    raise AssertionError(f"no film {film}")


def posts(built, kind):
    return [i for i, s in enumerate(built["steps"]) if s["do"] == "post" and s["payload"]["kind"] == kind]


def tick_of_post(built, kind):
    """The tick on which the world routes the named post: the ticks that run before it, plus 1."""
    ticks = 0
    for step in built["steps"]:
        if step["do"] == "post" and step["payload"]["kind"] == kind:
            return ticks + 1
        if step["do"] == "run":
            ticks += step["ticks"]
        if step["do"] == "film":
            ticks += step["frames"] * step["every"]
    raise AssertionError(f"no post {kind}")


class ProbeSeries(unittest.TestCase):
    """The probe writes each capture's region means as one line of a series file, and a PNG only for the kept names
    (ticket ter-d7qy)."""

    def write_series(self, folder, lines):
        (Path(folder) / survey.SERIES_FILE).write_text("".join(line + "\n" for line in lines))

    def test_a_films_series_holds_its_frames_in_frame_order_and_no_other_capture(self):
        with tempfile.TemporaryDirectory() as folder:
            self.write_series(folder, ["still 9 9", "f-000001 2 0.25", "g-000000 7 7", "f-000000 1 0.5"])
            self.assertEqual(survey.probe_series(Path(folder), "f"), [[1.0, 0.5], [2.0, 0.25]])

    def test_a_still_is_read_by_its_capture_name(self):
        with tempfile.TemporaryDirectory() as folder:
            self.write_series(folder, ["k0004-s02 1 1", "k0004-s03 3 0.125"])
            self.assertEqual(survey.probe_rows(Path(folder))["k0004-s03"], [3.0, 0.125])

    def test_a_switch_measures_its_series_and_keeps_the_pngs_of_the_four_report_frames_only(self):
        rendered = {}

        def render(work, script, *, binary, env, extra):
            # Each film frame's whole-image mean is its frame number, so that the kept PNGs name their frames.
            work.mkdir(parents=True)
            film = script["steps"][-1]
            kept = env["TERTIUS_PROBE_KEEP"].split(",")
            rows = [f"{survey.film_frame('f', k)} {k + 1} " + " ".join(["1"] * 9) for k in range(film["frames"])]
            self.write_series(work, ["still 1 " + " ".join(["1"] * 9)] + rows)
            for name in kept:
                (work / f"{name}.png").write_text(name)
            rendered[work.name] = env["TERTIUS_PROBE_SERIES"]

        case = {"name": "c", "scene": "s", "camera": None, "ticks": 10, "select": "all", "sun": "on"}
        with tempfile.TemporaryDirectory() as folder, \
                mock.patch.object(survey, "render", render), \
                mock.patch.object(survey, "probe_binary", lambda out: Path("probe")), \
                mock.patch.object(survey, "save", lambda *a: None):
            out = Path(folder)
            result = survey.run_switch(out, case, argparse.Namespace(exposure="4"))
            kept = sorted(p.name for p in (out / "images" / "switch").iterdir())
            frames = [survey.SWITCH_OFF - 1, survey.SWITCH_OFF + 8, survey.SWITCH_ON - 1, survey.SWITCH_ON + 8]
            self.assertEqual(kept, sorted(f"c-{k:03d}.png" for k in frames))
            self.assertEqual((out / "images" / "switch" / f"c-{frames[1]:03d}.png").read_text(),
                             survey.film_frame("f", frames[1]))
        self.assertEqual(len(result["series"]["switch"]), survey.SWITCH_FRAMES)
        self.assertEqual(result["series"]["on"][7][0], 8.0)
        self.assertTrue(all(path.endswith(survey.SERIES_FILE) for path in rendered.values()))


class Walks(unittest.TestCase):
    def test_the_walk_stands_and_draws_before_it_walks_and_films_its_settle_window_after_the_stop(self):
        built = survey.walk_script(WALK)
        films = {s["name"]: s["frames"] for s in built["steps"] if s["do"] == "film"}
        self.assertEqual(films, {"f": 180, "g": 120, "h": 200})
        self.assertEqual(ticks_at_frame(built, "h", 0), 60 + 180 + 120 + 1, "the first frame after the stop")

    def test_the_control_walks_undrawn_and_then_stands_where_the_walk_ends_through_the_walks_settle_window(self):
        built = survey.walk_control_script(WALK)
        first_draw = next(i for i, s in enumerate(built["steps"]) if s["do"] in ("capture", "film"))
        self.assertTrue(posts(built, "keyDown")[0] < posts(built, "keyUp")[0] < first_draw, "it walks before it draws")
        walk = survey.walk_script(WALK)
        for k in (0, 63, 199):
            self.assertEqual(ticks_at_frame(built, "c", survey.walk_control_frame(WALK, k)), ticks_at_frame(walk, "h", k))

    def test_both_runs_hold_the_key_for_the_walks_ticks_and_the_walk_lets_go_later_by_its_stand(self):
        for built in (survey.walk_script(WALK), survey.walk_control_script(WALK)):
            self.assertEqual(tick_of_post(built, "keyUp") - tick_of_post(built, "keyDown"), 120)
        self.assertEqual(tick_of_post(survey.walk_script(WALK), "keyUp") - tick_of_post(survey.walk_control_script(WALK), "keyUp"),
                         180, "the walk lets go later by its stand, which the control's frames make up")

    def test_the_control_draws_the_walks_stand_before_the_window_so_its_own_start_settles_first(self):
        control = survey.walk_control_script(WALK)
        before = ticks_at_frame(survey.walk_script(WALK), "h", 0) - ticks_at_frame(control, "c", 0)
        self.assertEqual(before, 180, "the control draws 180 frames before the window's first frame")

    def test_a_change_of_light_that_the_control_shows_too_is_no_part_of_the_walks_settle(self):
        control = [[1.0, 2.0]] * 3 + [[0.6, 1.2]] * 7
        after = [[1.5, 2.0], [1.2, 2.0]] + control[2:]
        self.assertEqual(survey.walk_settle(after, control), [2, 0], "the step at frame 3 shows in both")


class Settle(unittest.TestCase):
    def test_a_region_whose_control_is_dark_settles_when_it_is_dark_too(self):
        self.assertEqual(survey.settle_draws([0.5, 0.0, 0.0], [0.0] * 3, 0, 3), 1)

    def test_a_series_that_stays_within_the_tolerance_settles_at_once(self):
        self.assertEqual(survey.settle_draws([1.0] * 10, [1.02] * 10, 0, 10), 0)

    def test_the_settle_ends_at_the_last_frame_outside_the_tolerance(self):
        series = [2.0, 1.5, 1.2, 1.04, 1.1, 1.0, 1.0]
        self.assertEqual(survey.settle_draws(series, [1.0] * 7, 0, 7), 5, "frame 4 is the last one outside 5 %")

    def test_a_series_still_outside_the_tolerance_at_the_windows_end_did_not_settle(self):
        series = [2.0, 1.0, 1.0, 1.2]
        self.assertIsNone(survey.settle_draws(series, [1.0] * 4, 0, 4), "no count, not the window's length of 4")

    def test_a_series_inside_the_tolerance_only_at_the_windows_last_frame_settles_there(self):
        self.assertEqual(survey.settle_draws([2.0, 2.0, 2.0, 1.0], [1.0] * 4, 0, 4), 3)

    def test_the_slowest_region_decides_and_one_that_did_not_settle_decides_alone(self):
        self.assertEqual(survey.slowest_settle([4, 40, 12]), 40)
        self.assertIsNone(survey.slowest_settle([4, None, 12]))
        self.assertEqual(survey.slowest_settle([]), 0, "no changed region leaves nothing to settle")

    def test_a_count_that_did_not_settle_reads_as_not_settled_within_the_window(self):
        self.assertEqual(survey.settle_text(None, 200), "not settled within 200 draws")
        self.assertEqual(survey.settle_text(48, 200), "48")

    def test_only_regions_that_the_switch_changes_count(self):
        self.assertEqual(survey.changed_regions([1.0, 0.5, 0.95], [1.0, 1.0, 1.0]), [1])

    def test_a_film_that_trails_the_still_lags_by_the_difference_over_the_change_per_draw(self):
        self.assertAlmostEqual(survey.lag(film=1.0, still=1.1, slope=0.01), 10.0)

    def test_a_light_that_barely_changes_gives_no_lag(self):
        self.assertIsNone(survey.lag(film=1.0, still=1.001, slope=0.0001))


def flat(value=0.2, shape=(96, 128)):
    return np.full(shape, value, dtype=np.float32)


class Measures(unittest.TestCase):
    def test_a_lone_bright_pixel_is_a_speck_and_one_that_the_reference_holds_is_not(self):
        reference = flat()
        reference[10, 10] = 0.9
        still = reference.copy()
        still[50, 60] = 0.9
        visible = int(survey.visible(reference).sum())
        self.assertAlmostEqual(survey.specks(still, reference), 1e4 / visible)

    def test_an_image_whose_every_pixel_is_near_an_emitter_measures_no_blotch(self):
        self.assertEqual(survey.blotch(flat(0.9), flat(0.9)), (None, None))

    def test_a_film_with_no_measured_frame_has_no_flicker(self):
        self.assertEqual(survey.flicker(flat(0.9) for _ in range(5)), (None, None))

    def test_an_image_equal_to_its_reference_has_no_blotch(self):
        self.assertEqual(survey.blotch(flat(), flat())[0], 0.0)

    def test_a_wide_bright_patch_is_a_blotch(self):
        still = flat()
        still[30:60, 40:80] *= 1.2
        score, _ = survey.blotch(still, flat())
        self.assertGreater(score, survey.BLOTCH_LIMIT)

    def test_the_bloom_of_a_lamp_that_changes_from_draw_to_draw_is_no_blotch(self):
        reference = flat()
        reference[40:44, 60:64] = 0.95
        still = reference.copy()
        still[30:56, 48:76] *= 1.5
        still[40:44, 60:64] = 0.95
        self.assertLess(survey.blotch(still, reference)[0], survey.BLOTCH_LIMIT)

    def test_a_brighter_whole_image_is_a_level_change_and_no_blotch(self):
        still = flat() * 1.03
        self.assertAlmostEqual(survey.level(still, flat()), 0.03, places=4)
        self.assertLess(survey.blotch(still, flat())[0], 1e-4)

    def test_grain_without_low_frequency_error_hardly_moves_the_blotch(self):
        rng = np.random.default_rng(1)
        still = flat() * (1 + 0.1 * rng.standard_normal((96, 128)).astype(np.float32))
        self.assertLess(survey.blotch(still, flat())[0], survey.BLOTCH_LIMIT / 2)

    def test_a_light_that_rises_steadily_does_not_flicker(self):
        low, grain = survey.flicker(flat(0.1 + 0.01 * t) for t in range(10))
        self.assertLess(low, 1e-6)
        self.assertLess(grain, 1e-6)

    def test_a_light_that_pumps_from_frame_to_frame_flickers(self):
        low, _ = survey.flicker(flat(0.2 * (1.1 if t % 2 else 1.0)) for t in range(10))
        self.assertGreater(low, survey.FLICKER_LIMIT)

    def test_a_moving_object_adds_at_most_its_share_of_the_image_times_the_cap(self):
        frames = []
        for t in range(9):
            y = flat()
            y[:, 8 * t:8 * t + 8] = 0.9
            frames.append(y)
        low, grain = survey.flicker(frames)
        self.assertLessEqual(grain, 3 * 8 / 128 * survey.FLICKER_CAP + 1e-6)

    def test_region_means_give_the_whole_image_first_and_then_each_cell(self):
        y = np.zeros((6, 6), dtype=np.float32)
        y[:2, :2] = 9.0
        means = survey.region_means(y)
        self.assertEqual(len(means), 1 + survey.GRID ** 2)
        self.assertAlmostEqual(means[0], 1.0)
        self.assertAlmostEqual(means[1], 9.0)


def renders(direct, bounce, reflections, after=None):
    """The four renders of SIGNALS for given parts of the light: the passes after the composite add to each render."""
    after = np.zeros_like(direct) if after is None else after
    return {"all": direct + bounce + reflections + after, "direct": direct + after, "bounce": bounce + after,
            "reflections": reflections + after}


class Split(unittest.TestCase):
    """The split by light signal: which part of the light carries a film's error against its still."""

    def test_the_light_after_the_composite_is_taken_out_of_each_signals_render(self):
        parts = survey.parts_of(renders(flat(0.3), flat(0.2), flat(0.1), after=flat(0.05)))
        for name, value in (("direct", 0.3), ("bounce", 0.2), ("reflections", 0.1), ("after", 0.05)):
            self.assertTrue(np.allclose(parts[name], value), name)

    def test_a_patch_in_the_bounce_light_is_the_bounce_lights_blotch_alone(self):
        still = renders(flat(0.3), flat(0.2), flat(0.1), after=flat(0.02))
        bounce = flat(0.2)
        bounce[20:70, 30:100] *= 2
        film = renders(flat(0.3), bounce, flat(0.1), after=flat(0.02))
        errors = survey.signal_errors(film, still, np.ones_like(bounce, dtype=bool))
        parts = errors["parts"]
        self.assertGreater(parts["bounce"]["blotch"], survey.BLOTCH_LIMIT)
        for name in ("direct", "reflections", "after"):
            self.assertLess(parts[name]["blotch"], 1e-3, name)
        self.assertAlmostEqual(parts["bounce"]["share"], 0.2 / 0.62, places=4)

    def test_a_signal_that_trails_its_still_shows_its_own_level(self):
        still = renders(flat(0.3), flat(0.2), flat(0.1))
        film = renders(flat(0.3), flat(0.1), flat(0.1))
        parts = survey.signal_errors(film, still, np.ones((96, 128), dtype=bool))["parts"]
        self.assertAlmostEqual(parts["bounce"]["level"], -0.5, places=4)
        self.assertAlmostEqual(parts["direct"]["level"], 0.0, places=4)
        self.assertIsNone(parts["after"]["level"], "no light after the composite gives no level")

    def test_a_part_that_only_brightens_shows_in_its_own_level_and_no_other_part_gets_a_blotch(self):
        still = renders(flat(0.3), flat(0.2), flat(0.1))
        film = renders(flat(0.3), flat(0.6), flat(0.1))
        parts = survey.signal_errors(film, still, np.ones((96, 128), dtype=bool))["parts"]
        self.assertAlmostEqual(parts["bounce"]["level"], 2.0, places=4)
        for name in ("direct", "bounce", "reflections"):
            self.assertLess(parts[name]["blotch"], 1e-4, name)

    def test_a_pumping_reflection_is_the_reflections_flicker_and_no_other_parts(self):
        frames = [renders(flat(0.3), flat(0.2), flat(0.1 * (1.5 if t % 2 else 1.0))) for t in range(9)]
        flicker = survey.signal_flicker(frames)
        self.assertGreater(flicker["reflections"], survey.FLICKER_LIMIT)
        self.assertAlmostEqual(flicker["reflections"], flicker["all"], places=6)
        for name in ("direct", "bounce", "after"):
            self.assertLess(flicker[name], 1e-6, name)

    def test_the_scene_floor_shows_as_the_display_floor_of_the_blotch_score(self):
        self.assertAlmostEqual(float(survey.display(np.float32(survey.SCENE_FLOOR))), survey.RELATIVE_FLOOR, places=4)


class Verdicts(unittest.TestCase):
    def test_a_moving_light_whose_film_errs_by_more_than_the_limit_fails_though_its_lag_is_short(self):
        rows = [{"error": -0.3, "lagDraws": 20.0}, {"error": -0.2, "lagDraws": 25.0}, {"error": 0.0, "lagDraws": None}]
        self.assertEqual(survey.lag_verdict({"lagDraws": 22.0, "rows": rows}), ["lag error"])

    def test_a_moving_light_that_tracks_within_the_limit_passes(self):
        rows = [{"error": 0.02, "lagDraws": 4.0}, {"error": -0.05, "lagDraws": 9.0}]
        self.assertEqual(survey.lag_verdict({"lagDraws": 6.0, "rows": rows}), [])

    def test_a_frame_whose_p95_exceeds_the_budget_at_the_windowed_clock_fails(self):
        self.assertEqual(survey.timing_verdict({"p95AtWindowedMs": 12.1}), ["time"])
        self.assertEqual(survey.timing_verdict({"p95AtWindowedMs": 11.9}), [])

    def test_a_film_darker_than_its_still_by_more_than_the_lag_limit_fails(self):
        film = {"flicker": 0.0, "grainFlicker": 0.0, "blotch": 0.0, "level": -0.2}
        self.assertEqual(survey.motion_verdict(film), ["motion level"])
        self.assertEqual(survey.motion_verdict({**film, "level": -0.05}), [])

    def test_a_settle_over_the_goal_fails_and_so_does_one_that_did_not_settle_within_its_window(self):
        self.assertEqual(survey.settle_verdict(61), ["settle"])
        self.assertEqual(survey.settle_verdict(60), [])
        self.assertEqual(survey.settle_verdict(None), ["settle"])

    def test_a_moving_light_that_lags_more_than_the_goal_fails_and_a_case_too_steady_for_a_lag_measured_nothing(self):
        self.assertEqual(survey.lag_verdict({"lagDraws": 61.0, "rows": []}), ["lag"])
        self.assertEqual(survey.lag_verdict({"lagDraws": None, "rows": []}), [survey.NOT_MEASURED])

    def test_a_view_whose_clock_is_unknown_is_judged_by_its_raw_p95(self):
        self.assertEqual(survey.timing_verdict({"p95Ms": 30.0, "clock": {"unknown": "no gpu-clock"}}), ["time"])
        self.assertEqual(survey.timing_verdict({"p95Ms": 9.0}), [])

    def test_a_still_or_film_that_measured_nothing_fails(self):
        self.assertEqual(survey.still_verdict({"specks": 0.0, "blotch": None, "level": 0.0}), [survey.NOT_MEASURED])
        film = {"flicker": None, "grainFlicker": None, "blotch": None, "level": 0.0}
        self.assertEqual(survey.motion_verdict(film), [survey.NOT_MEASURED])

class TheGpuLock(unittest.TestCase):
    """Timed runs hold the GPU alone. Untimed renders share it (tools/README.md, "The GPU lock")."""

    def lock_calls(self, call):
        """The commands of tools/gpu-lock.sh that `call` runs, with every subprocess faked. A fake run writes an empty
        log where the helper would."""
        calls = []

        def fake(command, *args, **kwargs):
            command = [str(c) for c in command]
            if command[:2] == ["bash", str(survey.ROOT / "tools" / "gpu-lock.sh")]:
                calls.append(command)
                log = command[3] if command[2] == "--shared" else command[2]
                Path(log).write_text("")
            return subprocess.CompletedProcess(command, 0, "", "")

        with mock.patch.object(survey.subprocess, "run", fake), mock.patch.object(survey, "free_gb", lambda: 100.0), \
                mock.patch.dict(os.environ, {survey.HOLDS_LOCK: ""}):
            call()
        return calls

    def test_an_untimed_render_shares_the_gpu_and_a_timed_one_holds_it_alone(self):
        with tempfile.TemporaryDirectory() as folder:
            for timed, shared in ((False, True), (True, False)):
                with self.subTest(timed=timed):
                    out = Path(folder) / f"timed-{timed}"
                    calls = self.lock_calls(lambda: survey.render(out, script(), binary=Path("demo"), timed=timed))
                    self.assertEqual(len(calls), 1)
                    self.assertEqual(calls[0][2] == "--shared", shared)

    def test_a_chunk_shares_the_gpu_unless_it_times_views(self):
        with tempfile.TemporaryDirectory() as folder:
            args = argparse.Namespace(out=folder, no_build=True, parts="timing,still,motion", only=None, chunk=1000,
                                      exposure="4.08", clock_bin=None)
            calls = self.lock_calls(lambda: survey.run_chunks(args))
            parts = {c[c.index("--parts") + 1]: c[2] == "--shared" for c in calls}
            self.assertEqual(parts, {"timing": False, "still": True, "motion": True})


    def test_a_run_inside_a_take_of_the_lock_renders_in_this_process_and_does_not_queue_behind_itself(self):
        # On 2026-11-11 a caller that held the lock started run with its default chunks. run queued behind the caller,
        # and the queue stalled for 11 minutes (t1-1zic).
        with tempfile.TemporaryDirectory() as folder:
            args = argparse.Namespace(out=folder, no_build=True, parts="still", only=["no-such-case"], chunk=8,
                                      exposure="4.08", clock_bin=None)
            for holds, chunked in (("1", False), ("", True)):
                with self.subTest(holds=holds), mock.patch.object(survey, "run_chunks", return_value=0) as chunks, \
                        mock.patch.dict(os.environ, {survey.HOLDS_LOCK: holds}):
                    survey.run(args)
                    self.assertEqual(chunks.called, chunked)

if __name__ == "__main__":
    unittest.main()
