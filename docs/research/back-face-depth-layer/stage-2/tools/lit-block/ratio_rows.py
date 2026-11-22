"""Tool code (t1-r9vj): the arithmetic of the ratio tables, apart from the images. Tests: test_ratio_rows.py.

A region's mean is its mean scene-linear luminance in one image. Its ratio is that mean over the neutral region's mean in
the same image.
"""


def ratios(means, neutral):
    """The ratio of every region but the neutral one."""
    return {r: m / means[neutral] for r, m in means.items() if r != neutral}


def frame_noise(early, late, neutral):
    """The absolute change of each ratio between two settled frames of one film."""
    a, b = ratios(early, neutral), ratios(late, neutral)
    return {r: abs(b[r] - a[r]) for r in a}


def nearest(rows, reference):
    """For each region, the column whose ratio is nearest to the reference's, and the next nearest. rows maps a column to
    its ratios. A tie keeps the order of the columns in rows."""
    order = list(rows)
    result = {}
    for r, truth in reference.items():
        ranked = sorted(order, key=lambda c: (abs(rows[c][r] - truth), order.index(c)))
        result[r] = (ranked[0], ranked[1])
    return result
