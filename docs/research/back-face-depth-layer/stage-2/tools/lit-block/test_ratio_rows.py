"""Tool code (t1-r9vj): tests of ratio_rows.py. Run: python3 -I test_ratio_rows.py"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import ratio_rows  # noqa: E402


class RatioRows(unittest.TestCase):
    def test_a_ratio_is_the_region_mean_over_the_neutral_mean_and_leaves_the_neutral_region_out(self):
        self.assertEqual(ratio_rows.ratios({"wall": 3.0, "floor": 2.0}, "floor"), {"wall": 1.5})

    def test_the_frame_noise_is_the_absolute_change_of_each_ratio_between_two_frames(self):
        early = {"wall": 2.0, "floor": 1.0}
        late = {"wall": 3.0, "floor": 2.0}
        self.assertAlmostEqual(ratio_rows.frame_noise(early, late, "floor")["wall"], 0.5)

    def test_the_nearest_column_has_the_smallest_absolute_difference_from_the_reference_and_names_the_next(self):
        rows = {"layer": {"wall": 0.5}, "dfront": {"wall": 1.6}, "ten": {"wall": 0.2}}
        self.assertEqual(ratio_rows.nearest(rows, {"wall": 1.0}), {"wall": ("layer", "dfront")})

    def test_a_tie_for_nearest_keeps_the_order_of_the_columns(self):
        rows = {"layer": {"wall": 0.8}, "dfront": {"wall": 1.2}}
        self.assertEqual(ratio_rows.nearest(rows, {"wall": 1.0})["wall"], ("layer", "dfront"))


if __name__ == "__main__":
    unittest.main()
