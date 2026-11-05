#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"

// The rings of frame outlines (OutlinePass.cs). One instance draws one outline, from the record outlines[instance], and
// makes everything from the vertex index: 4 rings of 64 segments, 6 vertices for each segment.
//
// Rings 0 to 2 are the ellipses in the principal planes of the frame: XY, YZ and ZX, radii along the frame's axes. Ring 3
// is the rim of a sphere as seen from the eye, the circle of tangent points of the cone from the eye: for a sphere of
// radius r whose center is at distance d, it is centered at (1 - r²/d²) of the way to the center, with radius
// r √(1 - r²/d²), in the plane across the view of the center. An ellipsoid, or a sphere round the eye, has no ring 3.
//
// Each segment is a quad 2 pixels wide on the screen, two triangles across the segment's projection. Point k of a ring
// is the corner that segments k - 1 and k share. Both compute its two vertices from the same three ring points, k - 1,
// k and k + 1, so the two quads share one edge exactly, mitered across the bend: no pixel is drawn twice there, which
// matters for a ring that draws at 30 % alpha behind geometry. A segment is cut at the near plane first, so that both
// ends project in front of the eye: a camera inside a bubble sees the parts of its rings in front of it. A cut end is
// square across its segment.

layout(location = 0) out vec3 outWorld;          // the point of the ring, camera-relative, for the occlusion test
layout(location = 1) flat out uint outColor;

const int SEGMENTS = 64;
const int VERTICES_PER_SEGMENT = 6;
const float HALF_WIDTH = 1.0;                    // pixels on each side of the ring
// The longest miter, in half widths. A sharper bend than about 120° keeps this length, so the corner stays near the ring.
const float MITER_LIMIT = 2.0;

// For each vertex of a segment's quad: x the end of the segment (0 start, 1 end), y the side of the line (-1, +1).
const vec2 corners[6] = vec2[](vec2(0.0, -1.0), vec2(1.0, -1.0), vec2(0.0, 1.0),
                               vec2(0.0, 1.0), vec2(1.0, -1.0), vec2(1.0, 1.0));

GpuOutline outline;
int ring;
vec2 halfViewport;

vec3 rotate(vec4 q, vec3 v)
{
    vec3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Whether the ring draws anything. Only a sphere seen from outside has ring 3.
bool ringExists()
{
    return ring < 3 || ((outline.flags & 1u) != 0u && length(outline.center) > outline.radii.x);
}

// The camera-relative point k of the ring, k taken round the ring, so that point 64 is point 0 exactly.
vec3 ringPoint(int k)
{
    float a = float(k & (SEGMENTS - 1)) * (2.0 * PI / float(SEGMENTS));
    float c = cos(a), s = sin(a);
    if (ring < 3)
    {
        vec3 local = ring == 0 ? vec3(c, s, 0.0) : ring == 1 ? vec3(0.0, c, s) : vec3(s, 0.0, c);
        return outline.center + rotate(outline.rotation, local * outline.radii);
    }
    float r = outline.radii.x;
    float d = length(outline.center);
    vec3 n = outline.center / d;
    vec3 u = normalize(cross(n, abs(n.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 v = cross(n, u);
    float shrink = r * r / (d * d);
    // ringExists makes shrink below 1 only in exact arithmetic: a division may be off by 2.5 ULP, and for a center
    // within 2 ULP outside the sphere, shrink can come out above 1, and sqrt of a negative is not defined.
    return outline.center * (1.0 - shrink) + r * sqrt(max(1.0 - shrink, 0.0)) * (c * u + s * v);
}

// The near range's projection, infinite reversed-Z: a point is in front of the near plane where w - z >= 0.
vec4 project(vec3 p)
{
    vec4 clip = frame.rangeViewProj[2] * vec4(p, 1.0);
    clip.xy += 2.0 * frame.renderParams.yz * frame.viewport.zw * clip.w;
    return clip;
}

float front(vec4 clip)
{
    return clip.w - clip.z;
}

// The point of the segment from p (in front of the near plane) to q that is nearest q and in front of the near plane:
// q itself, or the cut at the near plane.
void cut(vec3 p, vec4 clipP, inout vec3 q, inout vec4 clipQ)
{
    float fq = front(clipQ);
    if (fq >= 0.0)
        return;
    float t = front(clipP) / (front(clipP) - fq);
    q = mix(p, q, t);
    clipQ = mix(clipP, clipQ, t);
}

// Pixels from the center of the screen.
vec2 screen(vec4 clip)
{
    return clip.xy / clip.w * halfViewport;
}

// The unit normal on the screen of the direction d, to its left, or zero for a direction too short to have one.
vec2 normalOf(vec2 d)
{
    float l = length(d);
    return l > 1e-6 ? vec2(-d.y, d.x) / l : vec2(0.0);
}

// Every vertex of a segment that draws nothing goes to one point, so its two triangles have no area.
void collapse()
{
    outWorld = vec3(0.0);
    gl_Position = vec4(2.0, 2.0, 0.0, 1.0);
}

void main()
{
    int corner = gl_VertexIndex % VERTICES_PER_SEGMENT;
    int segment = (gl_VertexIndex / VERTICES_PER_SEGMENT) % SEGMENTS;
    ring = gl_VertexIndex / (VERTICES_PER_SEGMENT * SEGMENTS);
    outline = outlines[gl_InstanceIndex];
    outColor = outline.color;
    halfViewport = frame.outputViewport.xy * 0.5;
    if (!ringExists())
    {
        collapse();
        return;
    }

    vec2 cornerOf = corners[corner];
    int k = segment + int(cornerOf.x);            // this vertex's end of the segment
    int other = segment + 1 - int(cornerOf.x);    // the segment's other end
    vec3 p = ringPoint(k), q = ringPoint(other);
    vec4 clipP = project(p), clipQ = project(q);
    if (front(clipP) < 0.0 && front(clipQ) < 0.0)
    {
        collapse();
        return;
    }

    vec2 offset;
    if (front(clipP) < 0.0)
    {
        // This end is behind the near plane: it moves to the cut, square across the segment.
        vec3 cutP = p;
        vec4 clipCut = clipP;
        cut(q, clipQ, cutP, clipCut);
        p = cutP;
        clipP = clipCut;
        vec2 along = cornerOf.x < 0.5 ? screen(clipQ) - screen(clipP) : screen(clipP) - screen(clipQ);
        offset = normalOf(along);
    }
    else
    {
        // A miter between the directions into and out of point k, each from the part of its segment in front of the eye.
        vec3 before = ringPoint(k - 1), after = ringPoint(k + 1);
        vec4 clipBefore = project(before), clipAfter = project(after);
        cut(p, clipP, before, clipBefore);
        cut(p, clipP, after, clipAfter);
        vec2 here = screen(clipP);
        vec2 normalIn = normalOf(here - screen(clipBefore));
        vec2 normalOut = normalOf(screen(clipAfter) - here);
        if (normalIn == vec2(0.0))
            normalIn = normalOut;
        if (normalOut == vec2(0.0))
            normalOut = normalIn;
        vec2 miter = normalIn + normalOut;
        float miterLength = length(miter);
        if (miterLength < 1e-6)
        {
            // The ring turns back on itself on the screen, or has no direction at all.
            offset = normalIn == vec2(0.0) ? vec2(0.0, 1.0) : normalIn;
        }
        else
        {
            miter /= miterLength;
            offset = miter / max(dot(miter, normalIn), 1.0 / MITER_LIMIT);
        }
    }

    vec4 clip = clipP;
    clip.xy += offset * (cornerOf.y * HALF_WIDTH) / halfViewport * clip.w;
    outWorld = p;
    gl_Position = clip;
}
