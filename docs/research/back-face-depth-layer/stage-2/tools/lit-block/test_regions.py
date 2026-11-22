"""Tool code (t1-r9vj): tests of regions.py. Run: python3 -I test_regions.py (in the dev shell, for NumPy)."""
import sys
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import regions  # noqa: E402


def scene():
    """A 6 x 6 image: the left half faces +x, the right half +y; the camera-relative z of column i is -i m."""
    normal = np.zeros((6, 6, 3))
    normal[:, :3] = (1, 0, 0)
    normal[:, 3:] = (0, 1, 0)
    position = np.zeros((6, 6, 3))
    position[:, :, 2] = -np.arange(6)[None, :]
    return normal, position


class RegionMask(unittest.TestCase):
    def test_a_box_alone_keeps_every_pixel_of_the_box(self):
        normal, position = scene()
        self.assertEqual(int(regions.region_mask({"box": [1, 1, 4, 3]}, normal, position).sum()), 6)

    def test_a_normal_keeps_only_that_face_and_drops_the_pixels_on_its_edge_with_another_face(self):
        normal, position = scene()
        mask = regions.region_mask({"box": [0, 0, 6, 6], "normal": [0, 1, 0]}, normal, position)
        self.assertEqual([int(x) for x in np.flatnonzero(mask[2])], [4, 5])

    def test_a_face_keeps_its_pixels_on_the_border_of_the_image_as_t1_g4p1s_ratios_did(self):
        normal, position = scene()
        mask = regions.region_mask({"box": [0, 0, 6, 6], "normal": [0, 1, 0]}, normal, position)
        self.assertEqual(sorted({int(y) for y in np.argwhere(mask)[:, 0]}), [0, 1, 2, 3, 4, 5])

    def test_a_position_range_keeps_only_the_pixels_inside_it(self):
        normal, position = scene()
        mask = regions.region_mask({"box": [0, 0, 6, 6], "z": [-4.5, -0.5]}, normal, position)
        self.assertEqual([int(x) for x in np.flatnonzero(mask[2])], [2, 3])


if __name__ == "__main__":
    unittest.main()
