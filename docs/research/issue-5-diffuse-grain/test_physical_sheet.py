"""Tests for the physical cockpit-panel sheet geometry, area-aware no-cutoff heat flow,
physical-point accumulation across moving views, and multi-phase batch convergence checks.
"""
import unittest
import numpy as np
from physical_sheet import (
    SheetMesh,
    build_physical_grid,
    area_aware_heat,
    unproject_to_sheet,
    accumulate_on_sheet,
    batch_convergence,
    load_cockpit_panel_sheet,
)


def make_two_triangle_hinge():
    """Two connected right triangles sharing edge (0,0)-(1,0), hinged by 10 degrees."""
    angle = np.radians(10.0)
    a = np.array([0.0, 0.0, 0.0])
    b = np.array([1.0, 0.0, 0.0])
    c = np.array([0.0, 1.0, 0.0])
    d = np.array([0.0, -np.cos(angle), np.sin(angle)])
    verts = np.array([a, b, c, d], dtype=np.float64)
    tris = np.array([[0, 1, 2], [0, 3, 1]], dtype=np.int64)
    return SheetMesh.from_triangles(verts, tris, root_tri=0)


class PhysicalSheetTests(unittest.TestCase):
    def test_isometric_hinge_unfolding_preserves_triangle_areas_and_edge_lengths(self):
        sheet = make_two_triangle_hinge()
        for ti in range(len(sheet.tris)):
            pts3 = sheet.verts3d[sheet.tris[ti]]
            pts2 = sheet.tris2d[ti]
            area3 = 0.5 * np.linalg.norm(np.cross(pts3[1] - pts3[0], pts3[2] - pts3[0]))
            e1 = pts2[1] - pts2[0]
            e2 = pts2[2] - pts2[0]
            area2 = 0.5 * abs(e1[0] * e2[1] - e1[1] * e2[0])
            self.assertAlmostEqual(area2, area3, places=10)
            for k in range(3):
                d3 = np.linalg.norm(pts3[(k + 1) % 3] - pts3[k])
                d2 = np.linalg.norm(pts2[(k + 1) % 3] - pts2[k])
                self.assertAlmostEqual(d2, d3, places=10)

    def test_area_aware_heat_conserves_physical_area_weighted_rgb_with_nonuniform_cells(self):
        # 1D-like strip of 3x15 cells with strongly varying cell areas (1x to 25x)
        areas = np.ones((3, 15), dtype=np.float64)
        for col in range(15):
            areas[:, col] = 1.0 + 24.0 * (col / 14.0) ** 2
        active = np.ones((3, 15), dtype=bool)
        signal = np.zeros((3, 15, 3), dtype=np.float64)
        signal[1, 2] = [3.0, 1.5, 0.5]
        signal[1, 12] = [0.2, 2.0, 4.0]
        before = np.sum(signal * areas[..., None], axis=(0, 1))
        diffused = area_aware_heat(signal, active, areas, spacing=1.0, diffusion_amount=4.0)
        after = np.sum(diffused * areas[..., None], axis=(0, 1))
        np.testing.assert_allclose(after, before, rtol=1e-10, atol=1e-11)
        # Unweighted mean must NOT stay equal when areas vary; area-weighted mean must.
        self.assertGreater(abs(diffused.mean() - signal.mean()), 1e-3)

    def test_area_aware_heat_peaks_at_source_and_has_no_distance_cutoff(self):
        active = np.ones((33, 33), dtype=bool)
        spacing = 0.2
        areas = np.full((33, 33), spacing * spacing, dtype=np.float64)  # 0.2m x 0.2m cells
        signal = np.zeros((33, 33, 3), dtype=np.float64)
        signal[16, 16] = [1.0, 2.0, 4.0]
        tau = 0.04  # exact interior RMS radial width = sqrt(4 * 0.04) = 0.4 m (2 cells)
        diffused = area_aware_heat(signal, active, areas, spacing=spacing, diffusion_amount=tau)
        # Strict center peak
        self.assertEqual(np.unravel_index(np.argmax(diffused[..., 2]), diffused.shape[:2]), (16, 16))
        self.assertLess(diffused[16, 16, 2], 4.0)
        # Exact interior RMS radial width in metres locks in physical diffusion scale
        yy, xx = np.mgrid[:33, :33]
        r2 = ((xx - 16) ** 2 + (yy - 16) ** 2) * (spacing ** 2)
        rms_radius = np.sqrt(np.sum(r2 * diffused[..., 2]) / np.sum(diffused[..., 2]))
        self.assertAlmostEqual(rms_radius, np.sqrt(4.0 * tau), places=3)
        # Strictly positive far beyond any compact radius (15 cells = 3.0 m away!)
        self.assertGreater(diffused[16, 31, 2], 0.0)
        self.assertGreaterEqual(diffused.min(), -1e-12)

    def test_area_aware_heat_keeps_constant_wash_and_has_zero_leakage_across_gap(self):
        active = np.ones((11, 21), dtype=bool)
        active[:, 10] = False  # Physical gap separating two sheets (like a windshield opening)
        areas = np.where(active, 0.01, 0.0)
        # Unequal boundary areas on left sheet
        areas[:, 0] = 0.003
        areas[:, 9] = 0.006
        # Constant wash stays exact on both sides
        const = np.where(active[..., None], np.array([0.2, 0.5, 0.8]), 0.0)
        out_const = area_aware_heat(const, active, areas, spacing=0.1, diffusion_amount=0.05)
        np.testing.assert_allclose(out_const[active], const[active], atol=1e-12)
        np.testing.assert_allclose(out_const[~active], 0.0, atol=1e-14)
        # Impulse on left sheet never leaks across the gap onto right sheet
        imp = np.zeros((11, 21, 3), dtype=np.float64)
        imp[5, 8] = [2.0, 3.0, 5.0]
        out_imp = area_aware_heat(imp, active, areas, spacing=0.1, diffusion_amount=0.08)
        np.testing.assert_allclose(out_imp[:, 11:], 0.0, atol=1e-14)
        np.testing.assert_allclose(
            np.sum(out_imp[:, :10] * areas[:, :10, None], axis=(0, 1)),
            imp[5, 8] * areas[5, 8],
            rtol=1e-10,
        )

    def test_area_aware_heat_obeys_semigroup_composition(self):
        active = np.ones((13, 13), dtype=bool)
        active[0, 0] = False
        areas = np.where(active, 0.01, 0.0)
        areas[0, 1] = 0.004
        rng = np.random.default_rng(7)
        signal = np.where(active[..., None], rng.uniform(0.0, 1.0, (13, 13, 3)), 0.0)
        two_step = area_aware_heat(
            area_aware_heat(signal, active, areas, spacing=0.1, diffusion_amount=0.015),
            active, areas, spacing=0.1, diffusion_amount=0.025,
        )
        one_step = area_aware_heat(signal, active, areas, spacing=0.1, diffusion_amount=0.040)
        np.testing.assert_allclose(two_step, one_step, rtol=1e-9, atol=1e-10)

    def test_physical_point_accumulation_cancels_fresh_noise_across_moving_camera(self):
        sheet = make_two_triangle_hinge()
        grid = build_physical_grid(sheet, spacing=0.1)
        # Create a smooth physical wash on the sheet plus independent zero-mean noise over 36 camera poses
        yy, xx = np.mgrid[:grid.active.shape[0], :grid.active.shape[1]]
        true_wash = np.stack([
            0.2 + 0.1 * (xx / max(grid.active.shape[1] - 1, 1)),
            0.3 + 0.2 * (yy / max(grid.active.shape[0] - 1, 1)),
            0.4 * np.ones_like(xx, dtype=np.float64),
        ], axis=-1)
        rng = np.random.default_rng(42)
        frames = []
        observed = []
        for t in range(36):
            noise = rng.normal(0.0, 0.25, size=true_wash.shape)
            obs = grid.active.copy()
            frames.append(np.where(obs[..., None], true_wash + noise, 0.0))
            observed.append(obs)
        accum = accumulate_on_sheet(np.stack(frames), np.stack(observed), window=36)
        err_first = np.sqrt(np.mean((frames[0][grid.active] - true_wash[grid.active]) ** 2))
        err_last = np.sqrt(np.mean((accum[-1][grid.active] - true_wash[grid.active]) ** 2))
        self.assertLess(err_last, err_first * 0.25)

    def test_batch_convergence_distinguishes_converged_and_divergent_batches(self):
        rng = np.random.default_rng(11)
        base = np.full((16, 16, 3), [0.1, 0.2, 0.3], dtype=np.float64)
        mask = np.ones((16, 16), dtype=bool)
        a = base + rng.normal(0.0, 0.001, base.shape)
        b = base + rng.normal(0.0, 0.001, base.shape)
        c = base * 1.4
        close = batch_convergence(a, b, mask)
        far = batch_convergence(a, c, mask)
        self.assertLess(close['relative_rms_percent'], 1.5)
        self.assertGreater(far['relative_rms_percent'], 30.0)

    def test_cockpit_panel_sheet_loads_real_shuttle_triangles_with_exact_area(self):
        sheet = load_cockpit_panel_sheet()
        self.assertEqual(len(sheet.tris), 3)
        self.assertAlmostEqual(float(sheet.tri_areas.sum()), 4.5166, places=3)

    def test_unproject_to_sheet_maps_sloped_surface_and_rejects_off_plane_pixels(self):
        sheet = make_two_triangle_hinge()
        grid = build_physical_grid(sheet, spacing=0.2)
        h, w = 16, 16
        eye = np.array([0.3, 0.3, 1.0], dtype=np.float64)
        fwd = np.array([0.0, 0.0, -1.0], dtype=np.float64)
        right_tan = np.array([0.5, 0.0, 0.0], dtype=np.float64)
        up_tan = np.array([0.0, 0.5, 0.0], dtype=np.float64)
        camera = dict(eye=eye, forward=fwd, right_tan=right_tan, up_tan=up_tan, jitter=[0.0, 0.0], width=w, height=h)
        yy, xx = np.mgrid[:h, :w]
        ndc_x = (xx + 0.5) / w * 2.0 - 1.0
        ndc_y = (yy + 0.5) / h * 2.0 - 1.0
        ray = fwd[None, None, :] + ndc_x[..., None] * right_tan[None, None, :] - ndc_y[..., None] * up_tan[None, None, :]
        ray_len = np.linalg.norm(ray, axis=-1)
        # Plane z = 0 is 1.0 m along forward, so Euclidean distance is 1.0 * ray_len
        dist = 1.0 * ray_len
        # Place an occluder in front of the top-left corner (off the triangle plane by 0.4 m)
        dist[:4, :4] = 0.6 * ray_len[:4, :4]
        rgb = np.full((h, w, 3), [0.2, 0.5, 0.8], dtype=np.float64)
        c_rgb, c_obs, _c_den, p_mask, _p_rc, p_area = unproject_to_sheet(rgb, dist, camera, sheet, grid)
        self.assertFalse(np.any(p_mask[:4, :4]))
        self.assertTrue(np.any(c_obs))
        np.testing.assert_allclose(c_rgb[c_obs], np.broadcast_to([0.2, 0.5, 0.8], c_rgb[c_obs].shape), atol=1e-12)
        self.assertGreater(float(p_area[p_mask].min()), 0.0)
        # Zero-diffusion and empty-active edge cases
        np.testing.assert_allclose(
            area_aware_heat(c_rgb, c_obs, grid.areas, grid.spacing, 0.0)[c_obs],
            c_rgb[c_obs],
            atol=1e-14,
        )
        empty = np.zeros_like(c_obs)
        np.testing.assert_allclose(area_aware_heat(c_rgb, empty, grid.areas, grid.spacing, 0.01), 0.0)


if __name__ == '__main__':
    unittest.main()
