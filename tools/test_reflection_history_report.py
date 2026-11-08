"""Behavioral checks for the uncapped-history experiment's saved-frame measurements."""

import importlib.util
import math
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "reflection_history_report", Path(__file__).with_name("reflection_history_report.py")
)
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class SavedFrameMeasurements(unittest.TestCase):
    def test_variation_about_the_mean_is_not_reference_error(self):
        result = report.measure([[1.0, 3.0], [3.0, 1.0]], [2.0, 2.0], 2)
        self.assertAlmostEqual(result["noise_rms"], math.sqrt(2))
        self.assertEqual(result["mean_error_rms"], 0)

    def test_a_dark_image_has_no_noise_but_loses_all_reference_light(self):
        result = report.measure([[0.0, 0.0], [0.0, 0.0]], [2.0, 4.0], 2)
        self.assertEqual(result["noise_rms"], 0)
        self.assertEqual(result["missing_fraction"], 1)
        self.assertAlmostEqual(result["mean_error_rms"], math.sqrt(10))

    def test_missing_and_extra_light_do_not_cancel(self):
        result = report.measure([[1.0, 3.0], [1.0, 3.0]], [2.0, 2.0], 2)
        self.assertEqual(result["missing_fraction"], 0.25)
        self.assertEqual(result["extra_fraction"], 0.25)
        self.assertEqual(result["total_fraction"], 1)

    def test_pixel_cell_width_includes_the_inside_of_the_edge_pixels(self):
        result = report.measure([[1.0, 1.0], [1.0, 1.0]], [1.0, 1.0], 2)
        self.assertAlmostEqual(result["central_90_width_pixels"], 1.8)

    def test_response_counts_the_first_changed_draw_as_one(self):
        result = report.response([100.0, 100.0, 50.0, 20.0, 9.0, 8.0], 2, 0)
        self.assertEqual(result["first_90_percent_update"], 3)
        self.assertEqual(result["persistent_90_percent_update"], 3)

    def test_one_dark_sample_does_not_prove_persistent_response(self):
        result = report.response([100.0, 100.0, 9.0, 30.0, 8.0], 2, 0)
        self.assertEqual(result["first_90_percent_update"], 1)
        self.assertEqual(result["persistent_90_percent_update"], 3)

    def test_a_dimmed_light_is_compared_with_the_dimmed_target(self):
        result = report.response([100.0, 100.0, 90.0, 81.0, 80.0], 2, 80)
        self.assertEqual(result["first_90_percent_update"], 2)


if __name__ == "__main__":
    unittest.main()
