# Optical references for bloom

Research date: 2026-11-27. Scope: [issue #3](https://github.com/alexispurslane/tertius/issues/3), under [epic #1](https://github.com/alexispurslane/tertius/issues/1). The [Blue Danube baseline](https://github.com/alexispurslane/tertius/wiki/blue-danube-rendering-baseline#second-cut-priorities) supplies the scene controls.

## Conclusion and settled behavior

Spencer et al. provide an absolute, angular reference for human glare. Their display algorithm also adds glare from light above the maximum displayable intensity. That supports the selected threshold and additive mechanism. It does not establish the current strength or the six-level filter as calibrated ocular optics.

The user's latest clarification preserves strong normal bloom, including helmet reflections. Extreme sources need a smooth, monotonic, sublinear artistic response so a dark-adapted shadow-to-sun transition keeps surrounding detail. The optical reference below characterizes normal bloom. It does not justify that artistic response or require a global reduction of normal strength. A factor-of-1,000 source probe diagnoses lost input radiance. Linear halo growth at that extreme is not visual acceptance.

No optical choice blocks the clipping investigation or matched transition captures. The current artistic normal look can remain. Selecting an observer and an angular range would become necessary only before claiming an optical match. No acceptance tolerance has been agreed.

## Primary sources inspected

| Source | Exact location | Evidence and limits |
|---|---|---|
| Spencer, Shirley, Zimmerman and Greenberg, *Physically-Based Glare Effects for Digital Images*, SIGGRAPH 1995, pp. 325–334 | [Original PDF URL](https://www.graphics.cornell.edu/pubs/1995/SSZG95.pdf), [working archived author PDF](https://web.archive.org/web/20240710031813id_/https://www.graphics.cornell.edu/pubs/1995/SSZG95.pdf), [archived publication record](https://web.archive.org/web/20240808002248id_/http://www.graphics.cornell.edu/pubs/1995/SSZG95.html) | PDF p. 4: absolute veiling luminance and perceptual normalization. PDF p. 5: normalized components and photopic equation (5). PDF p. 6: age equation (10), adaptation variants and numerical filter construction. PDF p. 7: add glare to excess intensity. PDF p. 8: example conditions and limitations. |
| Talvala, Adams, Horowitz and Levoy, *Veiling Glare in High Dynamic Range Imaging*, SIGGRAPH 2007 | [Author page](https://graphics.stanford.edu/papers/glare_removal/), [author PDF](https://graphics.stanford.edu/papers/glare_removal/glare_removal.pdf) | PDF pp. 3–4: measured camera conditions, numerical glare anchors, spatial variation and equations (1)–(3). This source does not provide a universal camera strength or radial profile. |

The current Cornell server redirects the original paper URL to a general department page. The archived PDF above contains the paper itself. Its PDF page numbers run from 1 to 10. The numerical work below uses its equations, not a secondary implementation. Attempts to retrieve the later CIE/Vos full text did not yield an inspectable primary equation. No CIE-specific coefficients or validity claims enter these calculations.

## Human-eye equations and units

A point-spread function, or PSF, describes how a point source spreads over viewing directions. Spencer's PSF is a **normalized perceptual** response. It includes the eye's directional sensitivity. It is not the fraction of photons physically scattered inside the eye.

The source defines the PSF on the entering hemisphere, from 0 to 90 degrees from the source direction. Equations (3) and (4), PDF pp. 4–5, require

\[
2\pi\frac{\pi}{180}\int_0^{90}P(\theta)\sin(\pi\theta/180)\,d\theta=1.
\]

where

- \(P\) is perceptual response per steradian, abbreviated sr⁻¹.
- \(\theta\) is angular separation in degrees. The factors of \(\pi/180\) convert degrees to radians.
- The integral includes the central response and the surrounding glare.

PDF p. 5 gives these normalized component functions:

\[
f_0(\theta)=2.61\times10^6\exp[-(\theta/0.02)^2],\qquad
f_1(\theta)=\frac{20.91}{(\theta+0.02)^3},\qquad
f_2(\theta)=\frac{72.37}{(\theta+0.02)^2}.
\]

where \(f_0\) is the narrow central response, \(f_1\) supplies the steeper near glare, and \(f_2\) supplies the broad glare. Each function has units sr⁻¹ when its input uses degrees. Changing the input to radians without changing the constants is wrong.

Equation (5), PDF p. 5, gives the normal photopic response:

\[
P_p=0.384f_0+0.478f_1+0.138f_2.
\]

where \(P_p\) is the response for a light-adapted observer. The paper classifies field luminance above 3 cd/m² as photopic, 0.01–3 cd/m² as mesopic, and below 0.01 cd/m² as scotopic. These are source assumptions, not adaptation inferred from engine exposure stops.

Equation (10), PDF p. 6, gives a rough relation for age in an observer without cataracts:

\[
q_A=6.9\times10^{-9}A^4,\qquad
P_A=(0.384-q_A)f_0+0.478f_1+(0.138+q_A)f_2.
\]

where \(A\) is observer age in years and \(q_A\) transfers perceptual response from the central component to broad glare. The paper does not give a formal age validity interval. Ages 20 and 60 below lie within its explicit comparison: the fraction beyond 0.05° grows from about 0.36 to 0.45. This relation is not a cataract model.

The source's hemispheric domain is a model domain, not evidence of equal measurement accuracy at every angle. These are empirical fits to glare research. The coefficients are rounded. They do not supply an error tolerance for Tertius.

### Numerical anchors independent of the renderer

The following fractions integrate \(P_A\) with the solid-angle measure above. They represent response to a unit point source, before any display threshold.

| Observer assumption | Fraction beyond 0.05°, through 90° | Fraction from 1° to 2° | Fraction from 2° to 4° | Fraction from 4° to 8° | Fraction from 8° to 16° |
|---|---:|---:|---:|---:|---:|
| Photopic, age 20, no cataract | 0.36341 | 0.022124 | 0.017834 | 0.015597 | 0.014395 |
| Photopic, age 60, no cataract | 0.44496 | 0.030363 | 0.026189 | 0.024001 | 0.022785 |

At angles well above the near component, the broad tail approaches \(P_A(\theta)=C_A/\theta^2\). The coefficients \(C_A\) are 10.06696 for age 20 and 16.45867 for age 60, using degrees and sr⁻¹. For small angles, each doubling of radius then contains approximately 0.01336 and 0.02184 of the unit source response, respectively. This gives an absolute normalization and radial falloff anchor. The complete equations, rather than this asymptote, supplied the table.

The paper's earlier prose says that perceived scattering is "roughly 10%". Do not turn that sentence into a universal bloom-strength constant. The explicit PSF includes narrow scattering, and the response outside a region depends on that region's angular radius.

### Threshold, pupil and adaptation

PDF p. 7 states that if computed intensity \(I\) exceeds maximum displayable intensity \(I_m\), the filter applies to \(I-I_m\). Here \(I\) and \(I_m\) use the same linear intensity units. This is primary support for an additive excess-light approximation on a limited display. Tertius's soft knee smooths that threshold. The source does not specify Tertius's knee of 0.5 or strength of 0.25.

The photopic calculation excludes the colored lenticular ring. PDF pp. 5–6 associate that ring with pupils above about 3 mm. The source gives a 568 nm ring near 3° and separate mesopic/scotopic coefficients in equations (8) and (9). Applying those variants adds a visible colored ring and changes glare energy. They would change the selected normal appearance, so they are outside this characterization.

## Match the reference to the actual source

The diagnostic lamp is a square, not a point. At 640 × 360 output pixels and 60° vertical field of view, its 24-pixel side spans about 4.41°. Its solid angle is 0.00591716 sr. Treating it as a point at the first measurement ring would bias the result.

For each receiving direction, integrate the PSF over the square source:

\[
H_A(\mathbf u)=\int_{\text{source}}Q(\mathbf s)\,
P_A\!\left(\frac{180}{\pi}\arccos(\mathbf u\cdot\mathbf s)\right)\,d\Omega_{\mathbf s}.
\]

where

- \(\mathbf u\) is a unit camera ray through a receiving pixel.
- \(\mathbf s\) is a unit camera ray through an emitting point on the source.
- \(Q\) is the source's excess radiance after exposure and threshold. The lamp has uniform radiance 64 and threshold 1, so \(Q=63\).
- \(d\Omega_{\mathbf s}\) is the solid angle of a source area element, in sr.
- \(H_A\) is the reference halo in the same linear pre-exposed radiance units as \(Q\).

This comparison assumes a constant white spectrum. Any constant conversion from radiance to luminance cancels between source and halo. A spectrally varying source requires spectral assumptions. The engine's exposure stops alone do not supply physical display luminance, pupil size or viewer adaptation.

### Independent numerical procedure

The focal length in output pixels is \(f=180/\tan(30°)=311.76915\). A ray through coordinates \((x,y)\), measured from the source center, is the normalized vector \((x/f,y/f,1)\). Source cells cover \([-12,12]\times[-12,12]\) output pixels. Each 1 × 1 cell uses its center ray and exact solid angle from corner differences of

\[
g(x,y)=\arctan\frac{(x/f)(y/f)}{\sqrt{1+(x/f)^2+(y/f)^2}}.
\]

where \(g\) is the rectangular solid-angle primitive, in sr. A cell's solid angle is \(g(x_1,y_1)-g(x_0,y_1)-g(x_1,y_0)+g(x_0,y_0)\), with subscripts 0 and 1 naming its lower and upper bounds.

Receiving positions match bloom level 0: \(x=2i+1-320\), \(y=2j+1-180\), for integer column \(i\) from 0 to 319 and row \(j\) from 0 to 179. Rings select Euclidean pixel radii [24,48), [48,96), and [96,192). The image clips the third ring vertically. Ring values are arithmetic means over those positions, matching `BloomHalo.RingMeans`.

The outside-source sum excludes the central square extending 14 pixels from the center on each axis. Each remaining level-0 value represents four output pixels. This matches the current `OutsideEnergy` measurement. Its units are radiance times output pixels. It is not a solid-angle integral or a fraction of physical source energy. Multiply individual receiving values by their solid angles to measure angular response. The saved aggregate JSON cannot recover that weighting exactly.

Source integration at 2 × 2 cells and then 1 × 1 cells changes the reference outside sum by 0.25% for age 20 and 0.20% for age 60. Ring means change by less than 0.10%. These checks bound this quadrature change only. They are not experimental uncertainty or an acceptance tolerance. The point-source anchor table uses Simpson integration in log angular radius with 12,000 intervals per range.

### Existing measurements against the reference

The measured column comes from `issue-3-before/Lamp-x10.json` in the lead's recovery evidence. It uses fixed exposure 0 stops, threshold 1, knee 0.5, strength 0.25 and six levels. All halo columns below include strength where applicable and precede the tone curve.

| Measurement | Current lamp, radiance 64 | Optical reference, age 20 | Optical reference, age 60 |
|---|---:|---:|---:|
| Mean, radius 24–48 px | 0.294325 | 0.118380 | 0.181517 |
| Mean, radius 48–96 px | 0.0597684 | 0.0255611 | 0.0405185 |
| Mean, radius 96–192 px, cropped by image | 0.00871405 | 0.00668619 | 0.0107556 |
| Outside-source sum, radiance × output pixels | 4875.40 | 2669.14 | 4125.77 |

The radii 24, 48, 96 and 192 pixels correspond to center-ray angles 4.402°, 8.753°, 17.115° and 31.626°. Individual source-to-receiver separations differ because the source has finite size. The integration uses those individual separations.

The current first two ring means are 2.49 and 2.34 times the age-20 reference. The third is 1.30 times that reference. Against age 60, the factors are 1.62, 1.48 and 0.81. Successive mean ratios are 4.92 and 6.86 for the current filter, 4.63 and 3.82 for age 20, and 4.48 and 3.77 for age 60. The current outer falloff is steeper.

These compare level-0 measurements with optical response sampled at the same positions. The tone map applies one more tent filter before adding bloom to the displayed image. Final-output calibration must include that filter and the tone curve separately. This table does not certify displayed contrast or clipped area. It also does not predict the panels from their center pixels: their spatial radiance can vary, so the reference needs their full source image.

## What the six-level filter can establish

The current chain normalizes its sum over six scales, then applies strength 0.25. Away from boundaries, with exact arithmetic, it adds about 0.25 times the thresholded source's image integral. Half-float storage, image boundaries and sampling affect the measured integral. Equal contribution per scale can approximate an inverse-square tail over an interior range of radii. Its finite largest scale makes the outer falloff steeper.

Strength alone cannot make all three measured rings match either observer. For age 20, the individual matching strengths would be 0.1006, 0.1069 and 0.1918. For age 60 they would be 0.1542, 0.1695 and 0.3086. These are diagnostic calculations, **not proposed production settings**. Globally applying them would weaken the normal highlights the user wants.

At 360-pixel height, the first two rings have a similar falloff to either reference over roughly 4.4°–17.1°. Their successive-mean ratio differs from age 20 by 6.3% and from age 60 by 9.9%. Those residuals describe a limited approximation after a scalar amplitude fit. They do not establish an accepted tolerance, an absolute match at strength 0.25, or agreement in the outer ring.

The current filter's distances use output pixels. At equal field of view, a fixed pixel radius has about one third the small-angle extent at 1080-pixel height. For example, 24, 48, 96 and 192 pixels span 1.470°, 2.938°, 5.860° and 11.601° there. A physically identical 4.41° source instead needs a 72-pixel side, with measurement radii 72, 144 and 288 pixels. Six levels remain finite at the same pixel scale. A 360-pixel result cannot establish 1080-pixel angular behavior.

Each added level approximately doubles the broadest pixel scale. Matching a threefold height increase would require about log₂(3) = 1.585 additional levels. Integer levels alone cannot hold the scale exactly. Changing level count also changes contribution per scale because the sum divides by that count. Changing field of view changes the pixel-to-angle mapping again.

The lead can diagnose this without changing production: compare the lamp at 360-pixel height with seven levels, and at 1080-pixel height with six and eight levels. Keep source solid angle, source radiance, exposure and angular ring boundaries constant. This distinguishes finite-range loss from a failure of the filter family. No such captures were run by this research stage, and their results must not be inferred from this report.

## Two defensible observer references

These are alternate optical reference assumptions, not alternate artistic curve proposals. The normal artistic strength remains settled.

| Reference | Absolute broad tail and angular falloff | Reference example | User-visible implication if adopted as a target | Cost and limitation |
|---|---|---|---|---|
| Photopic observer, age 20, no cataract | Equation (10), with coefficients 0.382896, 0.478 and 0.139104. Far tail approaches 10.06696/θ² sr⁻¹. | The fixed-exposure 24-pixel lamp gives ring means 0.118380, 0.0255611 and 0.00668619. | Less surrounding glare than the current artistic normal response. | Independent reference calculation is small and offline. Matching it in production would weaken desired normal highlights unless the spatial profile changed. It is not authorized by the current brief. |
| Photopic observer, age 60, no cataract | Equation (10), with coefficients 0.294576, 0.478 and 0.227424. Far tail approaches 16.45867/θ² sr⁻¹. | The same lamp gives ring means 0.181517, 0.0405185 and 0.0107556. | More broad glare and lower nearby contrast than the younger reference. | Same calculation cost. The age relation is rough and does not justify choosing an age to fit the existing filter. It still fails to match all current rings by one strength. |

Both references share the same primary source, angular units, finite-square integration, achromatic approximation and photopic state. Both can remain diagnostic comparisons. Neither requires changing the selected artistic appearance. Do not infer an observer age from a fit to the current bloom and then present that age as independent validation.

## Camera-lens alternative

Talvala et al. measured three actual camera systems. Their Canon EOS 20D used a Canon 28–135 mm IS USM lens at 28 mm and f/8. They varied shutter time from 30 s to 1/8,000 s while keeping the aperture fixed. The comparison cameras were a Canon A640 at f/8 with moderate zoom and a 640 × 480 camera-array tile with a fixed lens. These are specific optical systems, not a generic eye or game camera.

PDF p. 3 reports the 20D's broad uniform glare around 20 binary orders below a point source. That is approximately 2⁻²⁰, or 9.54 × 10⁻⁷, in local relative intensity. It is **not** a total scattered-energy fraction. Their simulations give about 20 stops of recordable dynamic range for a point source, but about nine stops for a source covering half the field. The A640 gives about seven stops in the latter case. Source extent matters.

The camera glare changes with source position, aperture and lens. PDF p. 4 uses a full transport matrix in equation (1). Its convolution approximation in equation (3) assumes spatial invariance, which the authors explicitly call generally unjustified. The paper has measured plots, ghosts and diffraction artifacts, but no tabulated universal radial equation from which to derive strength 0.25.

A camera reference would therefore need a named lens, aperture, focus, source position and measured glare data. Choosing an unspecified lens cannot calibrate the current filter. The physical capture and glare-removal process in this paper is not a proposed runtime dependency.

## Source situation and fit to Tertius

| Source | Original scale, hardware and quality goal | Dependencies | Fit to this task |
|---|---|---|---|
| Spencer et al. | Offline HDR stills. Their direct filtering took about 1–3 minutes per image on an HP9000/755. Perceptual trials used a Sony Trinitron 19-inch display, 300 × 300 stimuli on a 1280 × 1024 screen, and seven subjects in a dark room. The goal was apparent brightness on a limited display. | Viewer adaptation, angular field, HDR source intensity and numerical integration. No denoiser. Their digital method also creates radial streaks and a spectral ring. | The equations are useful as an independent reference. The full offline algorithm and its display experiment do not establish real-time cost, modern HDR-display behavior or an extreme-highlight artistic curve. |
| Talvala et al. | Measured HDR photography from 8/10-megapixel cameras and a 640 × 480 tile. The goal was removal of physical camera glare. Their mask capture took 30–60 minutes. | A specific camera and optics, exposure stacks, an occlusion mask and reconstruction. No denoiser. | Useful for showing why a camera PSF needs measured conditions. It supplies no defensible generic replacement profile. |

Direct per-pixel convolution with a large optical kernel has much higher constant cost than the current fixed-tap image pyramid. Tertius can evaluate the optical equations offline for diagnostic rings without adding a runtime filter. This research makes no GPU performance claim. Any production filter change needs matched Linux Runpod measurements.

## Verified helmet scene reference

The parent identified the visible helmet reference at release time 101.000 s: frame `night-000360`, simulation tick 94,901. The angle can be replayed with the saved [full-shuttle cockpit script](/Users/alexispurslane/.t3/worktrees/tertius/t3-7d632f6e-af62e861215c5d93/docs/research/cockpit-helmet-light/tools/cockpit.json). This research stage verified that the file exists and read its contents. It selects `demo-surface`, runs the shuttle sequence, then records 120 frames from `cockpit-camera`, with 120 simulation ticks between frames, at 1280 × 720 output pixels.

The parent reports that an earlier capture used fixed exposure 6.0043726 stops. That exposure is provenance from the earlier capture, not a setting encoded in this JSON file. Record the exposure actually applied in each new capture. The script provides a reproducible angle and scene sequence. Do not assume that its first frame has the release reference's exact tick.

Measure the visible helmet highlight and its surrounding glow. The earlier issue #20 ceiling crop is a different measurement region. Keep pose, simulation tick and applied exposure identical between each bloom-on and bloom-off pair. The continuous exterior replay and its same-tick controls establish the shadow-to-sun behavior separately.

## Remaining evidence

1. Capture the continuous shadow-to-sun sequence, or label an explicit reproduction. Include identical-pose bloom-off controls and both adapting and settled exposure.
2. Keep matched helmet images and measure surrounding hull/window detail, displayed clipped area and locally intense glow. Separate direct overexposure from added bloom.
3. Preserve distinguishable source radiance upstream. Characterize factor-of-1,000 inputs without making linear extreme output an acceptance condition.
4. If angular profile stability becomes a production requirement, record matched 360/1080-pixel and field-of-view measurements. The current optical comparison supplies no claim that these already match.
5. Label the chosen extreme response as artistic. Keep its normal-response anchors separate from the optical reference equations and from exposure metering.

No production files, builds, test runs, branches, commits or pull requests changed during this research stage. Only this report was written. Numerical source integration and primary-paper text extraction supplied the values above.
