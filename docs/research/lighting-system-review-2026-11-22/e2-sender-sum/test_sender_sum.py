"""Small CPU checks. Run with python3 -I test_sender_sum.py."""

import importlib.util
import math
from pathlib import Path
import unittest

import numpy as np

spec = importlib.util.spec_from_file_location("sender_sum", Path(__file__).with_name("sender_sum.py"))
sender = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sender)


class SenderSumTests(unittest.TestCase):
    def test_wall_blocks_a_segment_but_surface_endpoints_do_not(self):
        sources = np.array([[0.0, 0.0, 0.0]])
        receivers = np.array([[0.0, 0.0, 2.0], [2.0, 0.0, 0.0]])
        wall = (np.array([-0.5, -1.0, 0.9]), np.array([0.5, 1.0, 1.1]))
        source_box = (np.array([-1.0, -1.0, -1.0]), np.array([1.0, 1.0, 0.0]))
        visible = sender.segment_visibility(sources, receivers, [wall, source_box])
        np.testing.assert_array_equal(visible, [[False], [True]])

    def test_diffuse_patch_obeys_two_cosines_and_inverse_square_distance(self):
        sources = np.array([[0.0, 0.0, 0.0]])
        receivers = np.array([[0.0, 0.0, 2.0], [0.0, 0.0, 4.0], [2.0, 0.0, 2.0]])
        normals = np.tile([0.0, 0.0, -1.0], (3, 1))
        contributions = sender.patch_irradiance(sources, np.array([0, 0, 1]), np.array([2.0]), receivers, normals)
        np.testing.assert_allclose(contributions[:, 0], [0.5, 0.125, 0.125])

    def test_wall_beyond_the_receiver_does_not_block_light(self):
        wall = (np.array([-1.0, -1.0, 2.0]), np.array([1.0, 1.0, 2.2]))
        visible = sender.segment_visibility(np.array([[0.0, 0.0, 0.0]]), np.array([[0.0, 0.0, 1.0]]), [wall])
        self.assertTrue(bool(visible[0, 0]))

    def test_receiver_facing_away_from_a_sender_gets_no_light(self):
        contributions = sender.patch_irradiance(
            np.array([[0.0, 0.0, 0.0]]), np.array([0, 0, 1]), np.array([1.0]),
            np.array([[0.0, 0.0, 1.0]]), np.array([[0.0, 0.0, 1.0]]))
        self.assertEqual(float(contributions.sum()), 0.0)

    def test_lambert_sender_reflects_its_incident_flux_times_albedo(self):
        block = (np.array([-1.2, 0, -0.75]), np.array([1.2, 1.6, 0.75]))
        light = {"position": [0, 0.8, -2.15], "axis": [0, 0, 1], "intensity": [4, 4, 4],
                 "cosInner": math.cos(math.radians(25)), "cosOuter": math.cos(math.radians(30)),
                 "outerAngle": math.radians(30)}
        patches, radiance_area, flux = sender.senders(8, block, light)
        incident = sender.spot_irradiance(patches, np.tile([0, 0, -1], (len(patches), 1)), light)
        reflected = float(np.sum(incident) * flux["patch_area_m2"] * 0.7)
        self.assertGreater(reflected, 0)
        self.assertAlmostEqual(float(np.sum(radiance_area) * math.pi), reflected)

    def test_sender_emits_nothing_behind_its_face(self):
        contributions = sender.patch_irradiance(
            np.array([[0.0, 0.0, 0.0]]), np.array([0, 0, -1]), np.array([1.0]),
            np.array([[0.0, 0.0, 1.0]]), np.array([[0.0, 0.0, -1.0]]))
        self.assertEqual(float(contributions.sum()), 0.0)

    def test_spot_has_zero_outside_cone_and_inverse_square_at_its_axis(self):
        light = {"position": [0, 0, 0], "axis": [0, 0, 1], "intensity": [4, 4, 4],
                 "cosInner": math.cos(math.radians(25)), "cosOuter": math.cos(math.radians(30))}
        points = np.array([[0.0, 0.0, 2.0], [0.0, 0.0, 4.0], [2.0, 0.0, 2.0]])
        result = sender.spot_irradiance(points, np.tile([0, 0, -1], (3, 1)), light)
        np.testing.assert_allclose(result, [1.0, 0.25, 0.0])


if __name__ == "__main__":
    unittest.main()
