"""Controlled images establish what the experiment's grain measures detect."""
import unittest
import numpy as np
from measure import grain, compare, color_wash


class GrainMeasures(unittest.TestCase):
    def test_a_flat_unchanging_surface_has_no_grain(self):
        frames = np.full((4, 32, 32), 0.25)
        result = grain(frames)
        self.assertAlmostEqual(result['mean_luminance'], 0.25)
        self.assertAlmostEqual(result['spatial_rms'], 0)
        self.assertAlmostEqual(result['temporal_rms'], 0)

    def test_fine_speckles_increase_spatial_grain(self):
        flat = np.full((4, 32, 32), 0.25)
        speckles = flat + np.random.default_rng(5).normal(0, 0.03, flat.shape)
        self.assertGreater(grain(speckles)['spatial_rms'], 0.02)

    def test_changing_speckles_increase_temporal_grain(self):
        rng = np.random.default_rng(5)
        fixed = np.repeat(rng.uniform(0.2, 0.3, (1, 32, 32)), 4, axis=0)
        changing = rng.uniform(0.2, 0.3, (4, 32, 32))
        self.assertAlmostEqual(grain(fixed)['temporal_rms'], 0)
        self.assertGreater(grain(changing)['temporal_rms'], 0.02)

    def test_a_uniform_exposure_change_does_not_count_as_fine_grain(self):
        frames = np.stack([np.full((32, 32), x) for x in (0.2, 0.3, 0.4)])
        self.assertAlmostEqual(grain(frames)['temporal_rms'], 0)

    def test_a_darker_variant_reports_its_brightness_loss(self):
        baseline = np.full((4, 32, 32), 0.25)
        result = compare(baseline, baseline * 0.8)
        self.assertAlmostEqual(result['mean_change_percent'], -20)
        self.assertAlmostEqual(result['paired_rms'], 0.05)


class ColorWashMeasures(unittest.TestCase):
    def test_an_unchanged_color_wash_has_zero_error_in_every_patch(self):
        frames = np.full((4, 32, 32, 3), 0.25)
        result = color_wash(frames, frames)
        self.assertEqual(len(result), 4)
        for patch in result.values():
            self.assertAlmostEqual(patch['wash_relative_rms_percent'], 0)

    def test_moving_colors_between_patches_is_visible_despite_the_same_whole_panel_mean(self):
        frames = np.full((4, 32, 32, 3), 0.1)
        frames[:, :, :16, 0] = 0.3
        frames[:, :, 16:, 2] = 0.3
        swapped = frames[:, :, ::-1, :]
        np.testing.assert_allclose(frames.mean(axis=(0, 1, 2)), swapped.mean(axis=(0, 1, 2)))
        result = color_wash(frames, swapped)
        self.assertEqual(len(result), 4)
        for patch in result.values():
            self.assertGreater(patch['wash_relative_rms_percent'], 40)


if __name__ == '__main__':
    unittest.main()
