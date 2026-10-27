using System.Collections.Concurrent;
using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class ResourceTests
{
    [Fact]
    public void CreateMethodsAreThreadSafeAndUploadOnTheNextFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);
        try
        {
            var meshes = new ConcurrentBag<MeshId>();
            var textures = new ConcurrentBag<TextureId>();
            var materials = new ConcurrentBag<MaterialId>();
            Parallel.For(
                0,
                64,
                i =>
                {
                    meshes.Add(
                        renderer.CreateMesh(
                            i % 2 == 0 ? Primitives.Box(new(0.5f + i)) : Primitives.Sphere(1f, 8 + i % 5, 6)
                        )
                    );
                    TextureId texture = renderer.CreateTexture(
                        TestTextures.Checker(size: 16, tiles: 2, a: ((byte)i, 0, 0), b: (0, (byte)i, 0))
                    );
                    textures.Add(texture);
                    materials.Add(
                        renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: texture))
                    );
                }
            );

            // Ids are dense from 0, distinct, and never None: the defaults live in slots no id names.
            meshes.Select(m => m.Value).Order().Should().Equal(Enumerable.Range(0, 64));
            textures.Select(t => t.Value).Order().Should().Equal(Enumerable.Range(0, 64));
            materials.Select(m => m.Value).Order().Should().Equal(Enumerable.Range(0, 64));
            meshes.Should().NotContain(MeshId.None);
            textures.Should().NotContain(TextureId.None);
            materials.Should().NotContain(MaterialId.None);

            renderer.MeshCount.Should().Be(0, "uploads wait for the next frame");
            renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);
            renderer.MeshCount.Should().Be(64);
            renderer.TextureCount.Should().Be(64);
            renderer.MaterialCount.Should().Be(64);

            // A frame that uploads only a mesh leaves the textures and materials as they are.
            renderer.CreateMesh(Primitives.Box(Vector3.One)).Value.Should().Be(64);
            renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);
            (renderer.MeshCount, renderer.TextureCount, renderer.MaterialCount).Should().Be((65, 64, 64));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void InvalidResourcesAreRejected()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 64, height: 64);
        try
        {
            FluentActions
                .Invoking(() => renderer.CreateMesh(new([new()], [0, 0, 1])))
                .Should()
                .ThrowExactly<ArgumentException>("index 1 is out of range");
            FluentActions
                .Invoking(() => renderer.CreateMesh(new([new()], [0, 0])))
                .Should()
                .ThrowExactly<ArgumentException>("two indices are not a triangle");
            FluentActions
                .Invoking(() => renderer.CreateMesh(new([], [])))
                .Should()
                .ThrowExactly<ArgumentException>("a mesh needs vertices");
            FluentActions
                .Invoking(() => renderer.CreateTexture(new(Width: 2, Height: 2, Rgba: new byte[15])))
                .Should()
                .ThrowExactly<ArgumentException>("2 × 2 RGBA8 is 16 bytes");
            FluentActions
                .Invoking(() => renderer.CreateTexture(new(Width: 0, Height: 2, Rgba: [])))
                .Should()
                .ThrowExactly<ArgumentOutOfRangeException>();
            // A material may name only None or textures this renderer created. Texture 0 does not exist yet, and a
            // negative id other than None is never handed out.
            FluentActions
                .Invoking(() =>
                    renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: new(500)))
                )
                .Should()
                .ThrowExactly<ArgumentException>();
            FluentActions
                .Invoking(() =>
                    renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, NormalMap: new(0)))
                )
                .Should()
                .ThrowExactly<ArgumentException>();
            FluentActions
                .Invoking(() =>
                    renderer.CreateMaterial(
                        new(BaseColor: Vector4.One, Roughness: 0.38f, MetallicRoughnessMap: new(-3))
                    )
                )
                .Should()
                .ThrowExactly<ArgumentException>();
            renderer
                .CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: TextureId.None))
                .IsNone.Should()
                .BeFalse();
            TextureId first = renderer.CreateTexture(
                TestTextures.Checker(size: 4, tiles: 2, a: (255, 0, 0), b: (0, 255, 0))
            );
            first.Should().Be(new TextureId(0));
            renderer
                .CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, NormalMap: first))
                .Value.Should()
                .Be(1);
            FluentActions
                .Invoking(() => renderer.Resize(0, 10))
                .Should()
                .ThrowExactly<ArgumentOutOfRangeException>("a headless renderer needs a positive size");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }

        FluentActions
            .Invoking(() => renderer.CreateMesh(Primitives.Box(Vector3.One)))
            .Should()
            .ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => renderer.Render(new(), 1f)).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void TheBindlessArrayHoldsItsCapacityBesidesTheDefaults()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);
        try
        {
            const int width = 160,
                height = 90;
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            // Magenta everywhere but in the last slot, which is green.
            for (int i = 0; i < TextureStore.UserCapacity - 1; i++)
                renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [255, 0, 255, 255]));
            TextureId green = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [0, 255, 0, 255]));
            green.Value.Should().Be(TextureStore.UserCapacity - 1);
            TextureStore
                .Slot(green)
                .Should()
                .Be(TextureStore.Capacity - 1, "the last texture takes the last slot of the array");
            FluentActions
                .Invoking(() => renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [0, 0, 0, 255])))
                .Should()
                .ThrowExactly<InvalidOperationException>();

            // The texture in the array's last slot draws. A reflectance of 0 gives an F0 of 0, so no white highlight
            // washes the green out: the sun is straight behind the camera.
            MaterialId material = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.38f, Reflectance: 0f, BaseColorMap: green)
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0, 0, 1));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: material,
                    position: new(0, 0, -4),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "texture-array-last-slot"
            );
            renderer.TextureCount.Should().Be(TextureStore.UserCapacity);
            Vector3 color = image.Color(width / 2, height / 2);
            color.Y.Should().BeGreaterThan(0.3f, "the box is lit");
            // Every other slot is magenta. ACES (with its RRT saturation) mixes channels, so pure green displays with
            // some red and blue, but green stays far ahead.
            color
                .Y.Should()
                .BeGreaterThan(color.X + 0.3f, "the box shows the green texture, not a magenta one: {0}", color)
                .And.BeGreaterThan(color.Z + 0.3f, "the box shows the green texture, not a magenta one: {0}", color);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void GlobalBuffersGrowWhileFramesAreInFlight()
    {
        const int width = 320,
            height = 180;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, framesInFlight: 3);
        try
        {
            MeshId first = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId green = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.1f, y: 0.8f, z: 0.2f, w: 1), roughness: 0.38f)
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.2f, 0.5f, 1f));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: first,
                    material: green,
                    position: new(-1.5f, 0, -6),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);

            // About 200,000 vertices and 1,200,000 indices: both global buffers must grow (past 64 Ki vertices and
            // 256 Ki indices), and 300 more materials grow the material buffer, while earlier frames may still run.
            MeshId last = MeshId.None;
            for (int i = 0; i < 12; i++)
                last = renderer.CreateMesh(Primitives.Sphere(1f, 128, 128));
            MaterialId lastMaterial = MaterialId.None;
            for (int i = 0; i < 300; i++)
                lastMaterial = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.9f, y: 0.1f + i / 1000f, z: 0.1f, w: 1), roughness: 0.38f)
                );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: last,
                    material: lastMaterial,
                    position: new(1.5f, 0, -6),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            // Not captured: a capture waits until its frame has finished, which would release the old buffers at once.
            renderer.Render(snapshot, 1f);
            renderer.PendingReleases.Should().BePositive("the replaced buffers wait for the frames that use them");
            for (int i = 0; i < 4; i++)
                renderer.Render(snapshot, 1f);
            renderer.PendingReleases.Should().Be(0, "every frame that used the old buffers has finished");
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "buffer-growth");

            // The first mesh survived the copy into the larger buffers, and the new mesh draws with the new material.
            Vector3 left = image.Color(Gpu.Project(point: new(-1.5f, 0, -5.5f), width: width, height: height));
            Vector3 right = image.Color(Gpu.Project(point: new(1.5f, 0, -5.2f), width: width, height: height));
            left.Y.Should().BeGreaterThan(left.X, "the box is green").And.BeGreaterThan(left.Z);
            right.X.Should().BeGreaterThan(right.Y, "the sphere is red").And.BeGreaterThan(right.Z);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void NoneMeansTheDefaultTextureOrMaterialAndIdZeroIsARealResource()
    {
        const int width = 160,
            height = 90;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            // The first texture, material and mesh all get id 0, and none of them is None.
            TextureId red = renderer.CreateTexture(
                TestTextures.Checker(size: 8, tiles: 2, a: (255, 0, 0), b: (255, 0, 0))
            );
            MaterialId gray = renderer.CreateMaterial(
                new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 0.38f)
            );
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            (red.Value, gray.Value, box.Value).Should().Be((0, 0, 0));
            red.IsNone.Should().BeFalse();
            gray.IsNone.Should().BeFalse();
            box.IsNone.Should().BeFalse();
            // A material whose texture slots are left at None uses the default textures, not texture 0.
            MaterialId explicitNone = renderer.CreateMaterial(
                new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 0.38f, BaseColorMap: TextureId.None)
            );
            // A reflectance of 0 here, so that the sun straight behind the camera leaves the texture's red unwashed.
            MaterialId textured = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1),
                    Roughness: 0.38f,
                    Reflectance: 0f,
                    BaseColorMap: red
                )
            );

            RenderSnapshot snapshot = Gpu.Snapshot(new(0, 0, 1));
            var places = new (Vector3 Position, MaterialId Material, bool Gray)[]
            {
                (new(-1.2f, 0, -5), gray, true),
                (new(1.2f, 0, -5), textured, false),
                (new(-1.2f, 1.2f, -5), explicitNone, true),
                // None and an id this renderer never handed out both draw with the default white material.
                (new(1.2f, 1.2f, -5), MaterialId.None, true),
                (new(0, -1.2f, -5), new(9999), true),
            };
            foreach ((Vector3 position, MaterialId material, _) in places)
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: material,
                        position: position,
                        scale: Vector3.One,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            // MeshId.None draws nothing.
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: MeshId.None,
                    material: gray,
                    position: new(0, 0, -3),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "default-textures");
            renderer.LastFrameStats.SubmittedInstances.Should().Be(6);
            renderer.LastFrameStats.VisibleInstances.Should().Be(5, "an instance of MeshId.None is not drawn");
            foreach ((Vector3 position, MaterialId material, bool expectGray) in places)
            {
                Vector3 color = image.Color(
                    Gpu.Project(point: position + new Vector3(0, 0, 0.5f), width: width, height: height)
                );
                if (expectGray)
                {
                    color.X.Should().BeGreaterThan(0.4f, "{0} at {1} is lit", material, position);
                    MathF
                        .Abs(color.X - color.Y)
                        .Should()
                        .BeLessThan(0.03f, "{0} at {1} is gray: {2}", material, position, color);
                    MathF
                        .Abs(color.Y - color.Z)
                        .Should()
                        .BeLessThan(0.03f, "{0} at {1} is gray: {2}", material, position, color);
                }
                else
                {
                    color.X.Should().BeGreaterThan(2 * color.Y, "texture 0 is red: {0}", color);
                }
            }

            // Nothing is drawn where the None mesh would be, in front of the others: the center shows the sky.
            image.MeanLuma(new(width / 2f, height / 2f), 1).Should().BeLessThan(0.1f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
