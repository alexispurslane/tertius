using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The CPU boundary of the shaders (ARCHITECTURE.md, "Portable Vulkan"): every value that the renderer takes from the CPU
/// reaches a shader finite and in its range, or the thing that it belongs to is left out. No GPU needed. The camera sits
/// at the render origin and looks down −Z.
/// </summary>
public sealed class ShaderInputTests
{
    /// <summary>A box, and a mesh that reaches 10^21 m from its origin.</summary>
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f),
        new(FirstIndex: 36, IndexCount: 36, VertexOffset: 24, Radius: 1e21f),
    ];

    private static PreparedScene Prepare(RenderSnapshot snapshot)
    {
        var scene = new PreparedScene();
        new SceneBatcher().Prepare(
            snapshot: snapshot,
            alpha: 1f,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 1,
            environment: new WithoutMap(),
            scene: scene
        );
        return scene;
    }

    private static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);

    [Fact]
    public void ARadianceIsReadAsItsNearestValueFromZeroToTheLimitAndANaNAsZero()
    {
        ShaderInput
            .Radiance(new(float.NaN, float.NegativeInfinity, -2))
            .Should()
            .Be(Vector3.Zero, "a NaN and a negative radiance send no light");
        ShaderInput
            .Radiance(new(float.PositiveInfinity, 1e30f, 3))
            .Should()
            .Be(new Vector3(ShaderInput.RadianceLimit, ShaderInput.RadianceLimit, 3));
    }

    [Fact]
    public void AFractionIsReadAsItsNearestValueFromZeroToOneAndANaNAsZero()
    {
        ShaderInput.Fraction(float.NaN).Should().Be(0);
        ShaderInput.Fraction(-0.5f).Should().Be(0);
        ShaderInput.Fraction(0.25f).Should().Be(0.25f);
        ShaderInput.Fraction(float.PositiveInfinity).Should().Be(1);
    }

    [Fact]
    public void MorphWeightTurnsNaNIntoZero() =>
        ShaderInput.MorphWeight(float.NaN).Should().Be(0, "a NaN weight adds none of its target");

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(-3f, -3f)]
    [InlineData(1024f, 1024f)]
    [InlineData(2000f, 1024f)]
    [InlineData(float.PositiveInfinity, 1024f)]
    [InlineData(-2000f, -1024f)]
    [InlineData(float.NegativeInfinity, -1024f)]
    public void MorphWeightClampsToTheLargestMorphWeight(float weight, float read) =>
        ShaderInput.MorphWeight(weight).Should().Be(read);

    [Fact]
    public void ALightWithANumberThatIsNotFiniteIsLeftOut()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        Vector3 ahead = new(0, 0, -5);
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: ahead,
                Color: Vector3.One,
                Intensity: float.NaN,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: ahead,
                Color: new(float.NaN),
                Intensity: 1,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: ahead,
                Color: Vector3.One,
                Intensity: 1,
                Range: float.PositiveInfinity,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: new(float.NaN),
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        foreach (
            (Vector3 direction, float inner, float outer) in new[]
            {
                (new Vector3(float.PositiveInfinity, -1, 0), 0.3f, 0.5f),
                (-Vector3.UnitY, float.NaN, 0.5f),
                (-Vector3.UnitY, 0.3f, float.NaN),
            }
        )
            snapshot.Lights.Add(
                new SpotRenderLight(
                    Position: ahead,
                    Direction: direction,
                    Color: Vector3.One,
                    Intensity: 1,
                    Range: 10,
                    InnerConeAngle: inner,
                    OuterConeAngle: outer,
                    ChainDepth: TestChains.CamerasFrame
                )
            );
        // One that draws, to show that the others were left out and not everything.
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: ahead,
                Color: Vector3.One,
                Intensity: 2,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot);

        scene.LightCount.Should().Be(1, "only the last light holds finite numbers");
        scene.Lights[0].RadianceKind.X.Should().Be(2);
    }

    [Fact]
    public void ALightsRadianceReachesTheShaderInsideTheRadianceRange()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: new(0, 0, -5),
                Color: new(1, -1, 1e30f),
                Intensity: 1e10f,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot);

        Rgb(scene.Lights[0].RadianceKind)
            .Should()
            .Be(
                new Vector3(1e10f, 0, ShaderInput.RadianceLimit),
                "a negative channel sends no light, and a product past the limit shines at the limit"
            );
    }

    [Fact]
    public void TheSunAndTheAmbientLightReachTheShaderInsideTheRadianceRange()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Sun.Color = new(float.NaN, 1e30f, 1);
        snapshot.Sun.Intensity = 2;
        snapshot.Sun.AngularRadius = float.NaN;
        snapshot.Ambient = new()
        {
            SkyColor = new(float.PositiveInfinity, 1, 1),
            GroundColor = new(float.NaN, 1, -1),
            Intensity = 0.5f,
        };

        GpuFrameConstants constants = Prepare(snapshot).Constants;

        Rgb(constants.SunRadiance).Should().Be(new Vector3(0, ShaderInput.RadianceLimit, 2));
        constants.SunDirection.W.Should().Be(0, "an angular radius that is not finite is a point sun");
        Rgb(constants.AmbientSky).Should().Be(new Vector3(ShaderInput.RadianceLimit, 0.5f, 0.5f));
        Rgb(constants.AmbientGround).Should().Be(new Vector3(0, 0.5f, 0));
    }

    [Fact]
    public void APuffsLightReachesTheShaderInsideItsRange()
    {
        // At age 0.5 of a life of 2, a puff keeps 3/4 of its own share.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.PuffLooks.Add(Look(PuffShading.Glow(new(float.NegativeInfinity, float.NaN, 1e30f))));
        snapshot.PuffLooks.Add(Look(PuffShading.Lit(new(2, float.NaN, 0.5f))));
        snapshot.Puffs.Add(Puff(look: 0));
        snapshot.Puffs.Add(Puff(look: 1));

        PreparedScene scene = Prepare(snapshot);

        scene.PuffCount.Should().Be(2);
        Rgb(scene.Puffs[0].ColorKind)
            .Should()
            .Be(new Vector3(0, 0, ShaderInput.RadianceLimit), "a glow puff's light is a radiance");
        Rgb(scene.Puffs[1].ColorKind)
            .Should()
            .Be(
                new Vector3(0.75f, 0, 0.375f),
                "a lit puff's color is an albedo, a fraction of the light that reaches it"
            );
    }

    [Fact]
    public void APuffWhoseRadiusIsBelowTheSmallestNormalFloatIsLeftOut()
    {
        // A GPU may flush such a radius to 0, and the puff's fades divide by it.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.PuffLooks.Add(Look(PuffShading.Glow(Vector3.One)) with { StartRadius = 1e-39f, EndRadius = 1e-39f });
        snapshot.PuffLooks.Add(Look(PuffShading.Glow(Vector3.One)) with { StartRadius = 1e-37f, EndRadius = 1e-37f });
        snapshot.Puffs.Add(Puff(look: 0));
        snapshot.Puffs.Add(Puff(look: 1));

        PreparedScene scene = Prepare(snapshot);

        scene.PuffCount.Should().Be(1);
        scene.Puffs[0].PositionRadius.W.Should().Be(1e-37f);
    }

    [Theory]
    [InlineData(0, 16128f)]
    [InlineData(1, 16128f)]
    [InlineData(4096, 3.9375f)]
    public void EachPuffOfAFrameMayAddAnEqualShareOfThePuffsBudget(int puffs, float share) =>
        // The puffs over one pixel add in the blend, so together they may add at most their budget, half the surfaces'
        // cap, whatever their number. The other half takes the rounding of their blends.
        ShaderInput.PuffRadianceCap(puffs).Should().Be(share, "{0} puffs share the budget of 16,128", puffs);

    [Fact]
    public void TheFrameGivesTheShadersTheShareOfEachPuffThatItDraws()
    {
        // The puff behind the eye is culled, so it takes no share.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.PuffLooks.Add(Look(PuffShading.Glow(Vector3.One)));
        snapshot.Puffs.Add(Puff(look: 0));
        snapshot.Puffs.Add(Puff(look: 0));
        snapshot.Puffs.Add(Puff(look: 0) with { Position = new(0, 0, 10), PreviousPosition = new(0, 0, 10) });

        PreparedScene scene = Prepare(snapshot);

        scene.PuffCount.Should().Be(2);
        scene.Constants.Params.X.Should().Be(ShaderInput.PuffBudget / 2, "the two puffs drawn share the budget");
    }

    [Theory]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(1e-20f, false)] // 1 / scale² is past the largest single float
    [InlineData(1e20f, false)] // 1 / scale² is below the smallest normal single float
    [InlineData(1e-15f, true)]
    [InlineData(1e15f, true)]
    public void AnInstanceIsDrawnOnlyWhenEachAxisOfItsScaleIsFiniteAndInsideTheDrawableRange(float z, bool drawn)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -10), scale: new(1, 1, z), radius: 1)
        );

        Prepare(snapshot).VisibleCount.Should().Be(drawn ? 1 : 0);
    }

    [Fact]
    public void AnInstanceWhoseMeshScaledIsPastTheFloatRangeIsLeftOut()
    {
        // The bounding radius is set, so only the mesh's own extent times the scale, 10^39 m, leaves the float range.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(1), material: new(0), position: new(0, 0, -10), scale: new(1e18f), radius: 1)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -10), scale: new(1e18f), radius: 1)
        );

        Prepare(snapshot).VisibleCount.Should().Be(1, "the box scaled by 10^18 stays inside the float range");
    }

    [Fact]
    public void AQuaternionWithANumberThatIsNotFiniteCountsAsUnset()
    {
        var infinite = new Quaternion(x: float.PositiveInfinity, y: 0, z: 0, w: 1);
        var huge = new Quaternion(x: 3e38f, y: 3e38f, z: 0, w: 0);

        SceneBatcher.Interpolate(previous: infinite, current: infinite, alpha: 1).Should().Be(Quaternion.Identity);
        SceneBatcher
            .Interpolate(previous: Quaternion.Identity, current: huge, alpha: 1)
            .Should()
            .Be(Quaternion.Identity, "the squared length of the quaternion overflows, so it cannot be normalized");
    }

    [Fact]
    public void AHemisphereWithANumberThatIsNotFiniteMixesTheSkyAndTheGroundEvenly()
    {
        var along = GpuInstance.From(
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            material: 0,
            map: 0,
            hemisphere: HemisphereUp.Along(new(float.NaN, 1, 0))
        );
        var away = GpuInstance.From(
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            material: 0,
            map: 0,
            hemisphere: HemisphereUp.AwayFrom(new(float.PositiveInfinity, 0, 0))
        );

        along.Hemisphere.Should().Be(Vector4.Zero);
        away.Hemisphere.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void AnOutlineWhoseRotationIsNotFiniteIsLeftOut()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        Vector3 ahead = new(0, 0, -20);
        snapshot.Outlines.Add(new SemanticOutline(ahead, new(x: float.NaN, y: 0, z: 0, w: 1), new(1, 2, 3)));
        snapshot.Outlines.Add(new SemanticOutline(ahead, Quaternion.Identity, new(1, 2, 3)));
        PreparedScene scene = Prepare(snapshot);

        OutlinePass.Collect(snapshot, scene);

        scene.OutlineCount.Should().Be(1);
    }

    [Fact]
    public void AMaterialReachesTheShaderWithItsColorsAndFactorsInTheirRanges()
    {
        GpuMaterial material = MaterialStore.ToGpu(
            new(
                BaseColor: new(x: float.NaN, y: 2, z: -1, w: float.PositiveInfinity),
                Roughness: float.NaN,
                Metallic: float.NaN,
                Reflectance: float.NaN,
                Emissive: new(float.PositiveInfinity, float.NaN, 1)
            )
        );

        material.BaseColor.Should().Be(new Vector4(x: 0, y: 1, z: 0, w: 1), "a base color is a fraction of the light");
        material.Emissive.Should().Be(new Vector3(ShaderInput.RadianceLimit, 0, 1), "an emission is a radiance");
        material.Roughness.Should().Be(0);
        material.Metallic.Should().Be(0);
        material.Reflectance.Should().Be(0);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void AMeshWithANumberThatIsNotFiniteIsRefused(float position, float texCoord)
    {
        var queue = new UploadQueue(maxTextureSize: 64, deformBytesLimit: 1L << 27);
        MeshData box = Primitives.Box(Vector3.One);
        Vertex[] vertices = [.. box.Vertices];
        vertices[0].Position.X = position;
        vertices[1].TexCoord.Y = texCoord;

        FluentActions
            .Invoking(() => queue.AddMesh(new(vertices, box.Indices)))
            .Should()
            .ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void AMeshWithANormalOrTangentThatIsNotFiniteIsRefused()
    {
        var queue = new UploadQueue(maxTextureSize: 64, deformBytesLimit: 1L << 27);
        MeshData box = Primitives.Box(Vector3.One);
        Vertex[] normal = [.. box.Vertices];
        normal[0].Normal.Y = float.NaN;
        Vertex[] tangent = [.. box.Vertices];
        tangent[0].Tangent.W = float.NegativeInfinity;

        FluentActions
            .Invoking(() => queue.AddMesh(new(normal, box.Indices)))
            .Should()
            .ThrowExactly<ArgumentException>();
        FluentActions
            .Invoking(() => queue.AddMesh(new(tangent, box.Indices)))
            .Should()
            .ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void TheGpuGetsEachVertexNormalAndTangentAtUnitLengthOrZero()
    {
        var vertex = new Vertex
        {
            Position = new(1, 2, 3),
            Normal = new(0, 3e30f, 0),
            Tangent = new(x: 0, y: 0, z: 1e-30f, w: -1),
            TexCoord = new(0.5f, 0.25f),
        };

        Vertex staged = GeometryStore.Staged(vertex);
        Vertex flat = GeometryStore.Staged(vertex with { Normal = Vector3.Zero });

        staged.Normal.Should().Be(Vector3.UnitY);
        staged.Tangent.Should().Be(new Vector4(x: 0, y: 0, z: 1, w: -1), "the tangent's w is a sign, not a direction");
        staged.Position.Should().Be(vertex.Position);
        staged.TexCoord.Should().Be(vertex.TexCoord);
        flat.Normal.Should().Be(Vector3.Zero, "a normal of length 0 has no direction, and the shader gives it one");
    }

    [Fact]
    public void ALightRangePastTheLimitReachesTheShaderAtTheLimit()
    {
        // The shaders square the range, and float.MaxValue squared is infinite.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: new(0, 0, -5),
                Color: Vector3.One,
                Intensity: 1,
                Range: float.MaxValue,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            new SpotRenderLight(
                Position: new(0, 0, -5),
                Direction: -Vector3.UnitY,
                Color: Vector3.One,
                Intensity: 1,
                Range: 1e30f,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f,
                ChainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot);

        scene.LightCount.Should().Be(2);
        scene.Lights[0].PositionRange.W.Should().Be(ShaderInput.RangeLimit, "a point light reaches every drawn point");
        scene.Lights[1].PositionRange.W.Should().Be(ShaderInput.RangeLimit, "a spot light reaches every drawn point");
    }

    [Fact]
    public void AnInstanceThatReachesPastThePositionLimitIsLeftOut()
    {
        // The mesh's extent, 10^21 m, is finite, but the shaders square the distance of its surface from the eye. The box
        // reaches just short of the limit.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(1), material: new(0), position: new(0, 0, -10), scale: Vector3.One, radius: 1)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -4e18f), scale: Vector3.One, radius: 1)
        );

        PreparedScene scene = Prepare(snapshot);

        scene.VisibleCount.Should().Be(1);
        scene.VisibleSpan[0].Position.Z.Should().Be(-4e18f);
    }

    [Fact]
    public void APuffPastThePositionLimitIsLeftOut()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.PuffLooks.Add(Look(PuffShading.Glow(Vector3.One)));
        snapshot.Puffs.Add(Puff(look: 0) with { Position = new(0, 0, -1e19f), PreviousPosition = new(0, 0, -1e19f) });
        snapshot.Puffs.Add(Puff(look: 0));

        PreparedScene scene = Prepare(snapshot);

        scene.PuffCount.Should().Be(1);
        scene.Puffs[0].PositionRadius.Z.Should().Be(-10);
    }

    [Fact]
    public void ALightPastThePositionLimitIsLeftOut()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: new(0, 0, -1e19f),
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            new PointRenderLight(
                Position: new(0, 0, -5),
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                ChainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot);

        scene.LightCount.Should().Be(1);
        scene.Lights[0].PositionRange.Z.Should().Be(-5);
    }

    [Fact]
    public void AnOutlinePastThePositionLimitIsLeftOut()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Outlines.Add(new FallingOutline(new(0, 0, -1e19f), 1));
        snapshot.Outlines.Add(new FallingOutline(new(0, 0, -20), 1));
        PreparedScene scene = Prepare(snapshot);

        OutlinePass.Collect(snapshot, scene);

        scene.OutlineCount.Should().Be(1);
    }

    [Fact]
    public void AFarShadowPastThePositionLimitIsLeftOut()
    {
        // Its radius is a tenth of its distance, far over a pixel, so only the limit leaves it out.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -1e19f),
                scale: Vector3.One,
                flags: RenderFlags.CastsShadow,
                radius: 1e18f
            )
        );

        Prepare(snapshot).OccluderCount.Should().Be(0);
    }

    [Fact]
    public void ACameraWithAFieldOfViewNearZeroGivesTheShadersAxesThatHaveADirection()
    {
        // The shaders normalize the camera's up and right axes, of length tan(fov / 2) and that times the aspect. Their
        // squares must stay normal single floats, which a GPU does not flush to 0.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Camera = snapshot.Camera with { VerticalFov = 1e-19f };

        GpuFrameConstants constants = Prepare(snapshot).Constants;

        Rgb(constants.CameraUp).LengthSquared().Should().BeGreaterThanOrEqualTo(ShaderInput.SmallestNormal);
        Rgb(constants.CameraRight).LengthSquared().Should().BeGreaterThanOrEqualTo(ShaderInput.SmallestNormal);
    }

    /// <summary>A look of life 2 s and radius 1 m.</summary>
    private static PuffLook Look(PuffShading shading) =>
        new(Life: 2, StartRadius: 1, EndRadius: 1, TemperatureRatio: 1, Shading: shading);

    private static RenderPuff Puff(int look) =>
        new()
        {
            Position = new(0, 0, -10),
            PreviousPosition = new(0, 0, -10),
            Age = 0.5f,
            Look = look,
        };
}
