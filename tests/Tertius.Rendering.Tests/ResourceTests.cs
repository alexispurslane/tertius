using System.Collections.Concurrent;
using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class ResourceTests
{
    [Fact]
    public void CreateMethodsAreThreadSafeAndUploadOnTheNextFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(160, 90);
        try
        {
            var meshes = new ConcurrentBag<MeshId>();
            var textures = new ConcurrentBag<TextureId>();
            var materials = new ConcurrentBag<MaterialId>();
            Parallel.For(0, 64, i =>
            {
                meshes.Add(renderer.CreateMesh(i % 2 == 0 ? Primitives.Box(new Vector3(0.5f + i)) : Primitives.Sphere(1f, 8 + i % 5, 6)));
                TextureId texture = renderer.CreateTexture(TestTextures.Checker(16, 2, ((byte)i, 0, 0), (0, (byte)i, 0)));
                textures.Add(texture);
                materials.Add(renderer.CreateMaterial(new MaterialDesc(Vector4.One, Albedo: texture)));
            });

            Assert.Equal(64, meshes.Select(m => m.Value).Distinct().Count());
            Assert.Equal(64, textures.Select(t => t.Value).Distinct().Count());
            Assert.Equal(64, materials.Select(m => m.Value).Distinct().Count());
            // Ids are dense. Texture and material ids start after the built-in defaults.
            Assert.Equal(Enumerable.Range(0, 64), meshes.Select(m => m.Value).Order());
            Assert.Equal(Enumerable.Range(TextureStore.FirstUserIndex, 64), textures.Select(t => t.Value).Order());
            Assert.Equal(Enumerable.Range(MaterialStore.FirstUserIndex, 64), materials.Select(m => m.Value).Order());

            Assert.Equal(0, renderer.MeshCount);
            renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);
            Assert.Equal(64, renderer.MeshCount);
            Assert.Equal(TextureStore.FirstUserIndex + 64, renderer.TextureCount);
            Assert.Equal(MaterialStore.FirstUserIndex + 64, renderer.MaterialCount);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void InvalidResourcesAreRejected()
    {
        VulkanRenderer renderer = Gpu.Headless(64, 64);
        try
        {
            Assert.Throws<ArgumentException>(() => renderer.CreateMesh(new MeshData([new Vertex()], [0, 0, 1])));
            Assert.Throws<ArgumentException>(() => renderer.CreateMesh(new MeshData([new Vertex()], [0, 0])));
            Assert.Throws<ArgumentException>(() => renderer.CreateMesh(new MeshData([], [])));
            Assert.Throws<ArgumentException>(() => renderer.CreateTexture(new TextureData(2, 2, new byte[15])));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.CreateTexture(new TextureData(0, 2, [])));
            Assert.Throws<ArgumentException>(() => renderer.CreateMaterial(new MaterialDesc(Vector4.One, Albedo: new TextureId(500))));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Resize(0, 10));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
        Assert.Throws<ObjectDisposedException>(() => renderer.CreateMesh(Primitives.Box(Vector3.One)));
        Assert.Throws<ObjectDisposedException>(() => renderer.Render(new RenderSnapshot(), 1f));
    }

    [Fact]
    public void GlobalBuffersGrowWhileFramesAreInFlight()
    {
        const int width = 320, height = 180;
        VulkanRenderer renderer = Gpu.Headless(width, height, framesInFlight: 3);
        try
        {
            MeshId first = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId green = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.1f, 0.8f, 0.2f, 1)));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.2f, 0.5f, 1f));
            snapshot.Instances.Add(Gpu.Instance(first, green, new Vector3(-1.5f, 0, -6), Vector3.One));
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);

            // About 200,000 vertices and 1,200,000 indices: both global buffers must grow (past 64 Ki vertices and
            // 256 Ki indices), and 300 more materials grow the material buffer, while earlier frames may still run.
            MeshId last = default;
            for (int i = 0; i < 12; i++)
                last = renderer.CreateMesh(Primitives.Sphere(1f, 128, 128));
            MaterialId lastMaterial = default;
            for (int i = 0; i < 300; i++)
                lastMaterial = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.9f, 0.1f + i / 1000f, 0.1f, 1)));
            snapshot.Instances.Add(Gpu.Instance(last, lastMaterial, new Vector3(1.5f, 0, -6), Vector3.One));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "buffer-growth");
            Assert.True(renderer.PendingReleases > 0, "the replaced buffers wait for the frames that use them");
            for (int i = 0; i < 4; i++)
                renderer.Render(snapshot, 1f);
            Assert.Equal(0, renderer.PendingReleases);

            // The first mesh survived the copy into the larger buffers, and the new mesh draws with the new material.
            Vector3 left = image.Color(Gpu.Project(new Vector3(-1.5f, 0, -5.5f), width, height));
            Vector3 right = image.Color(Gpu.Project(new Vector3(1.5f, 0, -5.2f), width, height));
            Assert.True(left.Y > left.X && left.Y > left.Z, $"green box {left}");
            Assert.True(right.X > right.Y && right.X > right.Z, $"red sphere {right}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void DefaultTextureIdsMeanDefaultTextures()
    {
        const int width = 160, height = 90;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            // A real texture exists at the first user index, but default(TextureId) and None must not pick it up.
            renderer.CreateTexture(TestTextures.Checker(8, 2, (255, 0, 0), (255, 0, 0)));
            MaterialId byDefault = renderer.CreateMaterial(new MaterialDesc(new Vector4(0.8f, 0.8f, 0.8f, 1)));
            MaterialId byNone = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1)));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0, 0, 1));
            snapshot.Instances.Add(Gpu.Instance(box, byDefault, new Vector3(-1f, 0, -5), Vector3.One));
            snapshot.Instances.Add(Gpu.Instance(box, byNone, new Vector3(1f, 0, -5), Vector3.One));
            // An unknown material id falls back to the default white material.
            snapshot.Instances.Add(Gpu.Instance(box, new MaterialId(9999), new Vector3(0, 1.2f, -5), Vector3.One));
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "default-textures");
            foreach (Vector3 position in new[] { new Vector3(-1f, 0, -4.5f), new Vector3(1f, 0, -4.5f), new Vector3(0, 1.2f, -4.5f) })
            {
                Vector3 color = image.Color(Gpu.Project(position, width, height));
                Assert.True(color.X > 0.4f && MathF.Abs(color.X - color.Y) < 0.03f && MathF.Abs(color.Y - color.Z) < 0.03f, $"gray expected at {position}: {color}");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
