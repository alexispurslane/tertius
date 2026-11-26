"""Heat flow keeps channel energy and has tails beyond any chosen kernel width."""
import unittest
import numpy as np
from heat_reference import heat


class InsulatingHeat(unittest.TestCase):
    def test_rectangular_cells_diffuse_equally_along_both_axes(self):
        image=np.zeros((65,97,3));image[32,48]=[1,2,4]
        result=heat(image,4)
        yy,xx=np.mgrid[:65,:97]
        self.assertAlmostEqual(np.sum((xx-48)**2*result[...,0]),8,places=7)
        self.assertAlmostEqual(np.sum((yy-32)**2*result[...,0]),8,places=7)

    def test_an_impulse_has_nonzero_tails_beyond_eleven_cells(self):
        image=np.zeros((65,65,3));image[32,32]=[2,4,8]
        result=heat(image,7.5625)
        self.assertGreater(result[32,47,2],1e-6)
        self.assertLess(result[32,32,2],8)
        np.testing.assert_allclose(result.sum(axis=(0,1)),[2,4,8],atol=1e-12)
        self.assertGreaterEqual(result.min(),-1e-14)
        yy,xx=np.mgrid[:65,:65]
        radius=np.sqrt(np.sum(((xx-32)**2+(yy-32)**2)*result[...,0])/2)
        self.assertAlmostEqual(radius,5.5,places=5)

    def test_an_impulse_at_the_boundary_keeps_its_rgb_inside_the_domain(self):
        image=np.zeros((33,33,3));image[0,0]=[2,4,8]
        result=heat(image,7.5625)
        np.testing.assert_allclose(result.sum(axis=(0,1)),[2,4,8],atol=1e-12)
        self.assertGreater(result[0,1,2],0)
        self.assertGreaterEqual(result.min(),-1e-14)

    def test_a_constant_wash_stays_constant_at_the_boundary(self):
        image=np.full((33,33,3),[0.1,0.2,0.4])
        np.testing.assert_allclose(heat(image,7.5625),image,atol=1e-14)

    def test_two_spatial_diffusions_equal_one_with_the_sum_of_the_amounts(self):
        image=np.random.default_rng(12).uniform(0,1,(33,33,3))
        np.testing.assert_allclose(heat(heat(image,2),5),heat(image,7),atol=1e-13)


if __name__=='__main__':
    unittest.main()
