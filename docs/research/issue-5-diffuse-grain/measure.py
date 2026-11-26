"""Measurements of display-linear luminance, before any crop enlargement."""
import numpy as np
from scipy.ndimage import gaussian_filter


def grain(frames):
    """Absolute luminance RMS above a 1.5-pixel Gaussian, spatially and between frames."""
    high = frames - gaussian_filter(frames, sigma=(0, 1.5, 1.5), mode='reflect')
    return dict(
        mean_luminance=float(frames.mean()),
        spatial_rms=float(np.sqrt(np.mean(high ** 2))),
        temporal_rms=float(np.sqrt(np.mean(np.diff(high, axis=0) ** 2))),
    )


def compare(baseline, variant):
    """Report the paired change without normalizing away the variant's light loss."""
    return dict(
        mean_change_percent=float(100 * (variant.mean() / baseline.mean() - 1)),
        paired_rms=float(np.sqrt(np.mean((variant - baseline) ** 2))),
    )


def color_wash(baseline, variant):
    """Four local patch means and error of the time mean blurred by eight output pixels."""
    reference = baseline.mean(axis=0)
    changed = variant.mean(axis=0)
    reference_wash = gaussian_filter(reference, sigma=(8, 8, 0), mode='reflect')
    changed_wash = gaussian_filter(changed, sigma=(8, 8, 0), mode='reflect')
    height, width = reference.shape[:2]
    result = {}
    for row in range(2):
        for column in range(2):
            patch = np.s_[row * height // 2:(row + 1) * height // 2,
                          column * width // 2:(column + 1) * width // 2]
            before = reference_wash[patch]
            after = changed_wash[patch]
            result[f'row{row}-column{column}'] = dict(
                baseline_mean_rgb=reference[patch].mean(axis=(0, 1)).tolist(),
                variant_mean_rgb=changed[patch].mean(axis=(0, 1)).tolist(),
                wash_relative_rms_percent=float(100 * np.sqrt(np.mean((after - before) ** 2)
                    / max(float(np.mean(before ** 2)), 1e-20))),
            )
    return result
