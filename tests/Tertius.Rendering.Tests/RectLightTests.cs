using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# rectangle light (ARCHITECTURE.md, "Rectangle lights"): its diffuse light and its solid angle against numerical
/// integrals over its face, its LTC shine against a numerical integral of our GGX lobe over the face, the side that takes
/// no light, and its image in a mirror and on a rough floor.
/// </summary>
public sealed class RectLightTests
{
    /// <summary>
    /// A rectangle seen from a floor point at the origin with the normal +Y, by name: its center in m, the turn that takes
    /// its local axes to its width, its height and its front, and its width and height in m.
    /// </summary>
    public static TheoryData<string> Rectangles =>
        ["overhead", "overhead-aside", "near-overhead", "far", "wall", "across-the-horizon", "mostly-below", "tilted"];

    /// <summary>The rectangle of <see cref="Rectangles"/> by its name.</summary>
    private static TestRect RectNamed(string name) =>
        name switch
        {
            // The demo's cabin lamp, a panel 1.2 m square facing down, 2 m above the point.
            "overhead" => Rect(center: new(0, 2, 0), rotation: FaceDown, width: 1.2, height: 1.2),
            "overhead-aside" => Rect(center: new(1.5, 2, 0.5), rotation: FaceDown, width: 1.2, height: 1.2),
            // 0.3 m above the point, so that the panel fills most of the sky.
            "near-overhead" => Rect(center: new(0.2, 0.3, 0), rotation: FaceDown, width: 1.2, height: 0.8),
            "far" => Rect(center: new(0, 6, 3), rotation: FaceDown, width: 1.2, height: 1.2),
            // A bay lamp on a wall 2 m ahead, facing the point, its middle 1 m up.
            "wall" => Rect(center: new(0, 1, 2), rotation: FaceBack, width: 1.2, height: 1.2),
            // A panel on a wall whose lower half lies below the floor's horizon.
            "across-the-horizon" => Rect(center: new(0, 0, 1.5), rotation: FaceBack, width: 1.0, height: 0.8),
            // Only its top 10 cm lie above the horizon.
            "mostly-below" => Rect(center: new(0.3, -0.3, 1), rotation: FaceBack, width: 1.0, height: 0.8),
            "tilted" => Rect(
                center: new(0.8, 1.2, -0.4),
                rotation: Quaternion.Normalize(
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 2.1f)
                ),
                width: 0.9,
                height: 0.4
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such rectangle."),
        };

    /// <summary>A rectangle centered on <paramref name="center"/>, relative to the shaded point.</summary>
    private static TestRect Rect(Double3 center, Quaternion rotation, double width, double height) =>
        new(Center: center, Rotation: rotation, Width: width, Height: height);

    /// <summary>The turn that takes the local front +Z to −Z, toward the origin from a wall ahead.</summary>
    private static Quaternion FaceBack => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    /// <summary>The turn that takes the local front +Z to −Y, straight down.</summary>
    private static Quaternion FaceDown => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    [Theory]
    [MemberData(nameof(Rectangles))]
    public void ARectangleLightsAPointAsTheIntegralOverItsFaceDoes(string name)
    {
        TestRect rect = RectNamed(name);

        double irradiance = rect.View.ClippedCosine(Double3.UnitY);

        double reference = rect.Integral(Double3.UnitY).Irradiance;
        irradiance
            .Should()
            .BeApproximately(
                reference,
                // The grid of 1000 × 1000 cells misses at most a cell's width along the horizon's cut.
                1e-3 * reference + 1e-9,
                "the edge sum, cut at the horizon, is the exact irradiance {0} sr of the rectangle {1}",
                reference,
                name
            );
    }

    [Theory]
    [MemberData(nameof(Rectangles))]
    public void ARectanglesSolidAngleMatchesTheIntegralOverItsFace(string name)
    {
        TestRect rect = RectNamed(name);

        double reference = rect.Integral(Double3.UnitY).SolidAngle;

        rect.View.SolidAngle.Should()
            .BeApproximately(reference, 1e-3 * reference, "the two triangles' solid angles add to the face's {0} sr", reference);
    }

    [Fact]
    public void APointBehindARectangleTakesNoneOfItsLight()
    {
        // The cabin lamp turned to face up, 2 m above the point.
        TestRect rect = Rect(
            center: new(0, 2, 0),
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2),
            width: 1.2,
            height: 1.2
        );
        var matte = new MaterialSample(BaseColor: new(0.8, 0.8, 0.8), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        DirectTerms terms = matte.RectTerms(
            normal: Double3.UnitY,
            toView: Double3.UnitY,
            rect: rect.View,
            tables: BrdfTables.Shared,
            ltc: LtcTable.Shared
        );

        rect.View.SolidAngle.Should().Be(0, "the rectangle sends its light from its front alone");
        terms.Should().Be(default(DirectTerms), "the point lies behind the rectangle's face");
        rect.View.LtcShare(normal: Double3.UnitY, toView: Double3.UnitY, inverse: LtcTable.Shared.Sample(roughness: 0.5, nDotV: 1))
            .Should()
            .Be(0, "no lobe takes light from the back of the face");
    }

    [Fact]
    public void ASurfaceThatFacesAwayFromARectangleTakesNoneOfItsLight()
    {
        TestRect rect = Rect(
            center: new(0, -2, 0),
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2),
            width: 1.2,
            height: 1.2
        );
        var matte = new MaterialSample(BaseColor: new(0.8, 0.8, 0.8), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        DirectTerms terms = matte.RectTerms(
            normal: Double3.UnitY,
            toView: Double3.UnitY,
            rect: rect.View,
            tables: BrdfTables.Shared,
            ltc: LtcTable.Shared
        );

        rect.View.SolidAngle.Should().BePositive("the point lies in front of the rectangle");
        terms.Should().Be(default(DirectTerms), "the whole rectangle lies below the surface's horizon");
    }

    [Fact]
    public void AMatteSurfaceReflectsItsDiffuseColorOverPiOfTheIrradiance()
    {
        // The cabin lamp 2 m above a matte floor: Lambert's surface sends c_diff / π of its irradiance to every view.
        TestRect rect = RectNamed("overhead");
        var matte = new MaterialSample(BaseColor: new(0.8, 0.8, 0.8), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        DirectTerms terms = matte.RectTerms(
            normal: Double3.UnitY,
            toView: Double3.UnitY,
            rect: rect.View,
            tables: BrdfTables.Shared,
            ltc: LtcTable.Shared
        );

        double reference = matte.DiffuseColor.X / Math.PI * rect.Integral(Double3.UnitY).Irradiance;
        terms
            .Diffuse.X.Should()
            .BeApproximately(reference, 1e-3 * reference, "the floor reflects c_diff / π of the irradiance {0} sr", reference);
    }

    [Fact]
    public void AFaceJustBelowTheHorizonSendsNoShineToAGrazingView()
    {
        // A rough floor seen 1.3 rad off its normal, and a panel 2 m away on the side of the mirror ray, wholly below the
        // floor's horizon and facing the point. The fitted cosine leans toward the mirror ray and reaches a little below
        // the horizon, but light from below the surface cannot reach it.
        var toView = new Double3(Math.Sin(1.3), Math.Cos(1.3), 0);
        Vector3 front = Vector3.Normalize(new Vector3(2, 0.5f, 0));
        TestRect rect = Rect(
            center: new(-2, -0.5, 0),
            rotation: Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, front)), MathF.Acos(front.Z)),
            width: 1.0,
            height: 0.6
        );
        var metal = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0.75, Reflectance: 0.5);

        DirectTerms terms = metal.RectTerms(
            normal: Double3.UnitY,
            toView: toView,
            rect: rect.View,
            tables: BrdfTables.Shared,
            ltc: LtcTable.Shared
        );

        rect.View.InFront.Should().BeTrue("the point lies in front of the panel");
        terms.Should().Be(default(DirectTerms), "the whole panel lies below the floor's horizon");
    }

    /// <summary>The 108 checks of the LTC shine against the integral of the GGX lobe over the face (tests/STYLE.md, "Long tests").</summary>
    public sealed class LtcShineAgainstTheIntegral
    {
        /// <summary>
        /// The roughness, the view's angle off the normal in rad and the rectangle of each check of the LTC shine against the
        /// integral. "in-the-mirror" is a panel 1.2 m square 2 m along the mirror ray, facing back: the highlight's peak.
        /// </summary>
        public static TheoryData<double, double, string> Shines
        {
            get
            {
                var data = new TheoryData<double, double, string>();
                foreach (double roughness in new[] { 0.045, 0.1, 0.25, 0.5, 0.75, 1.0 })
                foreach (double viewAngle in new[] { 0.0, 0.6, 1.2 })
                foreach (
                    string rect in new[] { "in-the-mirror", "overhead", "overhead-aside", "wall", "across-the-horizon", "tilted" }
                )
                    data.Add(roughness, viewAngle, rect);
                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Shines))]
        public void TheLtcShineOfARectangleMatchesTheIntegralOfTheGgxLobeOverItsFace(
            double roughness,
            double viewAngle,
            string name
        )
        {
            var metal = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: roughness, Reflectance: 0.5);
            var toView = new Double3(Math.Sin(viewAngle), Math.Cos(viewAngle), 0);
            TestRect rect = name == "in-the-mirror" ? InTheMirror(toView) : RectNamed(name);

            double shine = metal
                .RectTerms(normal: Double3.UnitY, toView: toView, rect: rect.View, tables: BrdfTables.Shared, ltc: LtcTable.Shared)
                .Specular.X;

            double reference = rect.ShineIntegral(metal, toView);
            shine
                .Should()
                .BeApproximately(
                    reference,
                    ShineTolerance(viewAngle, reference),
                    "the LTC shine at roughness {0} and {1} rad off the normal matches the integral {2} of the rectangle {3}",
                    roughness,
                    viewAngle,
                    reference,
                    name
                );
        }

        /// <summary>
        /// How far the LTC shine may miss the integral <paramref name="reference"/>, in units of the face radiance: the fitted
        /// cosine's error measured over these cases on 2026-11-08, rounded up. Up to 0.6 rad off the normal it missed by at
        /// most 2.6 % plus 0.005. At 1.2 rad it overshot the peak by up to 10 % and fell short by up to 13 %, and lost up to
        /// 0.016 in GGX's longer tail, which the cosine does not have (ARCHITECTURE.md, "Rectangle lights").
        /// </summary>
        private static double ShineTolerance(double viewAngle, double reference) =>
            viewAngle <= 0.6 ? 0.006 + 0.03 * reference : 0.02 + 0.15 * reference;

        /// <summary>A panel 1.2 m square 2 m from the point along the mirror ray of <paramref name="toView"/>, facing the point.</summary>
        private static TestRect InTheMirror(Double3 toView)
        {
            Double3 mirror = Mirror(Double3.UnitY, toView);
            // The turn that takes the local front +Z to −mirror, about the axis across the plane of view.
            Vector3 back = -mirror.ToVector3();
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, back);
            float angle = MathF.Acos(Math.Clamp(back.Z, -1, 1));
            Quaternion rotation =
                axis.LengthSquared() > 1e-12f ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) : FaceDown;
            return Rect(center: 2 * mirror, rotation: rotation, width: 1.2, height: 1.2);
        }
    }

    [Fact]
    public void AMirrorShowsTheRectangleAtItsSize()
    {
        // A white metal mirror of roughness 0, normal +Y, under the cabin lamp 2 m above the origin, seen from the lamp's
        // center. Floor points along X show the lamp where their mirror ray meets its face. White metal reflects all of
        // its light, so the mirror shows the face radiance there.
        var mirror = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0, Reflectance: 0.5);
        var eye = new Double3(0, 2, 0);
        double Shown(double x)
        {
            var point = new Double3(x, 0, 0);
            TestRect rect = Rect(center: new Double3(0, 2, 0) - point, rotation: FaceDown, width: 1.2, height: 1.2);
            return mirror
                .RectTerms(
                    normal: Double3.UnitY,
                    toView: (eye - point).Normalized(),
                    rect: rect.View,
                    tables: BrdfTables.Shared,
                    ltc: LtcTable.Shared
                )
                .Specular.X;
        }

        // The eye looks straight down from the lamp's plane, so the mirror ray from x meets the lamp's plane at 2x: the
        // image's edge lies at x = 0.3, half the panel's half width.
        double inside = Shown(0.2),
            outside = Shown(0.4),
            edge = Shown(0.3);

        inside.Should().BeGreaterThan(0.95, "the mirror shows the panel's own face radiance inside its image");
        outside.Should().BeLessThan(0.05, "the mirror shows nothing beyond the panel's image");
        edge.Should().BeInRange(0.3, 0.7, "the image's edge lies where the mirror ray meets the panel's edge");
    }

    /// <summary>The rough floor's highlight against the integral, over a grid of 81 floor points (tests/STYLE.md, "Long tests").</summary>
    public sealed class RoughFloorEnergy
    {
        [Theory]
        [InlineData(4.0, 0.06)]
        [InlineData(1.5, 0.25)]
        public void ARoughFloorsHighlightKeepsTheEnergyThatTheIntegralGives(double eyeHeight, double tolerance)
        {
            // The cabin lamp 2 m above a rough white metal floor, seen from 3 m away and eyeHeight m up, so from about 40° and
            // from about 65° off the normal at the floor's middle. The highlight's sum over a grid of floor points 4 m square
            // keeps the energy of the lobe's integral over the panel: 4.6 % short seen from 4 m up, and 22.6 % short seen from
            // 1.5 m up, where the fitted cosine sends 7 % of its light below the horizon and lacks GGX's longer tail
            // (measured on 2026-11-08).
            var metal = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0.5, Reflectance: 0.5);
            var eye = new Double3(0, eyeHeight, 3);
            double shine = 0,
                reference = 0;
            for (int i = 0; i < 9; i++)
            for (int k = 0; k < 9; k++)
            {
                var point = new Double3(-2 + 0.5 * i, 0, -2 + 0.5 * k);
                TestRect rect = Rect(center: new Double3(0, 2, 0) - point, rotation: FaceDown, width: 1.2, height: 1.2);
                Double3 toView = (eye - point).Normalized();
                shine += metal
                    .RectTerms(normal: Double3.UnitY, toView: toView, rect: rect.View, tables: BrdfTables.Shared, ltc: LtcTable.Shared)
                    .Specular.X;
                reference += rect.ShineIntegral(metal, toView);
            }

            shine
                .Should()
                .BeApproximately(reference, tolerance * reference, "the highlight keeps the integral's energy {0}", reference);
        }
    }

    /// <summary>A rectangle relative to the shaded point, and the numerical integrals over its face.</summary>
    private sealed record TestRect(Double3 Center, Quaternion Rotation, double Width, double Height)
    {
        private Double3 HalfWidth => Vector3.Transform(Vector3.UnitX, Rotation) * (float)(Width / 2);

        private Double3 HalfHeight => Vector3.Transform(Vector3.UnitY, Rotation) * (float)(Height / 2);

        private Double3 Front => Vector3.Transform(Vector3.UnitZ, Rotation);

        public RectView View => new(toCenter: Center, halfWidth: HalfWidth, halfHeight: HalfHeight);

        /// <summary>
        /// The integral over the face, per unit face radiance, of cos θ_e / r² (its solid angle, in sr) and of
        /// max(n·s, 0) cos θ_e / r² (its irradiance on the normal <paramref name="normal"/>, in sr), with θ_e the angle at the
        /// face and s the unit direction toward it. A grid of 1000 × 1000 cells.
        /// </summary>
        public (double SolidAngle, double Irradiance) Integral(Double3 normal)
        {
            const int steps = 1000;
            double cell = Width * Height / (steps * steps);
            double solidAngle = 0,
                irradiance = 0;
            for (int i = 0; i < steps; i++)
            for (int j = 0; j < steps; j++)
            {
                Double3 place = PlaceAt((i + 0.5) / steps, (j + 0.5) / steps);
                double distanceSquared = place.LengthSquared;
                Double3 toPlace = place / Math.Sqrt(distanceSquared);
                double facing = -Double3.Dot(Front, toPlace);
                if (facing <= 0)
                    continue;
                solidAngle += facing * cell / distanceSquared;
                irradiance += Math.Max(Double3.Dot(normal, toPlace), 0) * facing * cell / distanceSquared;
            }
            return (solidAngle, irradiance);
        }

        /// <summary>
        /// The specular light that <paramref name="material"/> at the origin, normal +Y, sends toward
        /// <paramref name="toView"/> per unit face radiance: ∫ D Vis F c (n·l) dω over the face above the horizon, with c the
        /// energy compensation. The lobe's own samples, 2²⁰ Hammersley points, find the face for a sharp lobe, and a grid of
        /// 1000 × 1000 cells of the face for a wide one.
        /// </summary>
        public double ShineIntegral(MaterialSample material, Double3 toView)
        {
            GgxLobe lobe = material.Lobe;
            double nDotV = Math.Max(toView.Y, MaterialSample.MinViewCosine);
            double lobeAlbedo = BrdfTables.Shared.SampleDfg(nDotV: nDotV, roughness: lobe.Roughness).LobeAlbedo;
            double f0 = material.F0.X;
            double compensation = 1 + f0 * (1 / lobeAlbedo - 1);
            double Integrand(Double3 toLight)
            {
                Double3 half = (toLight + toView).Normalized();
                return lobe.Distribution(half.Y)
                    * lobe.Visibility(nDotL: toLight.Y, nDotV: nDotV)
                    * material.Fresnel(Double3.Dot(toView, half)).X
                    * toLight.Y;
            }

            if (lobe.Alpha >= 0.04)
            {
                const int steps = 1000;
                double cell = Width * Height / (steps * steps);
                double sum = 0;
                for (int i = 0; i < steps; i++)
                for (int j = 0; j < steps; j++)
                {
                    Double3 place = PlaceAt((i + 0.5) / steps, (j + 0.5) / steps);
                    double distanceSquared = place.LengthSquared;
                    Double3 toPlace = place / Math.Sqrt(distanceSquared);
                    double facing = -Double3.Dot(Front, toPlace);
                    if (facing <= 0 || toPlace.Y <= 0)
                        continue;
                    sum += Integrand(toPlace) * facing * cell / distanceSquared;
                }
                return sum * compensation;
            }

            const int samples = 1 << 20;
            var points = new Hammersley(samples);
            double hits = 0;
            for (int index = 0; index < samples; index++)
            {
                (double u1, double u2) = points[index];
                // The sample's half vector about +Z, turned to the floor's normal +Y.
                Double3 tangent = lobe.SampleHalfVector(u1: u1, u2: u2);
                var half = new Double3(tangent.X, tangent.Z, -tangent.Y);
                double vDotH = Double3.Dot(toView, half);
                Double3 toLight = 2 * vDotH * half - toView;
                if (toLight.Y <= 0 || vDotH <= 0 || !Hits(toLight))
                    continue;
                // The integrand over the sampling density D (n·h) / (4 v·h).
                hits += Integrand(toLight) * 4 * vDotH / (lobe.Distribution(half.Y) * half.Y);
            }
            return hits / samples * compensation;
        }

        /// <summary>The place of the face at the shares (<paramref name="u"/>, <paramref name="v"/>) of its width and height.</summary>
        private Double3 PlaceAt(double u, double v) =>
            Center + (2 * u - 1) * HalfWidth + (2 * v - 1) * HalfHeight;

        /// <summary>Whether the ray from the origin along <paramref name="direction"/> meets the face from its front.</summary>
        private bool Hits(Double3 direction)
        {
            double toward = Double3.Dot(direction, Front);
            if (toward >= 0)
                return false;
            Double3 hit = direction * (Double3.Dot(Center, Front) / toward) - Center;
            double across = Double3.Dot(hit, HalfWidth) / HalfWidth.LengthSquared,
                up = Double3.Dot(hit, HalfHeight) / HalfHeight.LengthSquared;
            return Math.Abs(across) <= 1 && Math.Abs(up) <= 1;
        }
    }
}
