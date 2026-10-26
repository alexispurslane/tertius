"""Tests of the build tool, tools/blender/build.sh, on the example model crate.model.

Run them in the assets shell:
    nix develop .#assets --command python3 tools/blender/tests/test_build.py
tools/test-all.sh runs them as the module Blender. They start Blender 25 times and take about 50 seconds.
"""

import json
import os
import pathlib
import shutil
import struct
import subprocess
import signal
import sys
import tempfile
import time
import unittest

HERE = pathlib.Path(__file__).resolve().parent
BUILD = HERE.parent / "build.sh"
CRATE = HERE / "crate.model"


def build(model, out, *extra):
    """Runs the build tool on the CPU and returns the finished process."""
    return subprocess.run(
        [str(BUILD), str(model), "--out", str(out), "--device", "cpu", *extra],
        capture_output=True,
        text=True,
        check=False,
    )


def glb_json(path):
    """The JSON chunk of a .glb file."""
    data = pathlib.Path(path).read_bytes()
    magic, _version, _length = struct.unpack_from("<4sII", data, 0)
    assert magic == b"glTF", magic
    chunk_length, chunk_type = struct.unpack_from("<I4s", data, 12)
    assert chunk_type == b"JSON", chunk_type
    return json.loads(data[20 : 20 + chunk_length])


class TheExampleModel(unittest.TestCase):
    """Two builds of crate.model: the first with views, the second without."""

    @classmethod
    def setUpClass(cls):
        cls.scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        cls.first = build(CRATE, cls.scratch / "first", "--views")
        cls.second = build(CRATE, cls.scratch / "second")
        cls.gltf = glb_json(cls.scratch / "first" / "model.glb")
        cls.report = json.loads((cls.scratch / "first" / "build.json").read_text())

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch, ignore_errors=True)

    def test_both_builds_finish_with_exit_code_0(self):
        self.assertEqual(self.first.returncode, 0, self.first.stdout + self.first.stderr)
        self.assertEqual(self.second.returncode, 0, self.second.stdout + self.second.stderr)

    def test_the_build_tool_prints_its_report(self):
        self.assertEqual(json.loads(self.first.stdout), self.report)
        self.assertEqual(self.report["status"], "ok")

    def test_two_builds_of_the_same_scripts_write_the_same_glb_bytes(self):
        first = (self.scratch / "first" / "model.glb").read_bytes()
        second = (self.scratch / "second" / "model.glb").read_bytes()
        self.assertEqual(first, second)

    def test_the_glb_holds_the_nodes_that_the_scripts_made(self):
        self.assertEqual(sorted(node["name"] for node in self.gltf["nodes"]), ["Crate", "Lid"])

    def test_the_lid_carries_the_piece_mark(self):
        lid = next(node for node in self.gltf["nodes"] if node["name"] == "Lid")
        self.assertEqual(lid.get("extras", {}).get("tertius_piece"), 1)

    def test_the_crate_is_a_root_node_and_the_lid_its_child(self):
        crate = next(i for i, node in enumerate(self.gltf["nodes"]) if node["name"] == "Crate")
        lid = next(i for i, node in enumerate(self.gltf["nodes"]) if node["name"] == "Lid")
        self.assertEqual(self.gltf["scenes"][0]["nodes"], [crate])
        self.assertEqual(self.gltf["nodes"][crate]["children"], [lid])

    def test_the_glb_holds_no_camera_and_no_light_of_the_stage(self):
        self.assertNotIn("cameras", self.gltf)
        self.assertNotIn("KHR_lights_punctual", self.gltf.get("extensions", {}))
        self.assertTrue(all("camera" not in node and "extensions" not in node for node in self.gltf["nodes"]))

    def test_the_crate_material_is_baked_into_three_embedded_maps(self):
        material = next(m for m in self.gltf["materials"] if m["name"] == "M_Crate_Baked")
        pbr = material["pbrMetallicRoughness"]
        self.assertIn("baseColorTexture", pbr)
        self.assertIn("metallicRoughnessTexture", pbr)
        self.assertIn("normalTexture", material)
        self.assertTrue(all("bufferView" in image for image in self.gltf["images"]))

    def test_the_bake_writes_each_map_at_the_size_of_model_json(self):
        maps = sorted(p.name for p in (self.scratch / "first" / "textures").iterdir())
        self.assertEqual(maps, ["Crate_basecolor.png", "Crate_normal.png", "Crate_roughness.png"])
        width, height = struct.unpack(">II", (self.scratch / "first" / "textures" / maps[0]).read_bytes()[16:24])
        self.assertEqual((width, height), (64, 64))

    def test_the_views_are_rendered_only_when_asked(self):
        self.assertTrue((self.scratch / "first" / "views" / "three-quarter.png").is_file())
        self.assertFalse((self.scratch / "second" / "views").exists())

    def test_the_report_names_the_roots_the_marked_objects_and_the_result_of_each_script(self):
        self.assertEqual(self.report["roots"], ["Crate"])
        self.assertEqual(self.report["marked"], ["Lid"])
        names = [s["name"] for s in self.report["scripts"]]
        self.assertEqual(names, ["s00_body.py", "s10_lid.py", "s20_material.py", "s30_check.py"])
        self.assertEqual(self.report["scripts"][1]["result"], {"lid": "Lid"})

    def test_the_attachment_check_finds_the_lid_on_the_body(self):
        self.assertEqual(self.report["scripts"][3]["result"], {"islands": 1, "free": 0, "freeIslands": []})

    def test_the_build_leaves_no_temporary_folder_of_blender(self):
        self.assertFalse(any(p.name.startswith("tertius-build.") for p in pathlib.Path(tempfile.gettempdir()).iterdir()))


class BadInput(unittest.TestCase):
    """Builds that stop before or inside Blender."""

    def setUp(self):
        self.scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        self.model = self.scratch / "crate.model"
        shutil.copytree(CRATE, self.model)

    def tearDown(self):
        shutil.rmtree(self.scratch, ignore_errors=True)

    def edit_model_json(self, change):
        path = self.model / "model.json"
        data = json.loads(path.read_text())
        change(data)
        path.write_text(json.dumps(data))

    def test_a_missing_script_is_bad_input_with_exit_code_2(self):
        self.edit_model_json(lambda d: d["scripts"].append("s30_missing.py"))
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 2, run.stdout + run.stderr)
        report = json.loads(run.stdout)
        self.assertEqual(report["status"], "badInput")
        self.assertIn("s30_missing.py", report["error"]["message"])

    def test_a_file_that_model_json_does_not_name_is_bad_input(self):
        (self.model / "s99_stray.py").write_text("result = {}\n")
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 2, run.stdout + run.stderr)
        self.assertIn("s99_stray.py", json.loads(run.stdout)["error"]["message"])

    def test_a_script_that_raises_fails_the_build_with_exit_code_3(self):
        (self.model / "s10_lid.py").write_text("raise ValueError('the lid does not fit')\n")
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
        error = json.loads(run.stdout)["error"]
        self.assertEqual((error["step"], error["script"]), ("script", "s10_lid.py"))
        self.assertIn("the lid does not fit", error["message"])

    def test_a_lid_that_floats_free_fails_the_attachment_check(self):
        path = self.model / "s10_lid.py"
        path.write_text(path.read_text().replace("lid.location = (0.0, depth / 2, height)",
                                                 "lid.location = (0.0, depth / 2, height + 0.5)"))
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
        error = json.loads(run.stdout)["error"]
        self.assertEqual(error["script"], "s30_check.py")
        self.assertIn("'object': 'Lid'", error["message"])

    def test_a_camera_in_the_model_fails_the_export(self):
        with open(self.model / "s10_lid.py", "a", encoding="utf-8") as fh:
            fh.write("bpy.context.scene.collection.objects.link(bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')))\n")
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
        error = json.loads(run.stdout)["error"]
        self.assertEqual(error["step"], "export")
        self.assertIn("Cam", error["message"])
        self.assertFalse((self.scratch / "out" / "model.glb").exists())

    def test_an_output_folder_with_other_files_is_refused_and_kept(self):
        out = self.scratch / "out"
        out.mkdir()
        (out / "notes.txt").write_text("keep me\n")
        run = build(self.model, out)
        self.assertEqual(run.returncode, 2, run.stdout + run.stderr)
        self.assertEqual((out / "notes.txt").read_text(), "keep me\n")

    def test_two_bake_objects_with_one_material_fail_the_bake(self):
        (self.model / "s25_twin.py").write_text(
            "import bpy\n"
            "crate = bpy.data.objects['Crate']\n"
            "twin = crate.copy()\n"
            "twin.data = crate.data.copy()\n"
            "twin.name = 'Crate2'\n"
            "bpy.context.scene.collection.objects.link(twin)\n")
        self.edit_model_json(lambda d: (d["scripts"].insert(3, "s25_twin.py"), d["bake"]["objects"].append("Crate2")))
        run = build(self.model, self.scratch / "out")
        self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
        error = json.loads(run.stdout)["error"]
        self.assertEqual(error["step"], "bake")
        self.assertIn("M_Crate", error["message"])
        self.assertIn("Crate2", error["message"])

    def test_a_signal_stops_blender_deletes_its_temporary_folder_and_gives_exit_code_130(self):
        (self.model / "s00_body.py").write_text("import time\ntime.sleep(60)\n")
        tmp = self.scratch / "tmp"
        tmp.mkdir()
        out = self.scratch / "out"
        process = subprocess.Popen([str(BUILD), str(self.model), "--out", str(out), "--device", "cpu"],
                                   env={**os.environ, "TMPDIR": str(tmp)}, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL)
        log = out / "blender.log"
        deadline = time.monotonic() + 60
        while not (log.exists() and "Blender" in log.read_text(errors="replace")) and time.monotonic() < deadline:
            time.sleep(0.2)
        process.send_signal(signal.SIGTERM)
        self.assertEqual(process.wait(timeout=60), 130)
        self.assertEqual(list(tmp.iterdir()), [])
        blenders = subprocess.run(["pgrep", "-f", "build.py -- " + str(self.model)], capture_output=True, text=True)
        self.assertEqual(blenders.stdout, "")

    def test_an_earlier_build_in_the_output_folder_is_replaced(self):
        out = self.scratch / "out"
        out.mkdir()
        (out / "build.json").write_text("{}")
        (out / "stale.png").write_text("old")
        run = build(self.model, out)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        self.assertFalse((out / "stale.png").exists())


class StrayRandomness(unittest.TestCase):
    """A script that calls the random module directly still builds the same way twice."""

    def test_the_random_module_is_seeded_from_model_json_before_each_script(self):
        scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        try:
            model = scratch / "dice.model"
            model.mkdir()
            (model / "model.json").write_text('{"format": 1, "scripts": ["s00_roll.py"], "seed": 3}')
            (model / "s00_roll.py").write_text("import random\nresult = {'roll': random.random()}\n")
            rolls = []
            for name in ("first", "second"):
                run = build(model, scratch / name)
                self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
                rolls.append(json.loads(run.stdout)["scripts"][0]["result"]["roll"])
            self.assertEqual(rolls[0], rolls[1])
        finally:
            shutil.rmtree(scratch, ignore_errors=True)


class AUserFolderOfTheBuild(unittest.TestCase):
    """An add-on that a script enables, such as MPFB, writes into Blender's user folder. The build gives Blender a
    user folder of its own, inside the build's temporary folder, so nothing reaches the user's own Blender folder."""

    def test_blender_writes_its_user_files_into_a_folder_of_the_build_that_the_build_deletes(self):
        scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        try:
            model = scratch / "user.model"
            model.mkdir()
            (model / "model.json").write_text('{"format": 1, "scripts": ["s00_user.py"], "seed": 3}')
            (model / "s00_user.py").write_text(
                "import os, tempfile, bpy\n"
                "user = bpy.utils.resource_path('USER')\n"
                "result = {'user': user, 'tmp': tempfile.gettempdir()}\n")
            run = build(model, scratch / "out")
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            found = json.loads(run.stdout)["scripts"][0]["result"]
            user = pathlib.Path(found["user"]).resolve()
            build_tmp = pathlib.Path(found["tmp"]).resolve()
            self.assertTrue(build_tmp.name.startswith("tertius-build."), build_tmp)
            self.assertEqual(user.parent, build_tmp)
            self.assertFalse(user.exists())
        finally:
            shutil.rmtree(scratch, ignore_errors=True)


class WeldedMeshes(unittest.TestCase):
    """MB.finish welds coincident vertices. The weld rebuilds faces in an order that follows memory addresses."""

    def test_a_mesh_with_welded_vertices_builds_the_same_glb_bytes_twice(self):
        scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        try:
            model = scratch / "beads.model"
            model.mkdir()
            (model / "model.json").write_text('{"format": 1, "scripts": ["s00_beads.py"], "seed": 3}')
            # The BMesh operators of spheres and lathes, and the weld of MB.finish, order what they make by memory
            # address. Neighboring spheres sit 4 micrometers apart, so the weld merges two vertices that are not
            # equal, and the one that stays depends on the order. Caps of posts and conduit stubs are polygons
            # whose first corner decides the triangles of the export. Two boxes in one place weld into one.
            (model / "s00_beads.py").write_text(
                "from mathutils import Matrix\n"
                "from tertius_blender.geometry import MB, rod\n"
                "mb = MB()\n"
                "for k in range(24):\n"
                "    mb.sphere((k * 0.900004, 0.0, 0.0), 0.45, segs=12, rings=6)\n"
                "    mb.box((k * 1.0, 2.0, 0.0), (0.6, 0.6, 0.6), bevel=0.05)\n"
                "    mb.cyl((k * 1.0, 2.0, 0.5), (0, 0, 1), 0.2, 0.6, segs=10 + 2 * (k % 8))\n"
                "    rod(mb, (k * 1.0, 2.0, 0.8), (k * 1.0 + 0.5, 2.5, 1.5), 0.05)\n"
                "    mb.box((k * 1.0, 4.0, 0.0), (0.5, 0.5, 0.4), bevel=0.04)\n"
                "    mb.cyl((k * 1.0, 4.0 + 0.25 + 0.4, 0.16), (0, 1, 0), 0.06, 0.9, segs=10)\n"
                "    mb.lathe([(0.3, 0.0), (0.5, 0.4), (0.2, 0.6)], Matrix.Translation((k * 1.0, 6.0, 0.0)), segs=16)\n"
                "    mb.box((k * 1.0, 8.0, 0.0), (0.4, 0.4, 0.4))\n"
                "    mb.box((k * 1.0, 8.0, 0.0), (0.4, 0.4, 0.4))\n"
                "rod(mb, (0.0, 0.0, 0.0), (20.7, 0.0, 0.0), 0.1)\n"
                "mb.finish('Beads', None, 'Beads')\n"
            )
            glbs = []
            for name in ("first", "second"):
                run = build(model, scratch / name)
                self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
                glbs.append((scratch / name / "model.glb").read_bytes())
            self.assertEqual(glbs[0], glbs[1])
        finally:
            shutil.rmtree(scratch, ignore_errors=True)


class TheLibraryInTheBridge(unittest.TestCase):
    """The functions that an agent calls through execute_blender_code, run in one plain headless Blender."""

    @classmethod
    def setUpClass(cls):
        cls.scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-build-test-"))
        # A second checkout: a folder with a model library, and a model folder inside it.
        other = cls.scratch / "other-checkout"
        (other / "tools" / "blender" / "tertius_blender").mkdir(parents=True)
        (other / "tools" / "blender" / "tertius_blender" / "run.py").write_text("")
        shutil.copytree(CRATE, other / "crate.model")
        code = (
            "import json, os, sys\n"
            "sys.path.insert(0, {lib!r})\n"
            "import bpy\n"
            "from tertius_blender import checks, export, run, stage\n"
            "def settings():\n"
            "    s = bpy.context.scene\n"
            "    return [s.render.engine, s.render.resolution_x, s.cycles.samples, s.cycles.max_bounces,\n"
            "            s.cycles.use_adaptive_sampling, s.cycles.device, s.view_settings.look, s.camera, s.world]\n"
            "run.new_scene()\n"
            "spec = run.load_model({model!r})\n"
            "for script in spec['scripts']:\n"
            "    run.run_script({model!r}, script)\n"
            "bpy.context.scene.cycles.device = 'GPU'\n"
            "before = repr(settings())\n"
            "stage.build(spec['views'], device='cpu')\n"
            "stage.use_shot(spec['views']['shots'][0])\n"
            "during = repr(settings())\n"
            "device = bpy.context.scene.cycles.device\n"
            "shares = checks.view_shares('VIEW_three-quarter', 48, 27)['share']\n"
            "export.write_glb(os.path.join({scratch!r}, 'with-camera.glb'))\n"
            "stage.remove()\n"
            "after = repr(settings())\n"
            "left = sorted(o.name for o in bpy.data.objects)\n"
            "left += [c.name for c in bpy.data.collections if c.name == 'Stage']\n"
            "try:\n"
            "    run.run_script({other!r}, 's00_body.py')\n"
            "    other = 'ran'\n"
            "except RuntimeError as ex:\n"
            "    other = str(ex)\n"
            "print('TERTIUS-TEST', json.dumps({{'shares': shares, 'left': left, 'worlds': len(bpy.data.worlds),\n"
            "      'before': before, 'during': during, 'after': after, 'device': device, 'other': other}}))\n"
        ).format(lib=str(HERE.parent), model=str(CRATE), scratch=str(cls.scratch),
                 other=str(other / "crate.model"))
        cls.done = subprocess.run(
            [os.environ["TERTIUS_BLENDER"], "--background", "--factory-startup", "--python-exit-code", "3",
             "--python-expr", code],
            capture_output=True, text=True, check=False)
        line = next((l for l in cls.done.stdout.splitlines() if l.startswith("TERTIUS-TEST ")), None)
        cls.report = json.loads(line[len("TERTIUS-TEST "):]) if line else None

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch, ignore_errors=True)

    def setUp(self):
        self.assertEqual(self.done.returncode, 0, self.done.stdout[-2000:] + self.done.stderr[-2000:])

    def test_the_view_check_sees_the_crate_through_the_camera_of_the_shot(self):
        self.assertGreater(self.report["shares"].get("Crate", 0), 0.1)
        self.assertGreater(self.report["shares"].get("(space)", 0), 0.3)
        self.assertAlmostEqual(sum(self.report["shares"].values()), 1.0, places=2)

    def test_the_stage_goes_away_with_its_objects_and_its_world(self):
        self.assertEqual(self.report["left"], ["Crate", "Lid"])
        self.assertEqual(self.report["worlds"], 0)

    def test_the_stage_changes_the_render_settings_and_restores_them_when_it_goes(self):
        self.assertNotEqual(self.report["during"], self.report["before"])
        self.assertEqual(self.report["after"], self.report["before"])

    def test_the_stage_renders_on_the_device_that_it_is_given(self):
        # The scripts left the GPU set, and the stage of a build with --device cpu renders on the CPU.
        self.assertEqual(self.report["device"], "CPU")

    def test_the_export_of_a_converter_leaves_a_camera_out_instead_of_failing(self):
        gltf = glb_json(self.scratch / "with-camera.glb")
        self.assertNotIn("cameras", gltf)

    def test_a_model_folder_of_another_checkout_does_not_run_with_this_library(self):
        self.assertIn("other-checkout", self.report["other"])
        self.assertIn(str(HERE.parent.parent.parent), self.report["other"])


class TheSpaceThatABuildNeeds(unittest.TestCase):
    """needed_mb.py, which build.sh asks before it starts Blender."""

    @classmethod
    def setUpClass(cls):
        sys.path.insert(0, str(HERE.parent))
        sys.dont_write_bytecode = True  # No __pycache__ next to the build tool.
        import needed_mb  # noqa: PLC0415 - the module lives next to the build tool, not on the path.

        cls.needed_mb = staticmethod(needed_mb.needed_mb)

    def test_a_model_with_no_bake_needs_one_gigabyte(self):
        self.assertEqual(self.needed_mb({"bake": {"objects": []}}), 1024)

    def test_the_need_grows_with_the_number_of_bake_objects_and_the_pixels_of_a_map(self):
        one = self.needed_mb({"bake": {"objects": ["A"], "size": 4096}})
        four = self.needed_mb({"bake": {"objects": ["A", "B", "C", "D"], "size": 4096}})
        self.assertEqual(one, 1024 + 16 * (32 + 24))
        self.assertEqual(four, 1024 + 16 * (32 + 4 * 24))

    def test_the_default_map_size_is_the_one_of_model_json(self):
        self.assertEqual(self.needed_mb({"bake": {"objects": ["A"]}}), 1024 + 56)


if __name__ == "__main__":
    os.environ.setdefault("PYTHONDONTWRITEBYTECODE", "1")
    unittest.main(verbosity=2)
