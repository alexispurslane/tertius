# Change 1 of branch t6-h5e3-six-frame-cuts on main's lighting.glsl: a far rectangle shades as a point light, in the
# one loop of the rectangles. With form == "split", the pairs that shade as points take a loop of their own (x19).
import sys
p = sys.argv[1]; form = sys.argv[2] if len(sys.argv) > 2 else "one"
s = open(p).read()
t = open('/private/tmp/t2-mlvy/t6-lighting.glsl').read()
def sub(a, b):
    global s
    assert s.count(a) == 1, a
    s = s.replace(a, b)
def between(text, start, end):
    i = text.index(start); j = text.index(end, i)
    return text[i:j]
# The constants.
sub("// ShaderInput.IrradianceLimit", between(t, "// RectView.PointDistanceRatio", "// ShaderInput.IrradianceLimit") + "// ShaderInput.IrradianceLimit")
# rectShadesAsPoint and rectResponse.
mine = between(s, "// MaterialSample.RectTerms per unit of the rectangle's face radiance", "// The light of light `index` along its path")
theirs = between(t, "// RectView.ShadesAsPointOn", "// The light of light `index` along its path")
if form == "split":
    block = """    if (point && s.roughness >= RectPointRoughness)
    {
        terms = surfaceResponse(s, lobes, V, p.L, 0.0);
        terms.diffuse *= solidAngle;
        terms.specular *= solidAngle;
        return terms;
    }
"""
    assert theirs.count(block) == 1
    theirs = theirs.replace(block, "")
sub(mine, theirs)
if form == "split":
    sub("""void shapeLight(bool fiber, bool tinted, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps,
    uint view, inout DirectTerms sum)
{
    // frame.counts.w holds the shapes of the list: without a light of this shape, its loop would only skip every light.
    if ((frame.counts.w & shape) == 0u)
        return;
""", """const uint WholeForm = 0u;
const uint RectAsPoint = 1u;
const uint RectByEdges = 2u;

void shapeLight(bool fiber, bool tinted, uint shape, uint form, ShadingInput s, SurfaceLobes lobes, vec3 V,
    uint shadowMaps, uint view, inout DirectTerms sum)
{
    // frame.counts.w holds the shapes of the list: without a light of this shape, its loop would only skip every light.
    if ((frame.counts.w & shape) == 0u)
        return;
    bool pointSurface = !fiber && s.roughness >= RectPointRoughness;
    if (form == RectAsPoint && !pointSurface)
        return;
""")
    sub("""        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility > 0.0)
        {
            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);
            sum.diffuse += terms.diffuse * visibility;
            sum.specular += terms.specular * visibility;
        }
""", """        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        if (form != WholeForm && (pointSurface && rectShadesAsPoint(s.normal, p)) != (form == RectAsPoint))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility > 0.0)
        {
            DirectTerms terms;
            if (form == RectAsPoint)
            {
                float scale = 1.0 / max(p.d, 1e-6);
                float solidAngle = -4.0 * dot(p.toCenter * scale, cross(p.halfWidth * scale, p.halfHeight * scale));
                terms = surfaceResponse(s, lobes, V, p.L, 0.0);
                terms.diffuse *= solidAngle * p.irradiance;
                terms.specular *= solidAngle * p.irradiance;
            }
            else
            {
                terms = pathLight(i, fiber, shape, s, lobes, V, p);
            }
            sum.diffuse += terms.diffuse * visibility;
            sum.specular += terms.specular * visibility;
        }
""")
    sub("""    shapeLight(fiber, tinted, PointShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, TubeShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, s, lobes, V, shadowMaps, view, sum);""", """    shapeLight(fiber, tinted, PointShape, WholeForm, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, TubeShape, WholeForm, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, RectAsPoint, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, RectByEdges, s, lobes, V, shadowMaps, view, sum);""")
open(p, 'w').write(s)
