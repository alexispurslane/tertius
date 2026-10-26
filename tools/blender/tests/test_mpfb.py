"""Tests of MPFB in the build's Blender: tertius_blender.mpfb enables the add-on headless, and MPFB's own slider code
makes the bodies that an independent copy of its formula (mpfb_reference.py) predicts.

Run them in the assets shell:
    nix develop .#assets --command python3 tools/blender/tests/test_mpfb.py
tools/test-all.sh runs them with the module Blender. They start Blender three times and take about 20 seconds.
"""

import json
import os
import pathlib
import shutil
import subprocess
import tempfile
import unittest

HERE = pathlib.Path(__file__).resolve().parent
LIBRARY = HERE.parent

# Slider sets of MPFB's macro details. Each one starts from MPFB's defaults and changes the values that it names.
DEFAULTS = {"gender": 0.5, "age": 0.5, "muscle": 0.5, "weight": 0.5, "proportions": 0.5, "height": 0.5,
            "cupsize": 0.5, "firmness": 0.5, "race": {"african": 0.33, "asian": 0.33, "caucasian": 0.33}}
SLIDER_SETS = {
    "defaults": {},
    "astronaut": {"gender": 0.8, "age": 0.55, "muscle": 0.6, "weight": 0.55, "proportions": 0.75, "height": 0.6,
                  "race": {"african": 0.2, "asian": 0.2, "caucasian": 0.6}},
    "young muscular man": {"gender": 1.0, "age": 0.3, "muscle": 0.9, "weight": 0.4, "height": 0.8},
    "old heavy woman": {"gender": 0.0, "age": 0.95, "muscle": 0.2, "weight": 0.9, "cupsize": 0.8, "firmness": 0.2,
                        "race": {"african": 1.0, "asian": 0.0, "caucasian": 0.0}},
    "child": {"gender": 0.3, "age": 0.1, "proportions": 0.2, "height": 0.3,
              "race": {"african": 0.0, "asian": 1.0, "caucasian": 0.0}},
    "every slider at 0": {k: 0.0 for k in DEFAULTS if k != "race"},
    "every slider at 1": {k: 1.0 for k in DEFAULTS if k != "race"},
}

# Blender keeps shape keys in 32-bit floats. The bodies of the first run differed by at most 0.54 micrometers.
TOLERANCE_M = 2e-6


def blender(code, env):
    """Runs `code` in a headless Blender of the assets shell and returns the JSON of its TERTIUS-TEST line, or None,
    and the finished process."""
    done = subprocess.run(
        [os.environ["TERTIUS_BLENDER"], "--background", "--factory-startup", "--python-exit-code", "3",
         "--python-expr", code],
        capture_output=True, text=True, check=False, env=env)
    line = next((l for l in done.stdout.splitlines() if l.startswith("TERTIUS-TEST ")), None)
    return (json.loads(line[len("TERTIUS-TEST "):]) if line else None), done


class MpfbSliders(unittest.TestCase):
    """MPFB's HumanService.create_human against mpfb_reference.Macros, for every slider set."""

    @classmethod
    def setUpClass(cls):
        cls.scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-mpfb-test-"))
        sets = {name: {**DEFAULTS, **over} for name, over in SLIDER_SETS.items()}
        code = (
            "import json, sys\n"
            "sys.path.insert(0, {lib!r}); sys.path.insert(0, {tests!r})\n"
            "import bpy, numpy as np\n"
            "from tertius_blender import mpfb, run\n"
            "import mpfb_reference\n"
            "S = mpfb.services()\n"
            "reference = mpfb_reference.Macros(S.LocationService.get_mpfb_data())\n"
            "out = {{'user': bpy.utils.resource_path('USER'), 'sets': {{}}}}\n"
            "for name, sliders in json.loads({sets!r}).items():\n"
            "    run.new_scene()\n"
            "    body = S.HumanService.create_human(mask_helpers=False, feet_on_ground=False,\n"
            "                                       macro_detail_dict=sliders)\n"
            "    mesh = body.evaluated_get(bpy.context.evaluated_depsgraph_get()).data\n"
            "    got = np.array([body.matrix_world @ v.co for v in mesh.vertices])\n"
            "    want = reference.body(sliders)\n"
            "    theirs = S.TargetService.calculate_target_stack_from_macro_info_dict(sliders)\n"
            "    ours = reference.stack(sliders)\n"
            "    out['sets'][name] = {{\n"
            "        'vertices': [len(got), len(want)],\n"
            "        'largest': float(np.linalg.norm(got - want, axis=1).max()),\n"
            "        'theirs': sorted([n, round(w, 9)] for n, w in theirs),\n"
            "        'ours': sorted([n, round(w, 9)] for n, w in ours)}}\n"
            "print('TERTIUS-TEST', json.dumps(out))\n"
        ).format(lib=str(LIBRARY), tests=str(HERE), sets=json.dumps(sets))
        env = {**os.environ, "BLENDER_USER_RESOURCES": str(cls.scratch / "user")}
        cls.report, cls.done = blender(code, env)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch, ignore_errors=True)

    def setUp(self):
        self.assertIsNotNone(self.report, self.done.stdout[-3000:] + self.done.stderr[-3000:])

    def test_mpfb_and_the_reference_choose_the_same_targets_with_the_same_weights(self):
        for name, found in self.report["sets"].items():
            with self.subTest(name):
                self.assertEqual(found["theirs"], found["ours"])

    def test_every_vertex_of_an_mpfb_body_lies_where_the_reference_puts_it(self):
        for name, found in self.report["sets"].items():
            with self.subTest(name):
                self.assertEqual(found["vertices"][0], found["vertices"][1])
                self.assertLess(found["largest"], TOLERANCE_M)

    def test_the_extremes_load_more_targets_than_the_defaults(self):
        sets = self.report["sets"]
        self.assertGreater(len(sets["every slider at 1"]["theirs"]), len(sets["defaults"]["theirs"]))
        self.assertGreater(len(sets["every slider at 0"]["theirs"]), len(sets["defaults"]["theirs"]))

    def test_mpfb_writes_its_files_into_the_user_folder_that_blender_was_given(self):
        self.assertEqual(pathlib.Path(self.report["user"]).resolve(), (self.scratch / "user").resolve())
        self.assertTrue((self.scratch / "user" / "mpfb" / "logs").is_dir())


class AFrozenHuman(unittest.TestCase):
    """tertius_blender.mpfb.human, pose, joints and freeze: a human with a rig, eyes and hair from the MakeHuman
    assets, posed, and turned into static meshes for the export."""

    @classmethod
    def setUpClass(cls):
        cls.scratch = pathlib.Path(tempfile.mkdtemp(prefix="tertius-mpfb-test-"))
        code = (
            "import json, sys\n"
            "sys.path.insert(0, {lib!r})\n"
            "import bpy\n"
            "from tertius_blender import mpfb, run\n"
            "run.new_scene()\n"
            "bpy.data.materials.new('M_Later')\n"
            "body = mpfb.human({{'phenotype': {{'gender': 1.0}}, 'rig': 'game_engine',\n"
            "                   'eyes': 'high-poly/high-poly.mhclo', 'hair': 'short02/short02.mhclo',\n"
            "                   'skin_mhmat': 'young_caucasian_male/young_caucasian_male.mhmat',\n"
            "                   'skin_material_type': 'MAKESKIN'}}, 'Tester')\n"
            "rest = mpfb.joints(body)\n"
            "mpfb.pose(body.parent, {{'upperarm_l': [0, 0, -30]}})\n"
            "posed = mpfb.joints(body)\n"
            "parts = mpfb.freeze(body)\n"
            "arm = mpfb.bone_weights(parts['Body'], [posed['l-hand'], posed['pelvis']],\n"
            "                        ('upperarm_l', 'lowerarm_l', 'hand_l'))\n"
            "def links(name):\n"
            "    m = bpy.data.materials['M_' + name]\n"
            "    p = next(n for n in m.node_tree.nodes if n.bl_idname == 'ShaderNodeBsdfPrincipled')\n"
            "    return {{'alpha': p.inputs['Alpha'].is_linked, 'color': p.inputs['Base Color'].links[0].from_node.bl_idname,\n"
            "            'method': m.surface_render_method,\n"
            "            'nodes': sorted(n.bl_idname for n in m.node_tree.nodes)}}\n"
            "print('TERTIUS-TEST', json.dumps({{\n"
            "    'parts': sorted(parts), 'objects': sorted((o.name, o.type) for o in bpy.data.objects),\n"
            "    'armatures': len(bpy.data.armatures),\n"
            "    'modifiers': sum(len(o.modifiers) for o in bpy.data.objects),\n"
            "    'shape_keys': sum(o.data.shape_keys is not None for o in bpy.data.objects),\n"
            "    'body_faces': len(parts['Body'].data.polygons), 'eye_faces': len(parts['Eyes'].data.polygons),\n"
            "    'groups': sorted(g.name for g in parts['Body'].vertex_groups),\n"
            "    'uv': parts['Body'].data.uv_layers.active is not None,\n"
            "    'rest_hand': rest['l-hand'], 'posed_hand': posed['l-hand'],\n"
            "    'lowest': min((o.matrix_world @ v.co).z for v in parts['Body'].data.vertices for o in [parts['Body']]),\n"
            "    'hair': links('Hair'), 'body': links('Body'), 'eyes': links('Eyes'),\n"
            "    'arm': [round(w, 3) for w in arm],\n"
            "    'later': [m.use_fake_user for m in bpy.data.materials if m.name == 'M_Later']}}))\n"
        ).format(lib=str(LIBRARY))
        env = {**os.environ, "BLENDER_USER_RESOURCES": str(cls.scratch / "user")}
        cls.report, cls.done = blender(code, env)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch, ignore_errors=True)

    def setUp(self):
        self.assertIsNotNone(self.report, self.done.stdout[-3000:] + self.done.stderr[-3000:])

    def test_a_material_of_an_earlier_script_survives_mpfb_without_a_lasting_fake_user(self):
        self.assertEqual(self.report["later"], [False])

    def test_freeze_names_each_part_and_removes_the_rig(self):
        self.assertEqual(self.report["parts"], ["Body", "Eyes", "Hair"])
        self.assertEqual(self.report["objects"], [["Body", "MESH"], ["Eyes", "MESH"], ["Hair", "MESH"]])
        self.assertEqual(self.report["armatures"], 0)

    def test_the_frozen_parts_hold_no_modifier_and_no_shape_key(self):
        self.assertEqual(self.report["modifiers"], 0)
        self.assertEqual(self.report["shape_keys"], 0)

    def test_the_body_loses_the_helper_geometry_and_keeps_its_uv_map(self):
        # MPFB's base mesh has 18,486 faces, 13,378 of them in the group "body". The rest are helpers.
        self.assertEqual(self.report["body_faces"], 13378)
        self.assertTrue(self.report["uv"])

    def test_the_body_keeps_only_the_bone_weights_as_vertex_groups(self):
        groups = self.report["groups"]
        self.assertIn("upperarm_l", groups)
        self.assertFalse([g for g in groups if g.startswith(("joint-", "helper-")) or g == "body"])

    def test_the_pose_moves_the_joints_and_stays_in_the_frozen_body(self):
        self.assertLess(self.report["posed_hand"][2], self.report["rest_hand"][2] - 0.1)

    def test_bone_weights_reads_the_weights_of_the_nearest_body_vertex(self):
        # The hand joint lies inside the wrist, where the skin follows the arm bones only. The pelvis joint lies
        # inside the hips, where no arm bone reaches.
        hand, pelvis = self.report["arm"]
        self.assertAlmostEqual(hand, 1.0, delta=0.01)
        self.assertEqual(pelvis, 0.0)

    def test_the_feet_stand_on_the_ground(self):
        self.assertAlmostEqual(self.report["lowest"], 0.0, delta=0.002)

    def test_the_eyes_lose_the_clear_cornea_and_are_drawn_opaque(self):
        # The high-poly eyes of MakeHuman have 1,020 faces with the clear shells.
        self.assertLess(self.report["eye_faces"], 1020)
        self.assertGreater(self.report["eye_faces"], 300)
        self.assertFalse(self.report["eyes"]["alpha"])

    def test_hair_keeps_the_alpha_of_its_texture_and_is_drawn_blended(self):
        self.assertTrue(self.report["hair"]["alpha"])
        self.assertEqual(self.report["hair"]["method"], "BLENDED")

    def test_the_skin_is_opaque_and_its_image_feeds_the_base_color_directly(self):
        self.assertFalse(self.report["body"]["alpha"])
        self.assertEqual(self.report["body"]["color"], "ShaderNodeTexImage")
        self.assertEqual(self.report["body"]["nodes"],
                         ["ShaderNodeBsdfPrincipled", "ShaderNodeOutputMaterial", "ShaderNodeTexImage"])


class AUserFolderOfItsOwn(unittest.TestCase):
    """tertius_blender.mpfb refuses to load MPFB into a Blender that would write into the user's own Blender folder."""

    def test_mpfb_refuses_to_load_without_a_user_resource_folder(self):
        # The add-on loader is a stub here, which records a call and loads nothing. So a broken refusal fails the test
        # without writing into the user's own Blender folder.
        code = (
            "import json, sys\n"
            "sys.path.insert(0, {lib!r})\n"
            "import addon_utils, bpy\n"
            "calls = []\n"
            "addon_utils.enable = lambda *args, **kwargs: calls.append(args)\n"
            "from tertius_blender import mpfb\n"
            "try:\n"
            "    mpfb.services()\n"
            "    found = 'loaded'\n"
            "except mpfb.MpfbError as ex:\n"
            "    found = str(ex)\n"
            "print('TERTIUS-TEST', json.dumps({{'found': found, 'calls': len(calls)}}))\n"
        ).format(lib=str(LIBRARY))
        env = {k: v for k, v in os.environ.items() if k != "BLENDER_USER_RESOURCES"}
        report, done = blender(code, env)
        self.assertIsNotNone(report, done.stdout[-3000:] + done.stderr[-3000:])
        self.assertIn("BLENDER_USER_RESOURCES", report["found"])
        self.assertEqual(report["calls"], 0)


if __name__ == "__main__":
    unittest.main()
