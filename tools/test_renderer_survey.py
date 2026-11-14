#!/usr/bin/env python3
"""Tool code. The scripts and the measures of tools/renderer-survey.py, on synthetic scripts and images.

    nix develop --command python3 -I tools/test_renderer_survey.py

It needs NumPy, SciPy and Pillow, which the dev shell gives. The file loads renderer-survey.py by its path, because
python3 -I leaves the script's folder out of the import path and the file name holds a dash.
"""

import argparse
import importlib.util
import io
import json
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


SWITCH = {"name": "cabin", "scene": "s", "camera": "person", "ticks": 60, "select": "z:-12:4", "sun": "on"}


class SwitchRunsInParts(unittest.TestCase):
    """A switch case's three runs can render in separate calls, such as separate workbench jobs of at most 840 s: each
    call keeps the series of its runs, and the call that finds all three measures them."""

    def render(self, calls):
        def render(out, built, *, binary, env=None, extra=(), **kwargs):
            calls.append(env)
            out.mkdir(parents=True)
            frames = built["steps"][-1]["frames"]
            write_series(out, {survey.film_frame("f", k): 1.0 for k in range(frames)})
            return out.parent / f"{out.name}.log"
        return render

    def test_a_call_limited_to_some_runs_renders_only_those_keeps_their_series_and_saves_no_result(self):
        calls = []
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(survey, "render", self.render(calls)), \
                mock.patch.object(survey, "probe_binary", lambda out: Path("probe")), \
                mock.patch.dict(os.environ, {survey.SWITCH_RUNS: "off"}):
            result = survey.run_switch(Path(folder), SWITCH, argparse.Namespace(exposure="4.08"))
            kept = sorted(p.name for p in (Path(folder) / "series" / "switch").iterdir())
            saved = (Path(folder) / "results" / "switch" / "cabin.json").exists()
        self.assertIsNone(result)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0]["TERTIUS_PROBE_LAMP_OFF"], "0-100000000", "the run that holds the lamps off")
        self.assertEqual(kept, ["cabin-off.json"])
        self.assertFalse(saved)

    def test_a_call_that_finds_all_three_series_kept_measures_them_without_a_binary(self):
        calls = []
        with tempfile.TemporaryDirectory() as folder:
            kept = Path(folder) / "series" / "switch"
            kept.mkdir(parents=True)
            frames = survey.SWITCH_FRAMES
            for label, value in (("switch", 1.0), ("off", 0.5), ("on", 1.0)):
                (kept / f"cabin-{label}.json").write_text(json.dumps([[value] * (1 + survey.GRID ** 2)] * frames))

            def no_binary(out):
                raise SystemExit("no binary")

            with mock.patch.object(survey, "render", self.render(calls)), \
                    mock.patch.object(survey, "probe_binary", no_binary):
                result = survey.run_switch(Path(folder), SWITCH, argparse.Namespace(exposure="4.08"))
            saved = (Path(folder) / "results" / "switch" / "cabin.json").exists()
        self.assertEqual(calls, [])
        self.assertTrue(saved)
        self.assertEqual(len(result["series"]["off"]), frames)


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


STAND = {"name": "cabin-stand", "scene": "s", "camera": "person", "ticks": 60, "signal": "bounce", "film": 600, "end": 10,
         "settleTick": 306, "region": "r11"}
CLIMB = {"name": "cabin-climb", "scene": "s", "camera": "person", "states": [300, 306], "stills": 12, "signal": "bounce",
         "exposure": "3.15", "region": "r11"}
# The index of region r11, the centre cell of the 3 x 3 grid, in a row of region_means.
BAND = 5


def captures_of(built):
    return [s["name"] for s in built["steps"] if s["do"] == "capture"]


def ticks_before_capture(built, name):
    ticks = 0
    for step in built["steps"]:
        if step["do"] == "capture" and step["name"] == name:
            return ticks
        if step["do"] == "run":
            ticks += step["ticks"]
        if step["do"] == "film":
            ticks += step["frames"] * step["every"]
    raise AssertionError(f"no capture {name}")


def rows_with_band(band, rest=1.0):
    """Rows of region means whose band region follows `band` and whose other regions hold `rest`."""
    return [[rest] * BAND + [b] + [rest] * (9 - BAND) for b in band]


class Stands(unittest.TestCase):
    """The stand film of ter-g3tk: the cabin walk's view without the walk, one light signal, against its end level."""

    def test_the_stand_draws_a_still_and_then_one_film_frame_a_tick_from_the_tick_after_its_head(self):
        built = survey.stand_script(STAND)
        self.assertEqual({s["name"]: s["frames"] for s in built["steps"] if s["do"] == "film"}, {"f": 600})
        self.assertEqual(captures_of(built)[0], "still")
        self.assertEqual(ticks_at_frame(built, "f", 0), 61)

    def test_the_end_stills_follow_the_film_without_a_tick_so_they_continue_its_history(self):
        built = survey.stand_script(STAND)
        film = next(i for i, s in enumerate(built["steps"]) if s["do"] == "film")
        self.assertEqual(built["steps"][film + 1:], survey.captures("end", 10))

    def test_the_settle_after_a_tick_counts_from_the_film_frame_that_shows_that_tick(self):
        frame = survey.stand_frame(STAND, 306)
        self.assertEqual(ticks_at_frame(survey.stand_script(STAND), "f", frame), 306)

    def test_the_excess_of_each_window_of_40_draws_is_its_mean_light_over_the_end_level(self):
        end = [[2.0] * 10] * 5 + [[1.0] * 10] * 5
        film = [[1.5] * 10] * 40 + [[1.25] * 10] * 40 + [[1.0] * 10] * 520
        result = survey.stand_result(STAND, film, end)
        self.assertEqual(result["end"], [1.0] * 10, "the end level is the mean of the last half of the end stills")
        self.assertEqual(len(result["excess"]), 15)
        self.assertEqual([row[BAND] for row in result["excess"][:3]], [0.5, 0.25, 0.0])

    def test_each_region_settles_when_it_stays_within_5_percent_of_its_end_level(self):
        end = [[1.0] * 10] * 10
        film = rows_with_band([1.5] * 300 + [1.0] * 300)
        result = survey.stand_result(STAND, film, end)
        self.assertEqual(result["settleDraws"][BAND], 300)
        self.assertEqual(result["settleDraws"][0], 0)
        self.assertEqual(result["settleDrawsAfterTick"][BAND], 300 - 245, "frame 245 shows tick 306")
        self.assertEqual(result["bandSettleDraws"], 55)
        self.assertEqual(result["failed"], [])

    def test_the_band_that_settles_later_than_60_draws_after_the_settle_tick_fails(self):
        end = [[1.0] * 10] * 10
        film = rows_with_band([1.5] * (245 + 61) + [1.0] * (600 - 245 - 61))
        self.assertEqual(survey.stand_result(STAND, film, end)["failed"], ["settle"])

    def test_a_region_other_than_the_band_that_wanders_to_the_films_end_does_not_fail_the_stand(self):
        end = [[1.0] * 10] * 10
        film = [[1.0] * 10 for _ in range(600)]
        for k in range(500, 600):
            film[k][4] = 1.1
        result = survey.stand_result(STAND, film, end)
        self.assertIsNone(result["settleDraws"][4], "r10 is still outside at the film's end")
        self.assertEqual(result["failed"], [])


class Climbs(unittest.TestCase):
    """The climb test of ter-g3tk: continued stills of fixed states, with the diffuse filter and without it."""

    def test_each_state_gets_its_continued_stills_at_its_world_tick(self):
        built = survey.climb_script(CLIMB)
        self.assertEqual(captures_of(built), captures_of({"steps": survey.captures("t300", 12) + survey.captures("t306", 12)}))
        for state in (300, 306):
            for k in (0, 11):
                self.assertEqual(ticks_before_capture(built, f"t{state}-s{k:02d}"), state)

    def test_the_climb_renders_the_bounce_light_alone_with_the_filter_and_without_it(self):
        runs = survey.climb_runs(CLIMB, "3.15")
        self.assertEqual(set(runs), {"filtered", "unfiltered"})
        for env in runs.values():
            self.assertEqual(env["TERTIUS_PROBE_SIGNAL"], survey.SIGNALS["bounce"])
            self.assertEqual(env["TERTIUS_PROBE_STILL_CONTINUES"], "1")
            self.assertEqual(env["TERTIUS_PROBE_EXPOSURE"], "3.15")
        self.assertEqual(runs["unfiltered"]["TERTIUS_DIFFUSE_FILTER"], "off")
        self.assertNotIn("TERTIUS_DIFFUSE_FILTER", runs["filtered"], "the filtered run keeps the renderer's kernel")

    def climb(self, filtered, unfiltered, later=None):
        later = later or filtered
        return survey.climb_result(CLIMB, {"filtered": {"t300": rows_with_band(filtered), "t306": rows_with_band(later)},
                                           "unfiltered": {"t300": rows_with_band(unfiltered),
                                                          "t306": rows_with_band(unfiltered)}})

    def test_the_bands_peak_counts_against_its_light_without_the_filter_from_still_5_on(self):
        unfiltered = [0.15, 0.25, 0.28, 0.29, 0.3] + [0.3] * 7
        result = self.climb([0.15, 0.2, 0.25, 0.3, 0.33, 0.36, 0.3, 0.3, 0.3, 0.3, 0.3, 0.291], unfiltered)
        self.assertEqual(result["draws"], [33 * (k + 1) for k in range(12)])
        self.assertAlmostEqual(result["plateau"]["t300"][BAND], 0.3)
        self.assertAlmostEqual(result["peakShare"]["t300"][BAND], 1.2)
        self.assertAlmostEqual(result["lastShare"]["t300"][BAND], 0.97)
        self.assertAlmostEqual(result["bandPeakShare"], 1.2)
        self.assertEqual(result["failed"], ["climb"])

    def test_a_band_that_stays_within_10_percent_of_its_light_without_the_filter_passes(self):
        result = self.climb([0.15, 0.25, 0.3, 0.32] + [0.31] * 8, [0.3] * 12)
        self.assertAlmostEqual(result["bandPeakShare"], 0.32 / 0.3)
        self.assertEqual(result["failed"], [])

    def test_the_later_state_decides_when_its_band_climbs_higher(self):
        result = self.climb([0.3] * 12, [0.3] * 12, later=[0.3] * 6 + [0.39] + [0.3] * 5)
        self.assertAlmostEqual(result["bandPeakShare"], 1.3)
        self.assertEqual(result["failed"], ["climb"])

    def test_a_band_without_light_when_the_filter_is_off_measured_nothing(self):
        result = self.climb([0.3] * 12, [0.0] * 12)
        self.assertIsNone(result["bandPeakShare"])
        self.assertEqual(result["failed"], [survey.NOT_MEASURED])


def write_series(work, lights):
    """The probe's series file of a run into work whose every region of capture `name` has the light lights[name]."""
    (work / survey.SERIES_FILE).write_text(
        "".join(f"{name} " + " ".join([repr(light)] * (1 + survey.GRID ** 2)) + "\n" for name, light in lights.items()))


class ProbeRuns(unittest.TestCase):
    """run_stand and run_climb with a fake render that writes the probe's series file of a chosen light (ticket ter-d7qy)."""

    def run_case(self, runner, case, light):
        calls = []

        def render(out, built, *, binary, env=None, extra=(), **kwargs):
            calls.append({"env": env, "extra": extra})
            out.mkdir(parents=True)
            rows = {}
            for step in built["steps"]:
                if step["do"] == "capture":
                    names = [step["name"]]
                elif step["do"] == "film":
                    names = [f"{step['name']}-{k:06d}" for k in range(step["frames"])]
                else:
                    continue
                rows.update({name: light(name, env) for name in names})
            write_series(out, rows)
            return out.parent / f"{out.name}.log"

        with tempfile.TemporaryDirectory() as folder, mock.patch.object(survey, "render", render), \
                mock.patch.object(survey, "probe_binary", lambda out: Path("probe")):
            result = runner(Path(folder), case, argparse.Namespace(exposure="4.08"))
            part = "stand" if runner is survey.run_stand else "climb"
            saved = json.loads((Path(folder) / "results" / part / f"{case['name']}.json").read_text())
            self.assertEqual(list((Path(folder) / "work").rglob("*.png")), [], "the captures are deleted")
        self.assertEqual(saved, json.loads(json.dumps(result)))
        return calls, result

    def test_the_stand_measures_its_film_against_its_end_stills_in_the_bounce_light_alone(self):
        calls, result = self.run_case(survey.run_stand, STAND,
                                      lambda name, env: 1.5 if name.startswith("f-") and int(name[2:]) < 100 else 1.0)
        self.assertEqual(len(calls), 1)
        env = calls[0]["env"]
        self.assertEqual({k: v for k, v in env.items() if k not in ("TERTIUS_PROBE_SERIES", "TERTIUS_PROBE_KEEP")},
                         {"TERTIUS_PROBE_EXPOSURE": "4.08", "TERTIUS_PROBE_SIGNAL": survey.SIGNALS["bounce"],
                          **survey.STILL_CONTINUES})
        self.assertEqual(env["TERTIUS_PROBE_KEEP"], "", "the stand keeps no PNG")
        self.assertEqual(calls[0]["extra"], survey.PROBE_OPTIONS)
        self.assertEqual(len(result["film"]), 600)
        self.assertEqual(result["settleDraws"][BAND], 100)
        self.assertEqual(result["bandSettleDraws"], 0)

    def test_the_climb_takes_its_cases_exposure_and_measures_the_filtered_band_against_the_unfiltered(self):
        def light(name, env):
            peak = name == "t306-s05" and "TERTIUS_DIFFUSE_FILTER" not in env
            return 1.25 if peak else 1.0

        calls, result = self.run_case(survey.run_climb, CLIMB, light)
        self.assertEqual([c["env"]["TERTIUS_PROBE_EXPOSURE"] for c in calls], ["3.15", "3.15"])
        self.assertEqual(len(result["filtered"]["t300"]), 12)
        self.assertAlmostEqual(result["bandPeakShare"], 1.25, places=4)
        self.assertEqual(result["failed"], ["climb"])



class StandAndClimbCases(unittest.TestCase):
    """The cases of cases.json reproduce the takes of ter-g3tk, so that their numbers compare with its notes."""

    def cases(self):
        lists = survey.load_cases()
        return lists["stands"][0], lists["climbs"][0]

    def test_the_stand_is_the_cabin_walks_view_filmed_600_frames_from_tick_60(self):
        stand, _ = self.cases()
        self.assertEqual(survey.stand_script(stand)["steps"],
                         [CAMERA("person"), RUN(60), CAPTURE("still"), FILM("f", 600, 1), *survey.captures("end", 10)])
        self.assertEqual((stand["scene"], stand["signal"], stand["region"], stand["settleTick"]),
                         ("orbit", "bounce", "r11", 306))

    def test_the_climb_is_the_seed_test_of_12_continued_stills_at_ticks_300_and_306(self):
        _, climb = self.cases()
        self.assertEqual(survey.climb_script(climb)["steps"],
                         [CAMERA("person"), RUN(300), *survey.captures("t300", 12), RUN(6), *survey.captures("t306", 12)])
        self.assertEqual((climb["scene"], climb["signal"], climb["region"], climb["exposure"]),
                         ("orbit", "bounce", "r11", "3.15"))

    def test_the_stand_and_climb_parts_run_their_cases(self):
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(survey, "run_stand", return_value={}) as stand, \
                mock.patch.object(survey, "run_climb", return_value={}) as climb, \
                mock.patch.dict(os.environ, {survey.HOLDS_LOCK: "1"}), mock.patch("sys.stdout", new_callable=io.StringIO):
            survey.run(argparse.Namespace(out=folder, no_build=True, parts="stand,climb", only=None, chunk=8,
                                          exposure="4.08", clock_bin=None))
        self.assertEqual([c.args[1]["name"] for c in stand.call_args_list], ["cabin-stand"])
        self.assertEqual([c.args[1]["name"] for c in climb.call_args_list], ["cabin-climb"])

    def test_the_report_gives_the_stands_band_settle_and_each_states_band_peak_share(self):
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder)
            survey.save(out, "stand", "cabin-stand", {"region": "r11", "settleTick": 306, "settleWindow": 355,
                                                      "bandSettleDraws": 55, "settleDrawsAfterTick": [12] + [0] * 9,
                                                      "failed": []})
            survey.save(out, "climb", "cabin-climb", {"region": "r11", "states": [300, 306],
                                                      "peakShare": {"t300": [1.0] * 5 + [1.2] + [1.0] * 4,
                                                                    "t306": [1.0] * 5 + [1.05] + [1.0] * 4},
                                                      "lastShare": {"t300": [1.0] * 5 + [0.97] + [1.0] * 4,
                                                                    "t306": [1.0] * 5 + [0.99] + [1.0] * 4},
                                                      "bandPeakShare": 1.2, "failed": ["climb"]})
            with mock.patch("sys.stdout", new_callable=io.StringIO) as printed:
                survey.report(argparse.Namespace(out=folder))
        lines = printed.getvalue().splitlines()
        self.assertIn("| cabin-stand r11 after tick 306 | stand | 55 | 12 | pass |", lines)
        self.assertIn("| cabin-climb | r11 | t300 | 1.200 | 0.970 | fail: climb |", lines)
        self.assertIn("| cabin-climb | r11 | t306 | 1.050 | 0.990 | fail: climb |", lines)

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


# The evidence census (docs/plans/bounce-light-confidence.md, section 21, take T12). A synthetic census is a grid of
# 10 x 6 blocks of 8 x 8 pixels, 80 x 48 pixels, for each signal of the line's light.
CENSUS_HEADER = {"width": 80, "height": 48, "block": 8, "blocksWide": 10, "blocksHigh": 6, "every": 0, "causes": 7,
                 "movedCuts": False, "motionCuts": False, "acts": True, "recentLightAge": 7, "leastMotion": 0.03125}


def census_line(draw, index, light="diffuse", part=0, levels=(60, 0, 0), blocks=(0, 0, 0), pixels=(0, 0, 0)):
    """One line of census.txt: a draw of one signal's part, its blocks at each level, and its blocks and pixels of each
    cause."""
    return " ".join(str(v) for v in (draw, index, 1 if index == 0 else 0, light, part, *levels, *blocks, *pixels))


def write_census(folder, lines, header=CENSUS_HEADER):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "header.json").write_text(json.dumps(header))
    (folder / "census.txt").write_text("".join(line + "\n" for line in lines))


def census_of(lines, **kwargs):
    with tempfile.TemporaryDirectory() as folder:
        write_census(Path(folder), lines)
        return survey.census(Path(folder), **kwargs)


def signal_of(result, light):
    (signal,) = [s for s in result["signals"] if s["light"] == light]
    return signal


class Census(unittest.TestCase):
    """census: the shares of block-draws at each evidence level and of each cause, for each signal."""

    def test_a_still_without_evidence_has_every_block_draw_at_no_evidence(self):
        result = census_of([census_line(k, k) for k in range(1, 11)])
        signal = signal_of(result, "diffuse")
        self.assertEqual(signal["blockDraws"], 600)
        self.assertEqual(signal["levels"], {"none": 600, "motion": 0, "light": 0})
        self.assertEqual(signal["levelShares"]["light"], 0.0)

    def test_the_reset_draw_counts_in_no_share_because_it_shows_every_instance_as_new(self):
        lines = [census_line(0, 0, levels=(0, 60, 0), blocks=(0, 0, 60), pixels=(0, 0, 3840))]
        lines += [census_line(k, k) for k in range(1, 5)]
        signal = signal_of(census_of(lines), "diffuse")
        self.assertEqual(signal["blockDraws"], 240)
        self.assertEqual(signal["levels"]["motion"], 0)

    def test_young_light_codes_in_a_still_count_as_light_evidence_and_as_their_cause(self):
        lines = [census_line(k, k) for k in range(1, 100)]
        lines.append(census_line(100, 100, levels=(58, 0, 2), blocks=(2, 0, 0), pixels=(5, 0, 0)))
        signal = signal_of(census_of(lines), "diffuse")
        self.assertEqual(signal["levels"]["light"], 2)
        self.assertAlmostEqual(signal["levelShares"]["light"], 2 / 6000)
        self.assertEqual(signal["causes"]["light"], {"blockDraws": 2, "share": 2 / 6000, "pixelDraws": 5,
                                                     "pixelShare": 5 / (100 * 80 * 48)})
        self.assertEqual(signal["evidenceDraws"], [[100, 0, 2]])

    def test_a_cause_that_the_knobs_leave_out_still_counts_as_a_cause_without_a_level(self):
        # TERTIUS_X_CAUSES=light: a moving surface gives no level, but the census still counts its blocks.
        lines = [census_line(1, 1, levels=(60, 0, 0), blocks=(0, 0, 12), pixels=(0, 0, 700))]
        signal = signal_of(census_of(lines), "diffuse")
        self.assertEqual(signal["levels"]["motion"], 0)
        self.assertEqual(signal["causes"]["moving"]["blockDraws"], 12)
        self.assertEqual(signal["evidenceDraws"], [])

    def test_the_parts_of_a_signal_pool_and_the_signals_stay_apart(self):
        lines = [census_line(1, 1, part=0, levels=(59, 1, 0)), census_line(1, 1, part=1, levels=(60, 0, 0)),
                 census_line(1, 1, light="specular", levels=(57, 0, 3))]
        result = census_of(lines)
        diffuse, specular = signal_of(result, "diffuse"), signal_of(result, "specular")
        self.assertEqual((diffuse["parts"], diffuse["blockDraws"], diffuse["levels"]["motion"]), (2, 120, 1))
        self.assertEqual((specular["parts"], specular["blockDraws"], specular["levels"]["light"]), (1, 60, 3))

    def test_the_result_keeps_the_probes_knobs_and_the_draws_that_it_leaves_out(self):
        result = census_of([census_line(k, k) for k in range(0, 5)], skip=2)
        self.assertEqual(result["skip"], 2)
        self.assertEqual(result["probe"]["recentLightAge"], 7)
        self.assertEqual(signal_of(result, "diffuse")["draws"], 3)


class CensusVerdict(unittest.TestCase):
    """Acceptance 12 of the design: in a still, no block-draw has motion evidence, and at most 1e-4 of the block-draws
    of each signal have light evidence."""

    def result(self, motion, light, block_draws=1_000_000):
        return {"signals": [{"light": "diffuse", "blockDraws": block_draws,
                             "levels": {"none": block_draws - motion - light, "motion": motion, "light": light},
                             "levelShares": {"motion": motion / block_draws, "light": light / block_draws}}]}

    def test_a_still_with_light_evidence_at_the_limit_passes(self):
        self.assertEqual(survey.census_verdict(self.result(0, 100)), [])

    def test_a_still_with_more_light_evidence_fails_for_its_signal(self):
        self.assertEqual(survey.census_verdict(self.result(0, 101)), ["diffuse light"])

    def test_a_still_with_one_block_draw_of_motion_evidence_fails(self):
        self.assertEqual(survey.census_verdict(self.result(1, 0)), ["diffuse motion"])


class CensusRuns(unittest.TestCase):
    """The census parts and command, with a fake render that writes a census where the probe would."""

    STILL = {"name": "rect-lamps", "scene": "rect", "camera": "a", "ticks": 10}

    def render(self, calls, lines):
        def render(out, built, *, binary, env=None, extra=(), **kwargs):
            calls.append({"steps": built["steps"], "env": env, "extra": extra, "binary": binary})
            out.mkdir(parents=True)
            write_census(Path(env["TERTIUS_X_CENSUS"]), lines)
            (out / survey.SERIES_FILE).write_text("")
            return out.parent / f"{out.name}.log"
        return render

    def run_part(self, runner, case, lines, environ=None):
        calls = []
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(survey, "render", self.render(calls, lines)), \
                mock.patch.object(survey, "probe_binary", lambda out: Path("probe")), \
                mock.patch.dict(os.environ, environ or {}):
            result = runner(Path(folder), case, argparse.Namespace(skip=1, exposure="4.08"))
            part = runner.__name__.removeprefix("run_").replace("_", "-")
            saved = json.loads((Path(folder) / "results" / part / f"{case['name']}.json").read_text())
            kept = sorted(p.name for p in (Path(folder) / "census" / part / case["name"]).iterdir())
            self.assertEqual([p for p in (Path(folder) / "work").rglob("*") if p.is_file()], [], "the render is deleted")
        self.assertEqual(saved, json.loads(json.dumps(result)))
        self.assertEqual(kept, ["census.txt", "header.json"], "the census stays for the census command")
        return calls, result

    def test_a_census_still_continues_its_history_to_draw_300_with_its_action_on_and_takes_the_verdict(self):
        lines = [census_line(k, k) for k in range(1, 300)]
        calls, result = self.run_part(survey.run_census_still, self.STILL, lines,
                                      {"TERTIUS_X_CAUSES": "light,moved", "TERTIUS_X_CENSUS_EVERY": "33"})
        (call,) = calls
        captures = captures_of({"steps": call["steps"]})
        _, first_is_still = survey.census_still_script(self.STILL)
        self.assertGreaterEqual(survey.still_draws(first_is_still, len(captures) - 1), 300, "it reaches draw 300")
        self.assertLess(survey.still_draws(first_is_still, len(captures) - 2), 300, "and stops there")
        self.assertEqual(call["env"]["TERTIUS_PROBE_STILL_CONTINUES"], "1")
        self.assertNotIn("TERTIUS_X_EVIDENCE_ACTION", call["env"], "the census counts with the action on")
        self.assertEqual(call["extra"], survey.NO_BLOOM)
        self.assertEqual(result["knobs"], {"TERTIUS_X_CAUSES": "light,moved", "TERTIUS_X_CENSUS_EVERY": "33"})
        self.assertEqual(result["failed"], [])

    def test_a_census_film_gives_its_shares_and_no_verdict(self):
        film = {"name": "rigs-crowd", "scene": "rigs", "camera": "a", "ticks": 10, "frames": 20, "view": "a"}
        lines = [census_line(k, k, levels=(50, 10, 0), blocks=(0, 10, 0)) for k in range(1, 20)]
        calls, result = self.run_part(survey.run_census_film, film, lines, {"TERTIUS_X_MOVED_ACTION": "cut"})
        (call,) = calls
        self.assertEqual(call["steps"][-1], {"do": "film", "name": "m0000", "frames": 20, "every": 1})
        self.assertEqual(call["env"]["TERTIUS_PROBE_KEEP"], "", "the film writes no PNG")
        self.assertEqual(signal_of(result, "diffuse")["levels"]["motion"], 190)
        self.assertEqual(result["knobs"], {"TERTIUS_X_MOVED_ACTION": "cut"})
        self.assertEqual(result["failed"], [])

    def test_a_census_switch_films_the_lamps_going_off_and_on_at_the_fixed_exposure(self):
        calls, _ = self.run_part(survey.run_census_switch, SWITCH, [census_line(1, 1)])
        (call,) = calls
        self.assertEqual(call["env"]["TERTIUS_PROBE_LAMP_OFF"], survey.switch_runs(SWITCH)["switch"]["TERTIUS_PROBE_LAMP_OFF"])
        self.assertEqual(call["env"]["TERTIUS_PROBE_EXPOSURE"], "4.08")
        self.assertEqual(call["extra"], survey.PROBE_OPTIONS)

    def test_a_census_walk_and_a_census_lag_film_their_cases(self):
        walk_calls, _ = self.run_part(survey.run_census_walk, {**WALK, "name": "cabin-walk"}, [census_line(1, 1)])
        self.assertEqual(walk_calls[0]["steps"], survey.walk_script(WALK)["steps"])
        lag = survey.load_cases()["lags"][0]
        lag_calls, _ = self.run_part(survey.run_census_lag, lag, [census_line(1, 1)])
        self.assertEqual(lag_calls[0]["steps"], survey.lag_scripts(lag)[0]["steps"])

    def test_the_census_parts_take_their_lists_and_run_only_when_named(self):
        cases = {"stills": [{"name": "rect-lamps"}], "motion": [{"name": "rigs-crowd"}], "switches": [{"name": "cabin"}],
                 "lags": [{"name": "bay-sun"}], "walks": [{"name": "cabin-walk"}]}
        self.assertEqual({part: [c["name"] for c in survey.cases_of(cases, part)] for part in survey.NAMED_PARTS},
                         {"census-still": ["rect-lamps"], "census-film": ["rigs-crowd"], "census-switch": ["cabin"],
                          "census-lag": ["bay-sun"], "census-walk": ["cabin-walk"]})
        with mock.patch.object(survey, "run", return_value=0) as run:
            survey.main(["run", "/tmp/x"])
        self.assertFalse([p for p in run.call_args.args[0].parts.split(",") if p.startswith("census")])

    def test_the_command_reduces_a_census_folder_and_writes_its_numbers(self):
        with tempfile.TemporaryDirectory() as folder:
            write_census(Path(folder) / "c", [census_line(k, k, levels=(59, 0, 1)) for k in range(0, 4)])
            target = Path(folder) / "census.json"
            self.assertEqual(survey.main(["census", str(Path(folder) / "c"), "--skip", "1", "--write", str(target)]), 0)
            written = json.loads(target.read_text())
        self.assertEqual(signal_of(written, "diffuse")["levels"]["light"], 3)
        self.assertEqual(written["skip"], 1)

    def test_the_report_gives_each_signal_s_shares_by_level_and_cause(self):
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder)
            survey.save(out, "census-still", "rect-lamps", {
                "failed": ["diffuse light"],
                "signals": [{"light": "diffuse", "blockDraws": 1000000,
                             "levelShares": {"none": 0.9997, "motion": 0.0, "light": 3e-4},
                             "causes": {"light": {"share": 3e-4, "pixelShare": 2e-5},
                                        "moved": {"share": 0.0, "pixelShare": 0.0},
                                        "moving": {"share": 0.0, "pixelShare": 0.0}}}]})
            with mock.patch("sys.stdout", new_callable=io.StringIO) as printed:
                survey.report(argparse.Namespace(out=folder))
        self.assertIn("| rect-lamps | still | diffuse | 1000000 | 3.0e-04 | 0.0e+00 | 3.0e-04 | 0.0e+00 | 0.0e+00 "
                      "| 2.0e-05 | fail: diffuse light |", printed.getvalue().splitlines())


if __name__ == "__main__":
    unittest.main()
