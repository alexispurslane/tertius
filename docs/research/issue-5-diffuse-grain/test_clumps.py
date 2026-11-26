"""Clumps count as spatial structure even when they persist through the whole clip."""
import unittest
import numpy as np
from multiscale import fluctuations
from connected_model import connected


class ClumpMeasures(unittest.TestCase):
    def test_a_linear_wash_has_no_band_fluctuation_away_from_edges(self):
        yy,xx=np.mgrid[:96,:96]
        image=np.stack([0.1+xx/200,0.2+yy/200,np.full(xx.shape,0.3)],axis=-1)
        frames=np.repeat(image[None],4,axis=0)
        mask=np.zeros((96,96),bool);mask[32:64,32:64]=True
        self.assertLess(fluctuations(frames,mask,[2])['2']['rgb_rms'],1e-12)

    def test_a_persistent_clump_is_detected_without_inventing_flicker(self):
        yy,xx=np.mgrid[:96,:96]
        blob=0.1*np.exp(-((xx-48)**2+(yy-48)**2)/32)
        frames=np.repeat((0.1+blob)[None,...,None],4,axis=0)*np.ones(3)
        row=fluctuations(frames,np.ones((96,96),dtype=bool),[4])['4']
        self.assertGreater(row['persistent_rgb_rms'],0.001)
        self.assertAlmostEqual(row['temporal_rgb_rms'],0)

    def test_moving_clumps_increase_temporal_color_fluctuation(self):
        yy,xx=np.mgrid[:96,:96]
        frames=np.stack([0.1+0.1*np.exp(-((xx-x)**2+(yy-48)**2)/32) for x in [30,40,50,60]])[...,None]*np.ones(3)
        row=fluctuations(frames,np.ones((96,96),dtype=bool),[4])['4']
        self.assertGreater(row['temporal_rgb_rms'],0.001)

    def test_an_equal_luminance_color_clump_is_still_detected(self):
        frames=np.full((4,96,96,3),0.2)
        frames[:,32:64,32:64]+=np.array([0.07152,-0.02126,0])
        row=fluctuations(frames,np.ones((96,96),dtype=bool),[4])['4']
        self.assertGreater(row['rgb_rms'],0.001)
        self.assertLess(row['luminance_rms'],1e-8)


class ConnectedSupport(unittest.TestCase):
    def test_an_impulse_has_positive_connected_support_and_finite_reach(self):
        image=np.zeros((31,31,3));image[15,15]=[2,4,8]
        out=connected(image,np.zeros((31,31),int),11)
        yy,xx=np.mgrid[:31,:31];r2=(xx-15)**2+(yy-15)**2
        self.assertTrue(np.all(out[...,0][r2<121]>0))
        np.testing.assert_allclose(out[...,0][r2>=121],0)
        np.testing.assert_allclose(out.sum(axis=(0,1)),[2,4,8])
        radius=np.sqrt(np.sum(r2*out[...,0])/2)
        self.assertGreater(radius,5.4)
        self.assertLess(radius,5.6)
        self.assertGreater(out[15,15,0],out[15,16,0])

    def test_a_hole_blocks_exchange_between_matching_endpoints(self):
        image=np.zeros((25,25,3));image[12,8]=[2,4,8]
        surface=np.zeros((25,25),int);surface[:,12]=1
        out=connected(image,surface,11)
        np.testing.assert_allclose(out[:,12:],0)
        np.testing.assert_allclose(out.sum(axis=(0,1)),[2,4,8])

    def test_variable_reach_keeps_rgb_and_cannot_enter_a_zero_reach_dark_patch(self):
        rng=np.random.default_rng(8)
        image=rng.uniform(0,4,(25,25,3))
        reach=rng.uniform(3,11,(25,25));reach[:,13:]=0
        out=connected(image,np.zeros((25,25),int),11,reach)
        np.testing.assert_allclose(out.sum(axis=(0,1)),image.sum(axis=(0,1)),rtol=1e-12)
        np.testing.assert_allclose(out[:,13:],image[:,13:])
        self.assertGreaterEqual(out.min(),0)


if __name__=='__main__':
    unittest.main()
