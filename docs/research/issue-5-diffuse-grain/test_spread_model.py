"""The fixed symmetric exchange must preserve energy and material boundaries."""
import unittest
import numpy as np
from spread_model import spread


class SymmetricExchange(unittest.TestCase):
    def test_an_impulse_keeps_its_rgb_energy_while_its_peak_falls(self):
        image = np.zeros((25, 25, 3)); image[12,12] = [2,4,8]
        result = spread(image, np.zeros((25,25), dtype=int), 2)
        np.testing.assert_allclose(result.sum(axis=(0,1)), [2,4,8])
        self.assertLess(result.max(), 1)
        self.assertGreaterEqual(result.min(), 0)

    def test_an_interior_impulse_spreads_equally_to_the_center_and_its_neighbors(self):
        image = np.zeros((25,25,3)); image[12,12] = [2,4,8]
        result = spread(image,np.zeros((25,25),dtype=int),2)
        np.testing.assert_allclose(result[12,12], result[10,10])
        self.assertGreater(result[12,12,2], 0)

    def test_exchange_cannot_cross_a_surface_boundary(self):
        image = np.zeros((25,25,3)); image[12,11] = [2,4,8]
        surface = np.zeros((25,25),dtype=int); surface[:,12:] = 1
        result = spread(image,surface,2)
        np.testing.assert_allclose(result[:,12:], 0)
        np.testing.assert_allclose(result.sum(axis=(0,1)), [2,4,8])

    def test_a_constant_wash_stays_constant_at_image_edges(self):
        image = np.full((25,25,3), [0.2,0.4,0.8])
        np.testing.assert_allclose(spread(image,np.zeros((25,25),dtype=int),6), image)


if __name__ == '__main__':
    unittest.main()
