"""The fixed symmetric exchange must preserve energy and material boundaries."""
import unittest
import numpy as np
from spread_model import spread, irregular, tangent_pair


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


class IrregularExchange(unittest.TestCase):
    def test_a_reciprocal_irregular_graph_preserves_rgb_and_nonnegative_light(self):
        rng = np.random.default_rng(51)
        image = rng.uniform(0, 4, (30,30,3))
        edges = rng.random((12,30,30)) < 0.75
        result = irregular(image,np.zeros((30,30),dtype=int),edges)
        np.testing.assert_allclose(result.sum(axis=(0,1)), image.sum(axis=(0,1)),rtol=1e-12)
        self.assertGreaterEqual(result.min(), 0)
        self.assertFalse(np.allclose(result,image))

    def test_light_crosses_artificial_modulo_six_phases_but_cannot_cross_a_surface_boundary(self):
        image = np.zeros((30,30,3)); image[15,14] = [2,4,8]
        surface = np.zeros((30,30),dtype=int); surface[:,16:] = 1
        result = irregular(image,surface,np.ones((12,30,30),dtype=bool))
        self.assertGreater(result[15,15,2], 0)
        np.testing.assert_allclose(result[:,16:],0)
        np.testing.assert_allclose(result.sum(axis=(0,1)),[2,4,8])


class TangentFootprint(unittest.TestCase):
    def test_equal_surface_distances_get_the_same_circular_support(self):
        origin = np.zeros(3)
        normal = np.array([0., 0., 1.])
        self.assertTrue(tangent_pair(origin, np.array([0.6, 0.8, 0.]), normal, normal, 1., 1.))
        self.assertFalse(tangent_pair(origin, np.array([1.2, 0., 0.]), normal, normal, 1., 1.))

    def test_both_endpoint_planes_must_accept_the_distance(self):
        first = np.zeros(3)
        second = np.array([0., 0., 2.])
        first_normal = np.array([0., 0., 1.])
        second_normal = np.array([1., 0., 0.])
        self.assertFalse(tangent_pair(first, second, first_normal, second_normal, 1., 1.))
        self.assertFalse(tangent_pair(second, first, second_normal, first_normal, 1., 1.))

    def test_the_smaller_endpoint_footprint_bounds_both_directions(self):
        first = np.zeros(3)
        second = np.array([2., 0., 0.])
        normal = np.array([0., 0., 1.])
        self.assertFalse(tangent_pair(first, second, normal, normal, 3., 1.))
        self.assertFalse(tangent_pair(second, first, normal, normal, 1., 3.))

    def test_plane_separation_is_left_to_the_separate_compatibility_guard(self):
        normal = np.array([0., 0., 1.])
        self.assertTrue(tangent_pair(np.zeros(3), np.array([0.6, 0.8, 4.]), normal, normal, 1., 1.))

    def test_a_sloped_surface_exchanges_with_near_surface_points_and_retains_rgb(self):
        image = np.zeros((30,30,3)); image[15,15] = [2,4,8]
        yy, xx = np.mgrid[:30,:30]
        points = np.stack([8.*xx, yy, 8.*xx], axis=-1)
        normals = np.broadcast_to(np.array([-1.,0.,1.])/np.sqrt(2), points.shape)
        radii = np.full((30,30), 1.1)
        result = irregular(image, np.zeros((30,30),dtype=int),
            np.ones((12,30,30),dtype=bool), (points,normals,radii))
        np.testing.assert_allclose(result[15,16], 0.)
        self.assertGreater(result[16,15,2], 0.)
        np.testing.assert_allclose(result.sum(axis=(0,1)), [2,4,8])
        self.assertGreaterEqual(result.min(), 0.)


if __name__ == '__main__':
    unittest.main()
