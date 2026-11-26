"""Unit tests for cross-shot script extraction, multiscale masking, and AccumulationTrace decoding."""
from pathlib import Path
import tempfile
import unittest
import numpy as np

from shots_render import prefix_steps_to_tick
from shots_analysis import multiscale_crop
from mainstream_analysis import (
    output_box_to_render_box,
    summarize_trace,
    TRACED_SURFACE_HOLDS,
    TRACED_REFLECTED_TRIED,
    TRACED_REFLECTED_WON,
    TRACED_SAMPLE,
    TRACED_SURFACE_PIXEL,
)
from sender_analysis import (
    summarize_subset,
    FLAG_TRACED,
    FLAG_WALK_HIT,
    FLAG_GUIDED,
    FLAG_GUIDED_COUNTED,
    FLAG_OWN_RAY,
)


class CrossAndMainstreamTests(unittest.TestCase):
    def test_prefix_steps_to_tick_reaches_exact_tick_and_preserves_posts(self):
        script = {
            'steps': [
                {'do': 'post', 'target': 'a'},
                {'do': 'run', 'ticks': 10},
                {'do': 'film', 'name': 'f1', 'frames': 5, 'every': 4},  # 20 ticks -> 30
                {'do': 'post', 'target': 'b'},
                {'do': 'film', 'name': 'f2', 'frames': 10, 'every': 1},  # 10 ticks -> 40
            ]
        }
        out = prefix_steps_to_tick(script, 33)
        total_ticks = sum(st['ticks'] for st in out if st['do'] == 'run')
        posts = [st['target'] for st in out if st['do'] == 'post']
        self.assertEqual(total_ticks, 33)
        self.assertEqual(posts, ['a', 'b'])

    def test_output_box_to_render_box_scales_and_clamps(self):
        self.assertEqual(output_box_to_render_box((640, 360, 1280, 720), 1280, 720, 848, 477), (424, 238, 848, 477))

    def test_summarize_trace_separates_reprojection_holds_from_step4_clamp_cut(self):
        with tempfile.TemporaryDirectory() as tmp:
            tdir = Path(tmp)
            h, w = 8, 8
            (tdir / 'size.txt').write_text(f'{w} {h}')
            l0 = np.zeros((h, w, 4), dtype='<f4')
            l2 = np.zeros((h, w, 4), dtype='<f4')
            l4 = np.zeros((h, w, 4), dtype='<f4')
            l5 = np.zeros((h, w, 4), dtype='<f4')
            l6 = np.zeros((h, w, 4), dtype='<f4')
            spec = np.zeros((h, w, 4), dtype=np.uint8)
            dist = np.full((h, w), 3.0, dtype='<f4')

            # Rough surface: surface holds, reflected not tried, Step 4 clamps length 12 -> 4
            flags_rough = float(TRACED_SURFACE_HOLDS | TRACED_SAMPLE | TRACED_SURFACE_PIXEL)
            l0[:, :4, :] = [11.0, 11.0, 1.0, flags_rough]
            l2[:, :4, :2] = [0.5, 0.0]
            l4[:, :4, :] = [12.0, 4.0, 0.0, 0.0]
            l5[:, :4, 3] = 1.0
            l6[:, :4, 3] = 0.8
            spec[:, :4, 1] = 128  # ~0.502 roughness

            # Glossy surface: reflected tried and won, no cut
            flags_glossy = float(
                TRACED_SURFACE_HOLDS | TRACED_REFLECTED_TRIED | TRACED_REFLECTED_WON | TRACED_SAMPLE | TRACED_SURFACE_PIXEL
            )
            l0[:, 4:, :] = [3.0, 3.0, 1.0, flags_glossy]
            l4[:, 4:, :] = [4.0, 4.0, 0.0, 0.0]
            spec[:, 4:, 1] = 51  # ~0.20 roughness

            for name, arr in [
                ('000048.layer0.f32', l0),
                ('000048.layer2.f32', l2),
                ('000048.layer4.f32', l4),
                ('000048.layer5.f32', l5),
                ('000048.layer6.f32', l6),
                ('000048.specular.u8', spec),
                ('000048.distance.f32', dist),
            ]:
                arr.tofile(tdir / name)

            res = summarize_trace(tdir, {'rough': (0, 0, 4, 8), 'glossy': (4, 0, 8, 8)}, out_w=8, out_h=8)
            r_pool = res['by_crop']['rough']['pooled']
            g_pool = res['by_crop']['glossy']['pooled']
            self.assertAlmostEqual(r_pool['surface_holds_frac'], 1.0)
            self.assertAlmostEqual(r_pool['reflected_tried_frac'], 0.0)
            self.assertAlmostEqual(r_pool['clamp_ran_frac'], 1.0)
            self.assertAlmostEqual(r_pool['clamp_cut_frac'], 1.0)
            self.assertAlmostEqual(r_pool['len_before_step4_mean_p50_p90'][1], 12.0)
            self.assertAlmostEqual(r_pool['len_after_step4_mean_p50_p90'][1], 4.0)
            self.assertAlmostEqual(g_pool['reflected_tried_frac'], 1.0)
            self.assertAlmostEqual(g_pool['reflected_won_frac'], 1.0)
            self.assertAlmostEqual(g_pool['clamp_cut_frac'], 0.0)

    def test_multiscale_crop_masks_clean_edges_and_measures_interior_noise(self):
        rng = np.random.default_rng(3)
        clean = np.full((6, 64, 64, 3), 0.1, dtype=np.float32)
        clean[:, :, 32:, :] = 0.6  # sharp vertical edge in the middle
        noisy = clean + rng.normal(0.0, 0.02, clean.shape).astype(np.float32)
        b_clean = multiscale_crop(clean, clean, (8, 8, 56, 56), scales=(1, 2))
        b_noisy = multiscale_crop(noisy, clean, (8, 8, 56, 56), scales=(1, 2))
        self.assertLess(b_clean['1']['rgb_rms'], 0.002)
        self.assertGreater(b_noisy['1']['rgb_rms'], 3.0 * b_clean['1']['rgb_rms'])

    def test_summarize_subset_separates_unique_rays_from_2x2_receivers_and_splits_fade(self):
        # 4 receiver pixels sharing 1 block ray (hit with fade=0.5) + 1 own ray (miss, ordinary)
        out_rgb = np.array(
            [
                [1.0, 1.0, 1.0],
                [1.0, 1.0, 1.0],
                [1.0, 1.0, 1.0],
                [1.0, 1.0, 1.0],
                [2.0, 2.0, 2.0],
            ],
            dtype=np.float64,
        )
        out_lum = np.array([1.0, 1.0, 1.0, 1.0, 2.0], dtype=np.float64)
        ray_ids = np.array([100, 100, 100, 100, 200], dtype=np.int64)
        flags = np.array(
            [
                FLAG_TRACED | FLAG_WALK_HIT | FLAG_GUIDED | FLAG_GUIDED_COUNTED,
                FLAG_TRACED | FLAG_WALK_HIT | FLAG_GUIDED | FLAG_GUIDED_COUNTED,
                FLAG_TRACED | FLAG_WALK_HIT | FLAG_GUIDED | FLAG_GUIDED_COUNTED,
                FLAG_TRACED | FLAG_WALK_HIT | FLAG_GUIDED | FLAG_GUIDED_COUNTED,
                FLAG_TRACED | FLAG_OWN_RAY,
            ],
            dtype=np.uint32,
        )
        fade = np.array([0.5, 0.5, 0.5, 0.5, 0.0], dtype=np.float64)
        miss_lum = np.array([2.0, 2.0, 2.0, 2.0, 3.0], dtype=np.float64)
        leaving_lum = np.array([2.0, 2.0, 2.0, 2.0, 0.0], dtype=np.float64)
        inc_lum = np.array([2.0, 2.0, 2.0, 2.0, 3.0], dtype=np.float64)
        w_cap = np.array([4.0, 4.0, 4.0, 4.0, 1.0], dtype=np.float64)
        w_uncap = np.array([12.0, 12.0, 12.0, 12.0, 1.0], dtype=np.float64)
        lobe_d = np.array([0.1, 0.1, 0.1, 0.1, 0.3], dtype=np.float64)
        guid_d = np.array([0.5, 0.5, 0.5, 0.5, 0.0], dtype=np.float64)
        regions = np.array(['console-cyan-emissive-screens'] * 4 + ['offscreen-miss'], dtype=object)
        guide_hit = np.array([5.0, 5.0, 5.0, 5.0, 0.0], dtype=np.float64)

        res = summarize_subset(
            'test', np.ones(5, dtype=bool), out_rgb, out_lum, ray_ids, flags, fade, miss_lum, leaving_lum,
            inc_lum, w_cap, w_uncap, lobe_d, guid_d, regions, guide_hit,
        )
        self.assertEqual(res['receiver_records'], 5)
        self.assertEqual(res['unique_rays'], 2)
        self.assertEqual(res['unique_block_rays'], 1)
        self.assertEqual(res['unique_own_rays'], 1)
        # Total lum = 6.0; block ray has lum 4.0 split 50/50 -> 2.0 onscreen, 2.0 offscreen; own miss = 2.0 offscreen
        self.assertAlmostEqual(res['onscreen_vs_offscreen']['onscreen_lum_share'], 2.0 / 6.0, places=6)
        self.assertAlmostEqual(res['onscreen_vs_offscreen']['offscreen_env_lum_share'], 4.0 / 6.0, places=6)
        self.assertAlmostEqual(res['guided_vs_ordinary']['guided_lum_share'], 4.0 / 6.0, places=6)
        self.assertAlmostEqual(res['brightness_vs_brdf_weight']['hit_most_ray_weight_cap4_frac'], 0.8, places=6)


if __name__ == '__main__':
    unittest.main()
