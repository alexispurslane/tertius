"""Actual cockpit-panel physical sheet geometry, area-aware no-cutoff heat flow,
physical-point accumulation across moving views, and multi-phase batch convergence.

Uses the cooked lunarshuttle.model/model.glb triangles (1343, 2548, 641) of the
starboard sloped cockpit panel in cabin coordinates (ArtScale=1.6, ArtPlace=(0,0,1)).
"""
from collections import deque
from dataclasses import dataclass
import json
from pathlib import Path
import struct
import numpy as np
from scipy.ndimage import gaussian_filter
from scipy.sparse import csr_matrix
from scipy.sparse.linalg import expm_multiply

DEFAULT_SHUTTLE_GLB = Path(
    '/Users/alexispurslane/Development/tertius/content/tertius.demo/bin/assets/'
    'blue-danube/lunar-shuttle/lunarshuttle.model/model.glb'
)
COCKPIT_PANEL_TRIS = (1343, 2548, 641)
ART_SCALE = 1.6
ART_PLACE = np.array([0.0, 0.0, 1.0], dtype=np.float64)


@dataclass(frozen=True)
class SheetMesh:
    verts3d: np.ndarray      # (V, 3) float64 in cabin space (metres)
    tris: np.ndarray         # (T, 3) int64 vertex indices
    tris2d: np.ndarray       # (T, 3, 2) float64 isometric 2D sheet coords (metres)
    tri_normals: np.ndarray  # (T, 3) float64 unit outward normals
    tri_areas: np.ndarray    # (T,) float64 areas in m^2

    @classmethod
    def from_triangles(cls, verts3d, tris, root_tri=0):
        verts3d = np.asarray(verts3d, dtype=np.float64)
        tris = np.asarray(tris, dtype=np.int64)
        n_tris = len(tris)
        normals = np.zeros((n_tris, 3), dtype=np.float64)
        areas = np.zeros(n_tris, dtype=np.float64)
        for ti, (a, b, c) in enumerate(tris):
            cr = np.cross(verts3d[b] - verts3d[a], verts3d[c] - verts3d[a])
            ln = np.linalg.norm(cr)
            areas[ti] = 0.5 * ln
            normals[ti] = cr / max(ln, 1e-15)

        # Build shared-edge adjacency (by vertex index)
        edge_map = {}
        adj = [[] for _ in range(n_tris)]
        for ti, tri in enumerate(tris):
            for k in range(3):
                u, v = int(tri[k]), int(tri[(k + 1) % 3])
                key = (u, v) if u < v else (v, u)
                if key in edge_map:
                    other = edge_map[key]
                    adj[ti].append((other, u, v))
                    adj[other].append((ti, u, v))
                else:
                    edge_map[key] = ti

        # Isometric BFS unfolding starting from root_tri
        tris2d = np.zeros((n_tris, 3, 2), dtype=np.float64)
        r0, r1, r2 = tris[root_tri]
        p0, p1, p2 = verts3d[r0], verts3d[r1], verts3d[r2]
        ex = p1 - p0
        ex = ex / np.linalg.norm(ex)
        ey = np.cross(normals[root_tri], ex)
        ey = ey / np.linalg.norm(ey)
        for k, vi in enumerate(tris[root_tri]):
            rel = verts3d[vi] - p0
            tris2d[root_tri, k] = [np.dot(rel, ex), np.dot(rel, ey)]

        visited = {root_tri}
        queue = deque([root_tri])
        while queue:
            cur = queue.popleft()
            cur_tri = list(tris[cur])
            for nb, u, v in adj[cur]:
                if nb in visited:
                    continue
                nb_tri = list(tris[nb])
                # Map shared vertices u, v from cur's 2D positions
                u2 = tris2d[cur, cur_tri.index(u)]
                v2 = tris2d[cur, cur_tri.index(v)]
                # Third vertex w in nb
                w = [x for x in nb_tri if x != u and x != v][0]
                # Exact isometric coordinates of w relative to edge u -> v
                e3 = verts3d[v] - verts3d[u]
                el = np.linalg.norm(e3)
                e3_hat = e3 / el
                e2_hat = (v2 - u2) / el
                rel3 = verts3d[w] - verts3d[u]
                along = np.dot(rel3, e3_hat)
                perp3 = rel3 - along * e3_hat
                dist = np.linalg.norm(perp3)
                # Place w on the opposite side of edge (u, v) from cur's third vertex
                cur_w = [x for x in cur_tri if x != u and x != v][0]
                cur_w2 = tris2d[cur, cur_tri.index(cur_w)]
                n2 = np.array([-e2_hat[1], e2_hat[0]], dtype=np.float64)
                if np.dot(cur_w2 - u2, n2) > 0:
                    n2 = -n2
                w2 = u2 + along * e2_hat + dist * n2
                pos_map = {u: u2, v: v2, w: w2}
                for k, vi in enumerate(nb_tri):
                    tris2d[nb, k] = pos_map[vi]
                visited.add(nb)
                queue.append(nb)

        return cls(verts3d=verts3d, tris=tris, tris2d=tris2d, tri_normals=normals, tri_areas=areas)


@dataclass(frozen=True)
class PhysicalGrid:
    origin_u: float
    origin_v: float
    spacing: float
    active: np.ndarray     # (H, W) bool
    areas: np.ndarray      # (H, W) float64 physical cell areas in m^2
    tri_id: np.ndarray     # (H, W) int32 dominant triangle index (-1 if inactive)


def _clip_polygon_halfplane(poly, a, b, c):
    """Keep points with a*x + b*y + c >= 0."""
    if not poly:
        return []
    out = []
    n = len(poly)
    for i in range(n):
        p1 = poly[i]
        p2 = poly[(i + 1) % n]
        d1 = a * p1[0] + b * p1[1] + c
        d2 = a * p2[0] + b * p2[1] + c
        if d1 >= 0.0:
            out.append(p1)
        if (d1 >= 0.0) != (d2 >= 0.0):
            t = d1 / (d1 - d2)
            out.append((p1[0] + t * (p2[0] - p1[0]), p1[1] + t * (p2[1] - p1[1])))
    return out


def _poly_area(poly):
    if len(poly) < 3:
        return 0.0
    area = 0.0
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        area += x1 * y2 - x2 * y1
    return 0.5 * abs(area)


def _tri_rect_overlap_area(tri2d, u0, u1, v0, v1):
    poly = [(float(p[0]), float(p[1])) for p in tri2d]
    # Ensure CCW winding
    if (poly[1][0] - poly[0][0]) * (poly[2][1] - poly[0][1]) - (poly[1][1] - poly[0][1]) * (poly[2][0] - poly[0][0]) < 0:
        poly.reverse()
    poly = _clip_polygon_halfplane(poly, 1.0, 0.0, -u0)
    poly = _clip_polygon_halfplane(poly, -1.0, 0.0, u1)
    poly = _clip_polygon_halfplane(poly, 0.0, 1.0, -v0)
    poly = _clip_polygon_halfplane(poly, 0.0, -1.0, v1)
    return _poly_area(poly)


def build_physical_grid(sheet: SheetMesh, spacing: float, min_area_fraction: float = 1e-4) -> PhysicalGrid:
    """Discretize the connected 2D unfolded sheet onto a physical grid with exact clipped cell areas."""
    all2 = sheet.tris2d.reshape(-1, 2)
    u_min, v_min = all2.min(axis=0)
    u_max, v_max = all2.max(axis=0)
    width = int(np.ceil((u_max - u_min) / spacing))
    height = int(np.ceil((v_max - v_min) / spacing))
    areas = np.zeros((height, width), dtype=np.float64)
    tri_id = np.full((height, width), -1, dtype=np.int32)
    best_area = np.zeros((height, width), dtype=np.float64)

    for ti, tri2d in enumerate(sheet.tris2d):
        c0 = max(0, int(np.floor((tri2d[:, 0].min() - u_min) / spacing)))
        c1 = min(width, int(np.ceil((tri2d[:, 0].max() - u_min) / spacing)))
        r0 = max(0, int(np.floor((tri2d[:, 1].min() - v_min) / spacing)))
        r1 = min(height, int(np.ceil((tri2d[:, 1].max() - v_min) / spacing)))
        for r in range(r0, r1):
            va = v_min + r * spacing
            vb = va + spacing
            for c in range(c0, c1):
                ua = u_min + c * spacing
                ub = ua + spacing
                ov = _tri_rect_overlap_area(tri2d, ua, ub, va, vb)
                if ov > 0.0:
                    areas[r, c] += ov
                    if ov > best_area[r, c]:
                        best_area[r, c] = ov
                        tri_id[r, c] = ti

    cell_area = spacing * spacing
    active = areas >= min_area_fraction * cell_area
    areas = np.where(active, areas, 0.0)
    tri_id = np.where(active, tri_id, -1)
    return PhysicalGrid(
        origin_u=float(u_min),
        origin_v=float(v_min),
        spacing=float(spacing),
        active=active,
        areas=areas,
        tri_id=tri_id,
    )


def build_heat_operator(active: np.ndarray, areas: np.ndarray, spacing: float) -> csr_matrix:
    """Sparse area-aware finite-volume Laplacian A = M^{-1} L on active cells.

    Insulating boundaries everywhere active meets inactive. Conserves sum_i areas_i * u_i.
    """
    active = np.asarray(active, dtype=bool)
    areas = np.asarray(areas, dtype=np.float64)
    h, w = active.shape
    idx_map = np.full((h, w), -1, dtype=np.int64)
    ys, xs = np.where(active)
    n = len(ys)
    idx_map[ys, xs] = np.arange(n, dtype=np.int64)
    active_areas = areas[ys, xs]

    rows = []
    cols = []
    vals = []
    diag = np.zeros(n, dtype=np.float64)

    # Conductance across a shared grid edge of length spacing and center distance spacing is 1.0
    for dy, dx in ((0, 1), (1, 0)):
        y0 = slice(0, h - dy)
        x0 = slice(0, w - dx)
        y1 = slice(dy, h)
        x1 = slice(dx, w)
        both = active[y0, x0] & active[y1, x1]
        by, bx = np.where(both)
        i = idx_map[by, bx]
        j = idx_map[by + dy, bx + dx]
        # Symmetric conductance g = 1.0 -> (M^{-1} L)_{ij} = g / A_i
        rows.append(i)
        cols.append(j)
        vals.append(1.0 / active_areas[i])
        rows.append(j)
        cols.append(i)
        vals.append(1.0 / active_areas[j])
        np.add.at(diag, i, -1.0 / active_areas[i])
        np.add.at(diag, j, -1.0 / active_areas[j])

    rows.append(np.arange(n, dtype=np.int64))
    cols.append(np.arange(n, dtype=np.int64))
    vals.append(diag)
    return csr_matrix(
        (np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))),
        shape=(n, n),
        dtype=np.float64,
    )


def area_aware_heat(
    signal: np.ndarray,
    active: np.ndarray,
    areas: np.ndarray,
    spacing: float,
    diffusion_amount: float,
    operator: csr_matrix | None = None,
) -> np.ndarray:
    """Exact matrix-exponential area-aware heat flow u(tau) = exp(tau * M^{-1} L) u(0).

    diffusion_amount is tau in physical area units (m^2). In the interior of a uniform grid,
    a unit impulse has RMS radial width sqrt(4 * diffusion_amount) in metres.
    """
    signal = np.asarray(signal, dtype=np.float64)
    active = np.asarray(active, dtype=bool)
    out = np.zeros_like(signal, dtype=np.float64)
    if not np.any(active):
        return out
    if diffusion_amount <= 0.0:
        out[active] = signal[active]
        return out
    op = build_heat_operator(active, areas, spacing) if operator is None else operator
    vec = signal[active]
    moved = expm_multiply(diffusion_amount * op, vec)
    out[active] = moved
    return out


def unproject_to_sheet(
    rgb: np.ndarray,
    distance: np.ndarray,
    camera: dict,
    sheet: SheetMesh,
    grid: PhysicalGrid,
    max_plane_dist: float = 0.01,
):
    """Map render-resolution isolated source pixels onto exact physical sheet cells.

    camera dict holds eye, forward, right_tan, up_tan, jitter (render px), width, height.
    Returns (cell_rgb, cell_observed, cell_pix_area, pixel_mask, pixel_cell_rc, pixel_area_m2).
    """
    h, w = distance.shape
    yy, xx = np.mgrid[:h, :w]
    jx, jy = camera['jitter']
    ndc_x = (xx + 0.5 + jx) / float(w) * 2.0 - 1.0
    ndc_y = (yy + 0.5 + jy) / float(h) * 2.0 - 1.0
    fwd = np.asarray(camera['forward'], dtype=np.float64)
    right_tan = np.asarray(camera['right_tan'], dtype=np.float64)
    up_tan = np.asarray(camera['up_tan'], dtype=np.float64)
    eye = np.asarray(camera['eye'], dtype=np.float64)

    # Exact screen.glsl viewRayThrough: forward + ndc.x * cameraRight - ndc.y * cameraUp
    ray = fwd[None, None, :] + ndc_x[..., None] * right_tan[None, None, :] - ndc_y[..., None] * up_tan[None, None, :]
    ray_len = np.linalg.norm(ray, axis=-1)
    dir_unit = ray / np.maximum(ray_len[..., None], 1e-12)
    valid_d = distance > 0.0
    pts = eye[None, None, :] + dir_unit * distance[..., None]

    # Exact view depth along forward axis and per-pixel solid-angle plane area factor
    view_depth = distance / np.maximum(ray_len, 1e-12)
    tan_x = np.linalg.norm(right_tan)
    tan_y = np.linalg.norm(up_tan)
    dpx = (2.0 * tan_x / float(w)) * view_depth
    dpy = (2.0 * tan_y / float(h)) * view_depth

    pixel_mask = np.zeros((h, w), dtype=bool)
    pixel_uv = np.zeros((h, w, 2), dtype=np.float64)
    pixel_area = np.zeros((h, w), dtype=np.float64)
    best_dist = np.full((h, w), max_plane_dist, dtype=np.float64)

    for ti in range(len(sheet.tris)):
        a, b, c = sheet.tris[ti]
        v0, v1, v2 = sheet.verts3d[a], sheet.verts3d[b], sheet.verts3d[c]
        n = sheet.tri_normals[ti]
        rel = pts - v0[None, None, :]
        pdist = np.abs(np.einsum('ijk,k->ij', rel, n))
        cand = valid_d & (pdist < best_dist)
        if not np.any(cand):
            continue
        # Barycentric coordinates in triangle plane
        e1 = v1 - v0
        e2 = v2 - v0
        d11 = np.dot(e1, e1)
        d12 = np.dot(e1, e2)
        d22 = np.dot(e2, e2)
        denom = d11 * d22 - d12 * d12
        r_cand = rel[cand]
        r1 = r_cand @ e1
        r2 = r_cand @ e2
        bv = (d22 * r1 - d12 * r2) / denom
        bw = (d11 * r2 - d12 * r1) / denom
        bu = 1.0 - bv - bw
        inside = (bu >= -1e-3) & (bv >= -1e-3) & (bw >= -1e-3)
        if not np.any(inside):
            continue
        cy, cx = np.where(cand)
        cy, cx = cy[inside], cx[inside]
        bu_i, bv_i, bw_i = bu[inside], bv[inside], bw[inside]
        best_dist[cy, cx] = pdist[cy, cx]
        pixel_mask[cy, cx] = True
        t2 = sheet.tris2d[ti]
        pixel_uv[cy, cx] = bu_i[:, None] * t2[0] + bv_i[:, None] * t2[1] + bw_i[:, None] * t2[2]
        cos_inc = np.abs(np.einsum('ij,j->i', -dir_unit[cy, cx], n))
        pixel_area[cy, cx] = (dpx[cy, cx] * dpy[cy, cx]) / np.maximum(cos_inc, 0.05)

    gh, gw = grid.active.shape
    cell_col = np.floor((pixel_uv[..., 0] - grid.origin_u) / grid.spacing).astype(np.int64)
    cell_row = np.floor((pixel_uv[..., 1] - grid.origin_v) / grid.spacing).astype(np.int64)
    in_grid = (
        pixel_mask
        & (cell_row >= 0) & (cell_row < gh)
        & (cell_col >= 0) & (cell_col < gw)
    )
    valid_cells = np.zeros((h, w), dtype=bool)
    iy, ix = np.where(in_grid)
    keep = grid.active[cell_row[iy, ix], cell_col[iy, ix]]
    iy, ix = iy[keep], ix[keep]
    valid_cells[iy, ix] = True

    cell_num = np.zeros((gh, gw, 3), dtype=np.float64)
    cell_den = np.zeros((gh, gw), dtype=np.float64)
    if len(iy) > 0:
        rr = cell_row[iy, ix]
        cc = cell_col[iy, ix]
        ww = pixel_area[iy, ix]
        vals = rgb[iy, ix].astype(np.float64) * ww[:, None]
        for ch in range(3):
            np.add.at(cell_num[..., ch], (rr, cc), vals[:, ch])
        np.add.at(cell_den, (rr, cc), ww)

    cell_observed = cell_den > 0.0
    cell_rgb = np.divide(cell_num, cell_den[..., None], out=np.zeros_like(cell_num), where=cell_observed[..., None])
    pixel_cell_rc = np.stack([cell_row, cell_col], axis=-1)
    return cell_rgb, cell_observed, cell_den, valid_cells, pixel_cell_rc, pixel_area


def accumulate_on_sheet(frames: np.ndarray, observed: np.ndarray, window: int) -> np.ndarray:
    """Running average at each physical sheet cell over genuinely fresh observed draws, capped at window."""
    frames = np.asarray(frames, dtype=np.float64)
    observed = np.asarray(observed, dtype=bool)
    n_frames, h, w, c = frames.shape
    out = np.zeros_like(frames, dtype=np.float64)
    running = np.zeros((h, w, c), dtype=np.float64)
    counts = np.zeros((h, w), dtype=np.float64)
    for t in range(n_frames):
        obs = observed[t]
        if np.any(obs):
            counts[obs] = np.minimum(counts[obs] + 1.0, float(window))
            alpha = 1.0 / counts[obs]
            running[obs] = running[obs] + alpha[:, None] * (frames[t, obs] - running[obs])
        out[t] = running
    return out


def batch_convergence(batch_a: np.ndarray, batch_b: np.ndarray, mask: np.ndarray, sigma_cells: float = 4.0) -> dict:
    """Cross-check convergence between two independent multi-phase batches on mask."""
    batch_a = np.asarray(batch_a, dtype=np.float64)
    batch_b = np.asarray(batch_b, dtype=np.float64)
    mask = np.asarray(mask, dtype=bool)
    va = batch_a[mask]
    vb = batch_b[mask]
    mean_a = va.mean(axis=0)
    mean_b = vb.mean(axis=0)
    mean_ref = 0.5 * (mean_a + mean_b)
    rel_mean = float(100.0 * np.linalg.norm(mean_a - mean_b) / max(np.linalg.norm(mean_ref), 1e-20))
    rel_rms = float(100.0 * np.sqrt(np.mean((va - vb) ** 2) / max(np.mean((0.5 * (va + vb)) ** 2), 1e-20)))

    # Masked Gaussian wash comparison (normalized convolution so inactive cells do not bleed zero)
    w_mask = gaussian_filter(mask.astype(np.float64), sigma=sigma_cells, mode='constant')
    safe = mask & (w_mask > 1e-6)
    wa = gaussian_filter(np.where(mask[..., None], batch_a, 0.0), sigma=(sigma_cells, sigma_cells, 0.0), mode='constant')
    wb = gaussian_filter(np.where(mask[..., None], batch_b, 0.0), sigma=(sigma_cells, sigma_cells, 0.0), mode='constant')
    wa = np.divide(wa, w_mask[..., None], out=np.zeros_like(wa), where=safe[..., None])
    wb = np.divide(wb, w_mask[..., None], out=np.zeros_like(wb), where=safe[..., None])
    wash_rms = float(
        100.0 * np.sqrt(np.mean((wa[safe] - wb[safe]) ** 2) / max(np.mean((0.5 * (wa[safe] + wb[safe])) ** 2), 1e-20))
    )
    return dict(
        mean_rgb_a=mean_a.tolist(),
        mean_rgb_b=mean_b.tolist(),
        mean_rgb_relative_diff_percent=rel_mean,
        relative_rms_percent=rel_rms,
        wash_relative_rms_percent=wash_rms,
        cell_count=int(mask.sum()),
    )


def load_cockpit_panel_sheet(glb_path: Path = DEFAULT_SHUTTLE_GLB) -> SheetMesh:
    """Load the actual connected 3-triangle starboard cockpit panel from lunarshuttle.model/model.glb."""
    with open(glb_path, 'rb') as f:
        magic, _ver, _length = struct.unpack('<4sII', f.read(12))
        assert magic == b'glTF'
        clen, _ctype = struct.unpack('<I4s', f.read(8))
        gltf = json.loads(f.read(clen))
        blen, _btype = struct.unpack('<I4s', f.read(8))
        blob = f.read(blen)

    def read_acc(idx):
        acc = gltf['accessors'][idx]
        bv = gltf['bufferViews'][acc['bufferView']]
        off = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
        dt = np.dtype('<' + {5120: 'i1', 5121: 'u1', 5122: 'i2', 5123: 'u2', 5125: 'u4', 5126: 'f4'}[acc['componentType']])
        nc = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[acc['type']]
        st = bv.get('byteStride', dt.itemsize * nc)
        raw = np.frombuffer(blob, dtype='u1', count=acc['count'] * st, offset=off).reshape(acc['count'], st)
        arr = np.frombuffer(np.ascontiguousarray(raw[:, :dt.itemsize * nc]), dtype=dt)
        return arr.reshape(acc['count'], nc) if nc > 1 else arr

    prim = gltf['meshes'][3]['primitives'][0]
    pos = read_acc(prim['attributes']['POSITION']).astype(np.float64) * ART_SCALE + ART_PLACE
    ind = read_acc(prim['indices']).reshape(-1, 3)

    # Weld vertices of the 3 selected triangles by exact rounded 3D position
    selected = ind[list(COCKPIT_PANEL_TRIS)]
    verts = []
    vmap = {}
    remapped = np.zeros_like(selected, dtype=np.int64)
    for ti in range(len(selected)):
        for k in range(3):
            pt = pos[selected[ti, k]]
            key = tuple(np.round(pt, 4))
            if key not in vmap:
                vmap[key] = len(verts)
                verts.append(pt)
            remapped[ti, k] = vmap[key]
    return SheetMesh.from_triangles(np.asarray(verts, dtype=np.float64), remapped, root_tri=0)
