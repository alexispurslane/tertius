// The sectors of a slice and the light of its blockers (docs/plans/screen-space-illumination.md, "The method").
// SliceSectors, SectorMask and SectorSearch in Shading/ are the C# copy. Change both together. The paper publishes no
// code, so this file is engine code, and XeGTAO's notice stays in gtao.glsl.
#ifndef TERTIUS_SECTORS_GLSL
#define TERTIUS_SECTORS_GLSL

#include "gtao.glsl"
#include "texel_surface.glsl"

const uint SectorCount = 32u;
// The steps of boundaryAngle's bisection, SliceSectors.BoundarySteps.
const int BoundarySteps = 8;
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

// The angle of the boundary `boundary`, from 0 to 32, where sector `boundary` begins: the angle whose measure fraction
// is boundary / 32. The ends of the hemisphere at 0 and 32, and in between the bisection of m over BoundarySteps steps,
// within 2^-9 π rad of it (SliceSectors.BoundaryAngle).
float boundaryAngle(SliceGeometry g, uint boundary)
{
    float low = g.normalAngle - GtaoHalfPi;
    float high = g.normalAngle + GtaoHalfPi;
    if (boundary == 0u)
        return low;
    if (boundary == SectorCount)
        return high;
    float fraction = float(boundary) / float(SectorCount);
    for (int step = 0; step < BoundarySteps; step++)
    {
        float middle = 0.5 * (low + high);
        if (measureFraction(g, middle) < fraction)
            low = middle;
        else
            high = middle;
    }
    return 0.5 * (low + high);
}

// The angles of `piece`, an unbroken piece of a slice's sectors that is not empty, from the boundary where its first
// sector begins to the one where its last sector ends (SliceSectors.PieceAngles).
vec2 pieceAngles(SliceGeometry g, uint piece)
{
    return vec2(boundaryAngle(g, uint(findLSB(piece))), boundaryAngle(g, uint(findMSB(piece) + 1)));
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

// cos θ V̂ + sin θ ô: the unit view-space direction at the angle θ in the slice.
vec3 sliceDirection(SliceGeometry g, float angle)
{
    return cos(angle) * g.toEye + sin(angle) * g.side;
}

// A run of a slice's sectors, first to end − 1, whose centers lie between the angles low and high, both within the
// hemisphere (SectorRun). An empty run has end ≤ first.
struct SectorRun
{
    uint first;
    uint end;
    float low;
    float high;
};

// The run of the sectors whose centers lie between the angles a and b, a ≤ b.
SectorRun runBetween(SliceGeometry g, float a, float b)
{
    SectorRun run;
    run.first = sectorBoundary(measureFraction(g, a));
    run.end = sectorBoundary(measureFraction(g, b));
    run.low = clamp(a, g.normalAngle - GtaoHalfPi, g.normalAngle + GtaoHalfPi);
    run.high = clamp(b, g.normalAngle - GtaoHalfPi, g.normalAngle + GtaoHalfPi);
    return run;
}

uint runSectors(SectorRun run)
{
    return sectorRun(run.first, run.end);
}

// The unbroken piece of the set `sectors` that holds its lowest sector, up to the first sector that the set leaves out;
// none for an empty set. The sum with the lowest bit carries through the piece and clears it, and a piece that ends at
// sector 31 wraps the sum to 0, which unsigned GLSL arithmetic does (SectorMask.LowestPiece).
uint lowestPiece(uint sectors)
{
    return sectors & ~(sectors + (sectors & (~sectors + 1u)));
}

// The angles of the part `part`, an unbroken piece of the run's sectors that is not empty, from its first sector to its
// last: each sector stands at the angle that its place in the run takes in proportion between low and high
// (docs/plans/screen-space-illumination.md, decision 41).
vec2 partAngles(SectorRun run, uint part)
{
    float perSector = (run.high - run.low) / float(run.end - run.first);
    vec2 places = vec2(float(findLSB(part)), float(findMSB(part) + 1)) - float(run.first);
    return run.low + places * perSector;
}

// The run of the sectors whose centers lie between the directions to a step's surface point and to the back of its
// slab.
SectorRun stepRun(SliceGeometry g, vec3 center, vec3 samplePosition, float side)
{
    float front = sliceAngle(g, samplePosition - center, side);
    float back = sliceAngle(g, samplePosition * (1.0 + ThicknessShare) - center, side);
    return runBetween(g, min(front, back), max(front, back));
}

// The empty run, which holds no sector, at the shading normal's angle γ (SliceSectors.NoRun).
SectorRun noRun(SliceGeometry g)
{
    return SectorRun(0u, 0u, g.normalAngle, g.normalAngle);
}

// The run of the sectors whose centers lie below the receiver's own geometric plane, which its surface blocks with no
// light (docs/plans/screen-space-illumination.md, decision 39). The plane's angle in the slice, γ_g, is taken within π
// of γ, so its edges in the slice are γ_g ± π/2, and only one of them lies inside the hemisphere: the lower one where
// γ_g > γ, the upper one where γ_g < γ. A normal with no part in the slice plane blocks nothing, and atan(0, 0) is
// undefined.
SectorRun ownSurfaceRun(SliceGeometry g, vec3 geometricNormal)
{
    vec3 shading = sliceDirection(g, g.normalAngle);
    vec3 acrossShading = sliceDirection(g, g.normalAngle + GtaoHalfPi);
    vec2 inSlice = vec2(dot(geometricNormal, shading), dot(geometricNormal, acrossShading));
    if (length(inSlice) < MinProjectedNormalLength)
        return noRun(g);
    float planeAngle = g.normalAngle + atan(inSlice.y, inSlice.x);
    return planeAngle > g.normalAngle ? runBetween(g, g.normalAngle - GtaoHalfPi, planeAngle - GtaoHalfPi)
                                      : runBetween(g, planeAngle + GtaoHalfPi, g.normalAngle + GtaoHalfPi);
}

// s_f: a step's surface point on its slice, where the view ray through its exact place meets the surface of its texel t
// (texelSurface, the pixels' rule, SectorSearch.TexelSurface): t's plane over the part of its square that reaches toward
// each neighbor that continues it, and the plane's value at the part's edge past it, read with the depth pass's
// continuation flags of t. place is the exact place in the texel grid, within half a texel of t on each axis, where whole
// numbers are a texel's representative pixel. The surface's inverse depth is positive everywhere on t's square: the part
// reaches only toward a neighbor whose center lies within 1 % of the plane. False at a view depth of farthestDepth or
// more, which lies beyond the radius.
bool stepSurfacePoint(ivec2 t, uvec2 depthAndNormal, vec2 place, float farthestDepth, out vec3 point)
{
    point = vec3(0.0);
    float w = surfaceW(texelSurface(t, depthAndNormal, continuationAt(t)), place);
    if (!(w * farthestDepth > 1.0))
        return false;
    // A texel spans two pixels, and its representative pixel's center lies half a pixel past its place.
    point = gtaoViewPosition((2.0 * place + 0.5) / vec2(fullSize()), 1.0) / w;
    return true;
}

// The direct light that a blocker of direct light `direct` gives the directions that it blocks: all of it when its
// view-space geometric normal faces towardReceiver, and none when the receiver sees its back.
vec3 blockerLight(vec3 direct, vec3 geometricNormal, vec3 towardReceiver)
{
    return dot(geometricNormal, towardReceiver) > 0.0 ? direct : vec3(0.0);
}

#endif
