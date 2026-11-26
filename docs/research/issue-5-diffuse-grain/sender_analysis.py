"""Analyze sender-side upper-tail & light-pyramid attribution on the 1:41 cockpit.

Separates unique diffuse rays from 2x2 receiver records, splits weighted outgoing RGB
into on-screen source location/material vs actual offscreen/environment fallback,
guided vs ordinary draws, incoming brightness vs receiving BRDF weight (and MostRayWeight=4
caps), and audits whether the existing light pyramid sees and samples the dominant sources.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float64)

FLAG_TRACED = 1
FLAG_WALK_HIT = 2
FLAG_GUIDED = 4
FLAG_GUIDED_COUNTED = 8
FLAG_OWN_RAY = 16


def classify_onscreen_region(hx, hy, ren_w, ren_h, emissive_lum, direct_lum, base_rgb):
    """Classify an on-screen hit pixel (in render coordinates) into human-readable cockpit features."""
    # Map render pixel to 1280x720 output coordinates for clear spatial regions
    ox = hx * (1280.0 / ren_w)
    oy = hy * (720.0 / ren_h)
    regions = np.full(len(hx), 'other-onscreen', dtype=object)

    # Check cyan/bright emissive console screens & cockpit displays (high emissive, around console x=[560..840], y=[470..640])
    is_emissive = emissive_lum > 0.05
    cyan_dom = is_emissive & (base_rgb[:, 2] + base_rgb[:, 1] >= 0.0) & (ox >= 520) & (ox <= 860) & (oy >= 440) & (oy <= 640)
    # Bright overhead cockpit lamp / windshield glare area (x=[100..660], y=[0..240])
    overhead_bright = (ox >= 80) & (ox <= 700) & (oy < 250)
    # Pilot suit / helmet (x=[420..620], y=[290..620])
    pilot = (ox >= 420) & (ox <= 620) & (oy >= 290) & (oy <= 620)
    # Seat backrest/chair (x=[340..540], y=[350..690])
    seat = (ox >= 340) & (ox <= 540) & (oy >= 350) & (oy <= 690)
    # Windshield frame / struts / lower sill (x=[500..940], y=[200..470])
    windshield_frame = (ox >= 500) & (ox <= 940) & (oy >= 180) & (oy <= 470)
    # Dashboard / center console top & sides (x=[540..940], y=[450..650])
    console = (ox >= 540) & (ox <= 940) & (oy >= 440) & (oy <= 650)
    # Sloped starboard panel itself (x=[660..1280], y=[0..450])
    sloped_self = (ox >= 660) & (oy < 450)
    # Cabin floor (y >= 540)
    floor = oy >= 540
    # Left wall / port interior (x < 420, y >= 240)
    port_wall = (ox < 420) & (oy >= 240)
    # Right lower side wall (x >= 940, y >= 450)
    stbd_wall = (ox >= 940) & (oy >= 450)

    # Assign in priority order (most specific last)
    regions[port_wall] = 'port-wall'
    regions[stbd_wall] = 'starboard-wall'
    regions[floor] = 'cabin-floor'
    regions[sloped_self] = 'sloped-panel-self'
    regions[windshield_frame] = 'windshield-frame-and-sill'
    regions[console] = 'console-housing'
    regions[seat] = 'pilot-seat'
    regions[pilot] = 'pilot-suit-and-helmet'
    regions[overhead_bright] = 'overhead-lamp-and-canopy'
    regions[cyan_dom] = 'console-cyan-emissive-screens'
    # Any other high-emissive surface outside console screens
    other_em = is_emissive & (~cyan_dom)
    regions[other_em & overhead_bright] = 'overhead-emissive-lamp'
    regions[other_em & (~overhead_bright)] = 'other-emissive'
    return regions


def summarize_subset(name, mask, out_rgb, out_lum, ray_ids, flags, fade, miss_lum, leaving_lum, inc_lum, w_cap, w_uncap, lobe_d, guid_d, regions, guide_at_hit):
    idx = np.where(mask)[0]
    n_rec = len(idx)
    if n_rec == 0:
        return dict(name=name, receiver_records=0, unique_rays=0)

    rgb_sum = out_rgb[idx].sum(axis=0)
    lum_sum = float(out_lum[idx].sum())

    # Deduplicate by unique ray id (draw, is_own, rx, ry)
    rids = ray_ids[idx]
    uniq_ids, first_pos, inv = np.unique(rids, return_index=True, return_inverse=True)
    n_uniq = len(uniq_ids)
    u_idx = idx[first_pos]

    u_flags = flags[u_idx]
    u_guided = (u_flags & FLAG_GUIDED) != 0
    u_hit = (u_flags & FLAG_WALK_HIT) != 0
    u_counted = (u_flags & FLAG_GUIDED_COUNTED) != 0
    u_own = (u_flags & FLAG_OWN_RAY) != 0

    r_flags = flags[idx]
    r_guided = (r_flags & FLAG_GUIDED) != 0
    r_hit = (r_flags & FLAG_WALK_HIT) != 0
    r_fade = fade[idx]
    r_ml = miss_lum[idx]
    r_ll = leaving_lum[idx]

    # Exact linear light split of diffuse.light = mix(missLight, leaving, fade) on hits, or missLight on misses (0 on uncounted guided)
    hit_part = np.where(r_hit, r_fade * r_ll, 0.0)
    miss_part = np.where(r_hit, (1.0 - r_fade) * r_ml, np.where(~r_guided, r_ml, 0.0))
    denom = np.maximum(hit_part + miss_part, 1e-20)
    onscreen_frac = hit_part / denom
    offscreen_frac = miss_part / denom

    onscreen_rgb = (out_rgb[idx] * onscreen_frac[:, None]).sum(axis=0)
    offscreen_rgb = (out_rgb[idx] * offscreen_frac[:, None]).sum(axis=0)
    onscreen_lum = float((out_lum[idx] * onscreen_frac).sum())
    offscreen_lum = float((out_lum[idx] * offscreen_frac).sum())

    guided_rgb = out_rgb[idx][r_guided].sum(axis=0)
    ordinary_rgb = out_rgb[idx][~r_guided].sum(axis=0)
    guided_lum = float(out_lum[idx][r_guided].sum())
    ordinary_lum = float(out_lum[idx][~r_guided].sum())

    # On-screen region breakdown (weighted by onscreen_frac * out_lum and unique-ray counts)
    reg_breakdown = {}
    hit_idx = idx[r_hit]
    hit_regs = regions[hit_idx]
    hit_w_lum = out_lum[hit_idx] * onscreen_frac[r_hit]
    hit_w_rgb = out_rgb[hit_idx] * onscreen_frac[r_hit, None]
    hit_rids = ray_ids[hit_idx]
    for reg in sorted(set(hit_regs.tolist())):
        rm = hit_regs == reg
        reg_lum = float(hit_w_lum[rm].sum())
        reg_rgb = hit_w_rgb[rm].sum(axis=0)
        reg_rids = np.unique(hit_rids[rm])
        reg_guided_share = float(np.mean((flags[hit_idx[rm]] & FLAG_GUIDED) != 0))
        reg_g_at_hit = guide_at_hit[hit_idx[rm]]
        reg_breakdown[reg] = dict(
            receiver_records=int(rm.sum()),
            unique_rays=int(len(reg_rids)),
            weighted_lum_share_of_subset=float(reg_lum / max(lum_sum, 1e-20)),
            weighted_rgb=reg_rgb.tolist(),
            guided_draw_frac=reg_guided_share,
            hit_has_pyramid_light_frac=float(np.mean(reg_g_at_hit > 0.0)),
            hit_pyramid_light_median=float(np.median(reg_g_at_hit)),
        )

    return dict(
        name=name,
        receiver_records=n_rec,
        unique_rays=n_uniq,
        receivers_per_unique_ray=float(n_rec / max(n_uniq, 1)),
        unique_own_rays=int(u_own.sum()),
        unique_block_rays=int((~u_own).sum()),
        total_outgoing_rgb=rgb_sum.tolist(),
        total_outgoing_lum=lum_sum,
        onscreen_vs_offscreen=dict(
            onscreen_lum_share=float(onscreen_lum / max(lum_sum, 1e-20)),
            offscreen_env_lum_share=float(offscreen_lum / max(lum_sum, 1e-20)),
            onscreen_rgb=onscreen_rgb.tolist(),
            offscreen_env_rgb=offscreen_rgb.tolist(),
            unique_rays_walk_hit=int(u_hit.sum()),
            unique_rays_walk_miss=int((~u_hit).sum()),
            unique_rays_partial_edge_fade=int((u_hit & (fade[u_idx] < 0.999)).sum()),
        ),
        guided_vs_ordinary=dict(
            guided_lum_share=float(guided_lum / max(lum_sum, 1e-20)),
            ordinary_lum_share=float(ordinary_lum / max(lum_sum, 1e-20)),
            guided_rgb=guided_rgb.tolist(),
            ordinary_rgb=ordinary_rgb.tolist(),
            unique_rays_guided=int(u_guided.sum()),
            unique_rays_guided_counted=int(u_counted.sum()),
            unique_rays_ordinary=int((~u_guided).sum()),
            unique_rays_ordinary_hit=int(((~u_guided) & u_hit).sum()),
            unique_rays_ordinary_miss=int(((~u_guided) & (~u_hit)).sum()),
        ),
        brightness_vs_brdf_weight=dict(
            incoming_lum_p50_p90_p99=[float(np.quantile(inc_lum[idx], q)) for q in (0.5, 0.9, 0.99)],
            brdf_weight_capped_p50_p90_p99=[float(np.quantile(w_cap[idx], q)) for q in (0.5, 0.9, 0.99)],
            brdf_weight_uncapped_p50_p90_p99=[float(np.quantile(w_uncap[idx], q)) for q in (0.5, 0.9, 0.99)],
            hit_most_ray_weight_cap4_frac=float(np.mean(w_uncap[idx] > 4.0)),
            lobe_density_p50_p90=[float(np.quantile(lobe_d[idx], q)) for q in (0.5, 0.9)],
            guided_density_p50_p90=[float(np.quantile(guid_d[idx], q)) for q in (0.5, 0.9)],
            guided_density_gt_zero_frac=float(np.mean(guid_d[idx] > 0.0)),
        ),
        onscreen_regions=reg_breakdown,
    )


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('capture_root', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    sdir = args.capture_root / 'sender-baseline' / 'sender'
    ren_w, ren_h, blk_w, blk_h = map(int, (sdir / 'size.txt').read_text().split())
    stems = sorted({f.name.split('.')[0] for f in sdir.glob('*.block0.f32')})

    # Verify byte identity of sender-baseline PNGs vs moving-normal
    n_pngs = sorted((args.capture_root / 'moving-normal' / 'frames').glob('cockpit-*.png'))
    s_pngs = sorted((args.capture_root / 'sender-baseline' / 'frames').glob('cockpit-*.png'))
    byte_identical = len(n_pngs) == len(s_pngs) and all(
        hashlib.sha256(a.read_bytes()).digest() == hashlib.sha256(b.read_bytes()).digest()
        for a, b in zip(n_pngs, s_pngs)
    )

    # Load static/mid-sweep G-buffer (sd=80, midpoint of 48..108) for region classification & pyramid audit
    ref_stem = '000080' if (sdir / '000080.emissive.f32').exists() else stems[0]
    em_ref = np.fromfile(sdir / f'{ref_stem}.emissive.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[..., :3]
    dd_ref = np.fromfile(sdir / f'{ref_stem}.directDiffuse.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[..., :3]
    lf_ref = np.fromfile(sdir / f'{ref_stem}.lightFinest.f32', dtype='<f4').reshape(ren_h, ren_w)
    hl_ref = np.fromfile(sdir / f'{ref_stem}.hitLightDiffuse.f32', dtype='<f4').reshape(ren_h, ren_w, 4)[..., :3]
    dist_ref = np.fromfile(sdir / f'{ref_stem}.distance.f32', dtype='<f4').reshape(ren_h, ren_w)

    em_lum_ref = em_ref @ LUMA
    dd_lum_ref = dd_ref @ LUMA
    hl_lum_ref = hl_ref @ LUMA

    # Audit light pyramid distribution across entire screen at ref_stem
    yy_all, xx_all = np.mgrid[:ren_h, :ren_w]
    surf_all = dist_ref > 0.0
    all_regs = np.full((ren_h, ren_w), 'sky', dtype=object)
    sy, sx = np.where(surf_all)
    all_regs[sy, sx] = classify_onscreen_region(
        sx, sy, ren_w, ren_h, em_lum_ref[sy, sx], dd_lum_ref[sy, sx], em_ref[sy, sx]
    )
    guide_total_ref = float(lf_ref.sum())
    hl_total_ref = float(hl_lum_ref[surf_all].sum())
    pyramid_screen_audit = dict(
        ref_draw=int(ref_stem),
        guide_total=guide_total_ref,
        hit_light_diffuse_total_lum=hl_total_ref,
        regions={},
    )
    for reg in sorted(set(all_regs[surf_all].tolist())):
        m = all_regs == reg
        g_sum = float(lf_ref[m].sum())
        hl_sum = float(hl_lum_ref[m].sum())
        em_sum = float(em_lum_ref[m].sum())
        dd_sum = float(dd_lum_ref[m].sum())
        pyramid_screen_audit['regions'][reg] = dict(
            pixels=int(m.sum()),
            guide_light_sum=g_sum,
            guide_light_share=float(g_sum / max(guide_total_ref, 1e-20)),
            hit_light_diffuse_share=float(hl_sum / max(hl_total_ref, 1e-20)),
            emissive_lum_sum=em_sum,
            direct_diffuse_lum_sum=dd_sum,
            guide_over_hit_light_ratio=float(g_sum / max(hl_sum, 1e-20)),
        )

    # Panel crop in render coordinates: sloped-panel [860, 40, 1080, 155] + panel-cool [855, 225, 955, 315] + broad panel [740, 35, 1080, 315]
    # Use the union of sloped-panel + panel-warm + panel-cool where handOver > 0.5!
    # Output boxes -> render boxes
    def to_ren(box):
        return (
            int(round(box[0] * ren_w / 1280.0)),
            int(round(box[1] * ren_h / 720.0)),
            int(round(box[2] * ren_w / 1280.0)),
            int(round(box[3] * ren_h / 720.0)),
        )

    panel_boxes = {
        'sloped-panel': to_ren((860, 40, 1080, 155)),
        'panel-cool': to_ren((855, 225, 955, 315)),
        'panel-warm': to_ren((740, 35, 855, 130)),
    }

    report = dict(
        sender_baseline_byte_identical_to_moving_normal=byte_identical,
        render_size=[ren_w, ren_h],
        block_size=[blk_w, blk_h],
        sampled_draws=[int(s) for s in stems],
        light_pyramid_screen_audit=pyramid_screen_audit,
        crops={},
    )

    # Accumulate hit-pixel and guided-target heatmaps on the screen for visualization
    tail_hit_heat = np.zeros((ren_h, ren_w), dtype=np.float64)
    guided_target_heat = np.zeros((ren_h, ren_w), dtype=np.float64)

    for cname, (rx0, ry0, rx1, ry1) in panel_boxes.items():
        all_out_rgb = []
        all_out_lum = []
        all_ray_ids = []
        all_flags = []
        all_fade = []
        all_ml = []
        all_ll = []
        all_inc = []
        all_wcap = []
        all_wuncap = []
        all_ld = []
        all_gd = []
        all_regs_hit = []
        all_guide_hit = []
        all_target_regs = []
        all_target_guided = []
        all_target_counted = []
        all_target_traced = []

        for di, s in enumerate(stems):
            b0 = np.fromfile(sdir / f'{s}.block0.f32', dtype='<f4').reshape(blk_h, blk_w, 4)
            b1 = np.fromfile(sdir / f'{s}.block1.f32', dtype='<f4').reshape(blk_h, blk_w, 4)
            b2 = np.fromfile(sdir / f'{s}.block2.f32', dtype='<f4').reshape(blk_h, blk_w, 4)
            p0 = np.fromfile(sdir / f'{s}.pixel0.f32', dtype='<f4').reshape(ren_h, ren_w, 4)
            p1 = np.fromfile(sdir / f'{s}.pixel1.f32', dtype='<f4').reshape(ren_h, ren_w, 4)
            p2 = np.fromfile(sdir / f'{s}.pixel2.f32', dtype='<f4').reshape(ren_h, ren_w, 4)
            p3 = np.fromfile(sdir / f'{s}.pixel3.f32', dtype='<f4').reshape(ren_h, ren_w, 4)
            p4 = np.fromfile(sdir / f'{s}.pixel4.f32', dtype='<f4').reshape(ren_h, ren_w, 4)
            lf = np.fromfile(sdir / f'{s}.lightFinest.f32', dtype='<f4').reshape(ren_h, ren_w)

            py, px = np.mgrid[ry0:ry1, rx0:rx1]
            ho = p3[ry0:ry1, rx0:rx1, 3]
            valid = ho > 0.0
            py_v = py[valid]
            px_v = px[valid]
            is_own = p4[py_v, px_v, 3] > 0.5
            bx_v = px_v // 2
            by_v = py_v // 2

            meta = np.where(is_own[:, None], p0[py_v, px_v], b0[by_v, bx_v])
            f_and_d = np.where(is_own[:, None], p1[py_v, px_v], b1[by_v, bx_v])
            lsplit = np.where(is_own[:, None], p2[py_v, px_v], b2[by_v, bx_v])

            hx = np.rint(meta[:, 0]).astype(np.int32)
            hy = np.rint(meta[:, 1]).astype(np.int32)
            tx = np.rint(meta[:, 2]).astype(np.int32)
            ty = np.rint(meta[:, 3]).astype(np.int32)

            fl = np.rint(f_and_d[:, 0]).astype(np.uint32)
            fd = f_and_d[:, 1].astype(np.float64)
            ld = f_and_d[:, 2].astype(np.float64)
            gd = f_and_d[:, 3].astype(np.float64)
            ml = lsplit[:, 0].astype(np.float64)
            ll = lsplit[:, 1].astype(np.float64)

            orgb = p3[py_v, px_v, :3].astype(np.float64)
            olum = orgb @ LUMA
            inc = p4[py_v, px_v, 0].astype(np.float64)
            wc = p4[py_v, px_v, 1].astype(np.float64)
            wu = p4[py_v, px_v, 2].astype(np.float64)

            # Encode unique ray id as int64: draw_idx << 26 | is_own << 25 | ry << 12 | rx
            rx_id = np.where(is_own, px_v, bx_v).astype(np.int64)
            ry_id = np.where(is_own, py_v, by_v).astype(np.int64)
            rids = (np.int64(di) << 26) | (is_own.astype(np.int64) << 25) | (ry_id << 12) | rx_id

            # Classify hit pixels and guided targets using all_regs
            in_b = (hx >= 0) & (hx < ren_w) & (hy >= 0) & (hy < ren_h)
            h_reg = np.full(len(hx), 'offscreen-miss', dtype=object)
            h_guide = np.zeros(len(hx), dtype=np.float64)
            if np.any(in_b):
                h_reg[in_b] = all_regs[hy[in_b], hx[in_b]]
                h_guide[in_b] = lf[hy[in_b], hx[in_b]]

            t_in = (tx >= 0) & (tx < ren_w) & (ty >= 0) & (ty < ren_h)
            t_reg = np.full(len(tx), 'none', dtype=object)
            if np.any(t_in):
                t_reg[t_in] = all_regs[ty[t_in], tx[t_in]]
                if cname == 'sloped-panel':
                    # Accumulate unique guided targets once per unique ray
                    _, upos = np.unique(rids, return_index=True)
                    u_tin = t_in[upos]
                    np.add.at(guided_target_heat, (ty[upos[u_tin]], tx[upos[u_tin]]), 1.0)

            if cname in ('sloped-panel', 'panel-cool') and np.any(in_b):
                # Weight heatmap by outgoing luminance for upper tail later; here accumulate all hits weighted by olum
                np.add.at(tail_hit_heat, (hy[in_b], hx[in_b]), olum[in_b])

            all_out_rgb.append(orgb)
            all_out_lum.append(olum)
            all_ray_ids.append(rids)
            all_flags.append(fl)
            all_fade.append(fd)
            all_ml.append(ml)
            all_ll.append(ll)
            all_inc.append(inc)
            all_wcap.append(wc)
            all_wuncap.append(wu)
            all_ld.append(ld)
            all_gd.append(gd)
            all_regs_hit.append(h_reg)
            all_guide_hit.append(h_guide)
            all_target_regs.append(t_reg)
            all_target_guided.append((fl & FLAG_GUIDED) != 0)
            all_target_counted.append((fl & FLAG_GUIDED_COUNTED) != 0)
            all_target_traced.append((fl & FLAG_TRACED) != 0)

        out_rgb = np.concatenate(all_out_rgb)
        out_lum = np.concatenate(all_out_lum)
        ray_ids = np.concatenate(all_ray_ids)
        flags = np.concatenate(all_flags)
        fade = np.concatenate(all_fade)
        ml = np.concatenate(all_ml)
        ll = np.concatenate(all_ll)
        inc = np.concatenate(all_inc)
        wc = np.concatenate(all_wcap)
        wu = np.concatenate(all_wuncap)
        ld = np.concatenate(all_ld)
        gd = np.concatenate(all_gd)
        regs = np.concatenate(all_regs_hit)
        ghit = np.concatenate(all_guide_hit)
        tregs = np.concatenate(all_target_regs)
        tguided = np.concatenate(all_target_guided)
        tcounted = np.concatenate(all_target_counted)
        ttraced = np.concatenate(all_target_traced)

        q95 = float(np.quantile(out_lum, 0.95))
        q99 = float(np.quantile(out_lum, 0.99))
        q995 = float(np.quantile(out_lum, 0.995))
        total_lum = float(out_lum.sum())

        # Audit guided target outcomes on unique guided rays for this crop
        _, u_pos = np.unique(ray_ids, return_index=True)
        ug_mask = tguided[u_pos]
        ug_pos = u_pos[ug_mask]
        guided_target_audit = dict(
            unique_guided_rays=int(len(ug_pos)),
            traced_above_plane_frac=float(np.mean(ttraced[ug_pos])) if len(ug_pos) else 0.0,
            walk_hit_frac=float(np.mean((flags[ug_pos] & FLAG_WALK_HIT) != 0)) if len(ug_pos) else 0.0,
            counted_in_3x3_frac=float(np.mean(tcounted[ug_pos])) if len(ug_pos) else 0.0,
            by_target_region={},
        )
        for tr in sorted(set(tregs[ug_pos].tolist())):
            tm = tregs[ug_pos] == tr
            sub = ug_pos[tm]
            guided_target_audit['by_target_region'][tr] = dict(
                unique_guided_rays=int(len(sub)),
                share_of_guided_rays=float(len(sub) / max(len(ug_pos), 1)),
                above_plane_frac=float(np.mean(ttraced[sub])),
                counted_frac=float(np.mean(tcounted[sub])),
            )

        crop_rep = dict(
            render_box=[rx0, ry0, rx1, ry1],
            quantiles_outgoing_lum=dict(p50=float(np.median(out_lum)), p95=q95, p99=q99, p995=q995, max=float(out_lum.max())),
            tail_lum_shares=dict(
                top_5pct=float(out_lum[out_lum >= q95].sum() / max(total_lum, 1e-20)),
                top_1pct=float(out_lum[out_lum >= q99].sum() / max(total_lum, 1e-20)),
                top_05pct=float(out_lum[out_lum >= q995].sum() / max(total_lum, 1e-20)),
            ),
            all_records=summarize_subset(
                'all', np.ones(len(out_lum), dtype=bool), out_rgb, out_lum, ray_ids, flags, fade, ml, ll, inc, wc, wu, ld, gd, regs, ghit
            ),
            top_5pct=summarize_subset(
                'top_5pct', out_lum >= q95, out_rgb, out_lum, ray_ids, flags, fade, ml, ll, inc, wc, wu, ld, gd, regs, ghit
            ),
            top_1pct=summarize_subset(
                'top_1pct', out_lum >= q99, out_rgb, out_lum, ray_ids, flags, fade, ml, ll, inc, wc, wu, ld, gd, regs, ghit
            ),
            guided_target_audit=guided_target_audit,
        )
        report['crops'][cname] = crop_rep

    # Save visual diagnostic map: [cockpit frame | lightFinest pyramid map | panel specular sender heatmap | guided target map]
    bg = np.asarray(Image.open(args.capture_root / 'moving-normal' / 'frames' / 'cockpit-000048.png').convert('RGB'))
    bg_r = np.asarray(Image.fromarray(bg).resize((ren_w, ren_h), Image.Resampling.BILINEAR), dtype=np.float64)

    def overlay_heat(base_rgb, heat, tint):
        h_log = np.log1p(heat / max(float(np.quantile(heat[heat > 0], 0.90)) if np.any(heat > 0) else 1.0, 1e-12) * 10.0)
        h_norm = np.clip(h_log / max(float(h_log.max()), 1e-12), 0.0, 1.0)
        out = base_rgb * 0.45 + h_norm[..., None] * np.array(tint, dtype=np.float64) * 255.0
        return np.uint8(np.clip(out, 0, 255))

    t1 = np.uint8(bg_r)
    t2 = overlay_heat(bg_r, lf_ref, [1.0, 0.85, 0.2])
    t3 = overlay_heat(bg_r, tail_hit_heat, [0.2, 0.95, 1.0])
    t4 = overlay_heat(bg_r, guided_target_heat, [1.0, 0.35, 0.2])

    w2, h2 = 640, 360
    canvas = Image.new('RGB', (w2 * 2, (h2 + 24) * 2), '#17191c')
    dr = ImageDraw.Draw(canvas)
    tiles = [
        ('1:41 cockpit (f=48)', t1),
        ('Light pyramid finest (guideLight)', t2),
        ('Panel rough-spec sender hits (lum weighted)', t3),
        ('Guided diffuse ray targets (sloped-panel)', t4),
    ]
    for k, (lbl, arr) in enumerate(tiles):
        c, r = k % 2, k // 2
        ox, oy = c * w2, r * (h2 + 24)
        canvas.paste(Image.fromarray(arr).resize((w2, h2), Image.Resampling.BILINEAR), (ox, oy + 22))
        dr.text((ox + 6, oy + 4), lbl, fill='white')
    canvas.save(args.output / 'sender-attribution-map.png')

    (args.output / 'sender-results.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Wrote', args.output / 'sender-results.json')


if __name__ == '__main__':
    main()
