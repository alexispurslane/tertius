"""Tool code (t1-8b4w): the tests of films.py. Run: python3 -I -m unittest test_films (from this folder)."""
import unittest

import numpy as np

import films


def gray(value, shape=(4, 4)):
    return np.full(shape + (3,), value, dtype=np.uint8)


class TileMeanTests(unittest.TestCase):
    def test_each_tile_holds_the_mean_of_its_pixels(self):
        image = np.zeros((4, 4))
        image[:2, :2] = 8.0
        image[0, 2] = 4.0
        np.testing.assert_allclose(films.tile_mean(image, 2), [[8.0, 1.0], [0.0, 0.0]])

    def test_a_partial_tile_at_the_edge_is_left_out(self):
        self.assertEqual(films.tile_mean(np.ones((5, 7)), 2).shape, (2, 3))


class FlickerTests(unittest.TestCase):
    def test_a_still_film_has_no_flicker(self):
        np.testing.assert_allclose(films.flicker([gray(100), gray(100), gray(100)], 2), np.zeros((2, 2)))

    def test_flicker_is_the_mean_absolute_change_between_consecutive_frames_in_luminance_of_one_255th(self):
        # Two changes of 10 and one of 0: the mean is 20 / 3 in every tile.
        frames = [gray(100), gray(110), gray(100), gray(100)]
        np.testing.assert_allclose(films.flicker(frames, 2), np.full((2, 2), 20.0 / 3.0))

    def test_a_steady_drift_counts_as_its_step_not_as_its_total(self):
        frames = [gray(100 + 2 * i) for i in range(6)]
        np.testing.assert_allclose(films.flicker(frames, 2), np.full((2, 2), 2.0))

    def test_a_changing_pixel_counts_only_in_its_own_tile(self):
        a, b = gray(50), gray(50)
        b[0, 0] = 54
        result = films.flicker([a, b], 2)
        self.assertAlmostEqual(result[0, 0], 1.0)
        self.assertEqual(result[1, 1], 0.0)


if __name__ == "__main__":
    unittest.main()
