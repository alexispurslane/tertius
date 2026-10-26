// The sectors of a slice and the light of its blockers (docs/plans/screen-space-illumination.md, "The method").
// SliceSectors, SectorMask and SectorSearch in Shading/ are the C# copy. Change both together. The paper publishes no
// code, so this file is engine code, and XeGTAO's notice stays in gtao.glsl.
#ifndef TERTIUS_SECTORS_GLSL
#define TERTIUS_SECTORS_GLSL

#include "gtao.glsl"
#include "depth_pyramid.glsl"

const uint SectorCount = 32u;
// The halvings of the search for the angle at a sector's center: π / 4,096 at the end (SliceSectors.AngleHalvings).
const int AngleHalvings = 12;
// The total slice weight at or below which a texel holds no estimate (SectorSearch.MinTotalWeight).
const float MinTotalWeight = 1e-6;

// F(h): the cosine-weighted measure from 0 to h, signed by h. gtaoSliceArc gives the measure between 0 and h.
float signedArc(float h, float gamma)
{
    return h >= 0.0 ? gtaoSliceArc(h, gamma) : -gtaoSliceArc(h, gamma);
}

// m(θ): the share of the slice's arc from its edge at γ − π/2 to the angle, from 0 to 1. The clamp keeps m monotone:
// outside the hemisphere the integrand turns negative. T is at least 1, so the division is safe.
float measureFraction(SliceGeometry g, float angle)
{
    float low = g.normalAngle - GtaoHalfPi;
    float clamped = clamp(angle, low, g.normalAngle + GtaoHalfPi);
    return (signedArc(clamped, g.normalAngle) - signedArc(low, g.normalAngle)) / g.arc;
}

// The first sector whose center lies after the measure fraction m, from 0 to 32: a tie goes to the later sector.
// floor(x + 0.5), not round, whose ties GLSL leaves to the driver.
uint sectorBoundary(float m)
{
    return uint(floor(m * float(SectorCount) + 0.5));
}

// The sectors from start to end − 1, none when end ≤ start. bitfieldInsert never shifts by 32, which GLSL leaves
// undefined.
uint sectorRun(uint start, uint end)
{
    return end > start ? bitfieldInsert(0u, 0xFFFFFFFFu, int(start), int(end - start)) : 0u;
}

// θ of a direction in the slice, from V̂. side, +1 or −1, is the side of the step whose point the direction reaches.
// The clamp keeps acos defined where rounding puts the cosine of two unit vectors past ±1.
float sliceAngle(SliceGeometry g, vec3 direction, float side)
{
    return side * acos(clamp(dot(normalize(direction), g.toEye), -1.0, 1.0));
}

// The sectors whose centers lie between the directions to a step's surface point and to the back of its slab.
uint stepSectors(SliceGeometry g, vec3 center, vec3 samplePosition, float side)
{
    float front = sliceAngle(g, samplePosition - center, side);
    float back = sliceAngle(g, samplePosition * (1.0 + ThicknessShare) - center, side);
    return sectorRun(sectorBoundary(measureFraction(g, min(front, back))),
                     sectorBoundary(measureFraction(g, max(front, back))));
}

// The sectors whose centers lie below the receiver's own geometric plane, which its surface blocks with no light
// (docs/plans/screen-space-illumination.md, decision 39). The plane's angle in the slice, γ_g, is taken within π of γ,
// so its edges in the slice are γ_g ± π/2. A normal with no part in the slice plane blocks nothing, and atan(0, 0) is
// undefined.
uint ownSurfaceSectors(SliceGeometry g, vec3 geometricNormal)
{
    vec3 shading = cos(g.normalAngle) * g.toEye + sin(g.normalAngle) * g.side;
    vec3 acrossShading = cos(g.normalAngle) * g.side - sin(g.normalAngle) * g.toEye;
    vec2 inSlice = vec2(dot(geometricNormal, shading), dot(geometricNormal, acrossShading));
    if (length(inSlice) < MinProjectedNormalLength)
        return 0u;
    float planeAngle = g.normalAngle + atan(inSlice.y, inSlice.x);
    return sectorRun(0u, sectorBoundary(measureFraction(g, planeAngle - GtaoHalfPi)))
           | sectorRun(sectorBoundary(measureFraction(g, planeAngle + GtaoHalfPi)), SectorCount);
}

// The least and the greatest view depth of the texels around a step's exact place: its texel t, at tDepth, and t's two
// neighbors toward the place, along the row and along the column. The place lies within half a texel of t on each axis,
// so their triangle holds it. towardPlace is the place minus t, in half-resolution texels (SectorSearch.DepthsAround).
vec2 depthsAround(ivec2 t, float tDepth, vec2 towardPlace)
{
    ivec2 across = ivec2(sign(towardPlace));
    float alongRow = viewDepthAt(t + ivec2(across.x, 0));
    float alongColumn = viewDepthAt(t + ivec2(0, across.y));
    return vec2(min(tDepth, min(alongRow, alongColumn)), max(tDepth, max(alongRow, alongColumn)));
}

// s_f: a step's surface point on its slice, where the view ray through its exact place, exactRay (z = 1), meets the
// plane of its texel's surface, through texelPosition at right angles to texelNormal (SectorSearch.SurfacePoint). False
// where the ray meets the plane only behind the eye or never: the surface does not reach the slice there, and the step
// blocks nothing. At a view depth outside the depths around the exact place, widened by the depths' precision,
// 1 − CenterBias, the plane does not reach the place, as beside a convex edge, and the step stands at texelPosition,
// the real point that it read. Also false at a view depth of farthestDepth or more, which lies beyond the radius. The
// tests by products before the quotient keep the depth finite.
bool stepSurfacePoint(vec3 exactRay, vec3 texelPosition, vec3 texelNormal, vec2 depthsAround, float farthestDepth,
                      out vec3 point)
{
    float planeOffset = dot(texelNormal, texelPosition);
    float rayAlongNormal = dot(texelNormal, exactRay);
    bool inFront = planeOffset * rayAlongNormal > 0.0;
    // The depth is |planeOffset| / |rayAlongNormal| where the two share a sign.
    float depthTimesRay = abs(planeOffset);
    float rayLength = abs(rayAlongNormal);
    bool betweenAround = depthTimesRay >= rayLength * depthsAround.x * CenterBias
                         && depthTimesRay <= rayLength * depthsAround.y / CenterBias;
    if (!inFront)
        return false;
    if (!betweenAround)
    {
        point = texelPosition;
        return true;
    }
    if (!(depthTimesRay < rayLength * farthestDepth))
        return false;
    point = exactRay * (planeOffset / rayAlongNormal);
    return true;
}

// The direct light that a blocker at a full-resolution pixel gives the directions that it blocks: all of it when its
// view-space geometric normal faces towardReceiver, and none when the receiver sees its back.
vec3 blockerLight(ivec2 pixel, vec3 geometricNormal, vec3 towardReceiver)
{
    return dot(geometricNormal, towardReceiver) > 0.0 ? texelFetch(gbufferDirect, pixel, 0).rgb : vec3(0.0);
}

// θ at the center of a sector: the inverse of measureFraction at (sector + ½) / 32, by halving, since m has no closed
// inverse (SliceSectors.CenterAngle).
float sectorCenterAngle(SliceGeometry g, uint sector)
{
    float target = (float(sector) + 0.5) / float(SectorCount);
    float low = g.normalAngle - GtaoHalfPi;
    float high = g.normalAngle + GtaoHalfPi;
    for (int i = 0; i < AngleHalvings; i++)
    {
        float middle = 0.5 * (low + high);
        if (measureFraction(g, middle) < target)
            low = middle;
        else
            high = middle;
    }
    return 0.5 * (low + high);
}

// The open sector of a rank, counted from sector 0 (SectorMask.SectorOfRank). open has more than rank bits set, so
// findLSB never meets 0, where it returns −1.
uint openSectorOfRank(uint open, uint rank)
{
    for (uint i = 0u; i < rank; i++)
        open &= open - 1u;
    return uint(findLSB(open));
}

#endif
