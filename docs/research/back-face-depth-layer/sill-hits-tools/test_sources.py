"""Tool code (ter-ukw6): the tests of sources.py.  python3 test_sources.py."""
import unittest

from sources import source_of, tally

REGIONS = [("sill-edge", 593, 376, 687, 377), ("wall-below", 593, 377, 687, 386), ("floor-patch", 560, 387, 720, 390)]


class SourceOf(unittest.TestCase):
    def test_a_hit_pixel_inside_a_region_takes_its_name(self):
        self.assertEqual(source_of(600, 376, REGIONS), "sill-edge")

    def test_a_region_excludes_its_last_row_and_column(self):
        self.assertEqual(source_of(687, 376, REGIONS), "other")
        self.assertEqual(source_of(600, 386, REGIONS), "other")

    def test_a_hit_pixel_outside_every_region_is_other(self):
        self.assertEqual(source_of(100, 100, REGIONS), "other")

    def test_the_first_region_that_holds_the_pixel_wins(self):
        self.assertEqual(source_of(600, 388, [("a", 0, 0, 1280, 720), ("floor-patch", 560, 387, 720, 390)]), "a")


class Tally(unittest.TestCase):
    LINES = [
        "draws 400 size 1280x720",
        "sill-glow crossing 0 reason 0 5 500.0",
        "sillpixel-glow 600 376 0 0 3 300.5",
        "sillpixel-glow 610 376 0 0 2 199.5",
        "sillpixel-glow 600 388 1 0 4 40.0",
        "sillpixel-cloud 600 376 0 0 9 900.0",
        "sillfade-glow 0 31 5 500.0",
    ]

    def test_it_sums_the_samples_and_light_of_the_box_by_class_and_source(self):
        self.assertEqual(
            tally(self.LINES, "glow", REGIONS),
            {("0", "sill-edge"): [5, 500.0], ("1", "floor-patch"): [4, 40.0]},
        )

    def test_it_ignores_the_lines_of_other_boxes(self):
        self.assertNotIn(("0", "sill-edge", 9), [(k[0], k[1], v[0]) for k, v in tally(self.LINES, "glow", REGIONS).items()])


if __name__ == "__main__":
    unittest.main()
