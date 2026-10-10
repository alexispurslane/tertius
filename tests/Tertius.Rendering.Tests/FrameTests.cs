using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class FrameTests
{
    private static RenderSnapshot LitFromBehind() => Gpu.Snapshot(new Vector3(0.3f, 0.6f, 1f));

    [Fact]
    public void AlphaInterpolatesInstancesBetweenPreviousAndCurrentTransforms()
    {
        const int width = 640, height = 360;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.9f, 0.9f, 0.9f, 1)));
            RenderSnapshot snapshot = LitFromBehind();
            var left = new Vector3(-2.5f, 0, -8);
            var right = new Vector3(2.5f, 0, -8);
            RenderInstance instance = Gpu.Instance(box, white, right, Vector3.One, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4));
            instance.PreviousPosition = left;
            instance.PreviousRotation = Quaternion.Identity;
            snapshot.Instances.Add(instance);

            Capture start = Gpu.RenderAndCapture(renderer, snapshot, "interpolation-alpha0", alpha: 0f);
            Capture middle = Gpu.RenderAndCapture(renderer, snapshot, "interpolation-alpha05", alpha: 0.5f);
            Capture end = Gpu.RenderAndCapture(renderer, snapshot, "interpolation-alpha1", alpha: 1f);
            Vector2 leftPixel = Gpu.Project(left, width, height);
            Vector2 centerPixel = Gpu.Project((left + right) / 2, width, height);
            Vector2 rightPixel = Gpu.Project(right, width, height);

            start.Luma(leftPixel).Should().BeGreaterThan(0.3f, "alpha 0 draws the previous position");
            start.Luma(rightPixel).Should().BeLessThan(0.1f, "alpha 0 draws nothing at the current position");
            middle.Luma(centerPixel).Should().BeGreaterThan(0.3f, "alpha 0.5 draws the midpoint");
            middle.Luma(leftPixel).Should().BeLessThan(0.1f, "alpha 0.5 draws nothing at the previous position");
            middle.Luma(rightPixel).Should().BeLessThan(0.1f, "alpha 0.5 draws nothing at the current position");
            end.Luma(rightPixel).Should().BeGreaterThan(0.3f, "alpha 1 draws the current position");
            end.Luma(leftPixel).Should().BeLessThan(0.1f, "alpha 1 draws nothing at the previous position");

            // The rotation is interpolated too: a centered box turning 45 degrees about Y shows only its front face
            // at alpha 0 and two faces, about √2 times wider, at alpha 1.
            RenderSnapshot turning = LitFromBehind();
            RenderInstance spinner = Gpu.Instance(box, white, new Vector3(0, 0, -8), Vector3.One, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4));
            spinner.PreviousRotation = Quaternion.Identity;
            turning.Instances.Add(spinner);
            Capture unturned = Gpu.RenderAndCapture(renderer, turning, "rotation-alpha0", alpha: 0f);
            Capture turned = Gpu.RenderAndCapture(renderer, turning, "rotation-alpha1", alpha: 1f);
            int row = height / 2;
            float ratio = Measure.LongestRun(turned, row, horizontal: true) / (float)Measure.LongestRun(unturned, row, horizontal: true);
            ratio.Should().BeInRange(1.3f, 1.5f, "a box turned by 45 degrees is about √2 times wider");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AlphaInterpolatesTheCamera()
    {
        const int width = 320, height = 180;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.9f, 0.9f, 0.9f, 1)));
            RenderSnapshot snapshot = LitFromBehind();
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, 0, -6), Vector3.One));
            // The camera moved 2 m to the right during the tick; the box is fixed in the world.
            snapshot.Camera.PreviousPosition = new Vector3(-2f, 0, 0);
            Capture start = Gpu.RenderAndCapture(renderer, snapshot, "camera-alpha0", alpha: 0f);
            Capture end = Gpu.RenderAndCapture(renderer, snapshot, "camera-alpha1", alpha: 1f);
            Vector2 seenFromPrevious = Gpu.Project(new Vector3(2f, 0, -6), width, height);
            Vector2 seenFromCurrent = Gpu.Project(new Vector3(0, 0, -6), width, height);
            start.Luma(seenFromPrevious).Should().BeGreaterThan(0.3f, "alpha 0 sees the box from the previous position");
            start.Luma(seenFromCurrent).Should().BeLessThan(0.1f, "alpha 0 does not see the box where the current position would");
            end.Luma(seenFromCurrent).Should().BeGreaterThan(0.3f, "alpha 1 sees the box from the current position");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ResizeChangesTheOutputAndKeepsRendering()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f));
            MaterialId orange = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.9f, 0.45f, 0.1f, 1)));
            RenderSnapshot snapshot = LitFromBehind();
            snapshot.Instances.Add(Gpu.Instance(sphere, orange, new Vector3(0, 0, -5), Vector3.One));

            Capture small = Gpu.RenderAndCapture(renderer, snapshot, "resize-320x180");
            (small.Width, small.Height).Should().Be((320, 180));

            renderer.Resize(400, 300);
            Capture square = Gpu.RenderAndCapture(renderer, snapshot, "resize-400x300");
            (square.Width, square.Height).Should().Be((400, 300));
            renderer.OutputSize.Should().Be((400, 300));
            square.Luma(200, 150).Should().BeGreaterThan(0.3f, "the sphere is centered after the resize");

            renderer.Resize(800, 450);
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);
            Capture large = Gpu.RenderAndCapture(renderer, snapshot, "resize-800x450");
            (large.Width, large.Height).Should().Be((800, 450));
            // The sphere covers the same fraction of the height at any size.
            static float SphereHeight(Capture image) => Measure.LongestRun(image, image.Width / 2, horizontal: false) / (float)image.Height;
            SphereHeight(large).Should().BeApproximately(SphereHeight(small), 0.03f, "the vertical field of view does not change with the size");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TransparentObjectsBlendOverOpaqueAndAreHiddenBehindIt()
    {
        const int width = 480, height = 270;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.9f, 0.9f, 0.9f, 1), gloss: 0.1f));
            MaterialId redGlass = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(1f, 0.05f, 0.05f, 0.5f), gloss: 0.1f) with { SpecularIntensity = 0f });
            RenderSnapshot snapshot = LitFromBehind();
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, 0, -8), new Vector3(4, 4, 1)));
            // In front of the wall, and a second one hidden behind it.
            snapshot.Instances.Add(Gpu.Instance(sphere, redGlass, new Vector3(-1, 0, -5), new Vector3(0.7f), flags: RenderFlags.Transparent));
            snapshot.Instances.Add(Gpu.Instance(sphere, redGlass, new Vector3(1, 0, -11), new Vector3(0.7f), flags: RenderFlags.Transparent));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "transparent");
            Vector3 wall = image.Color(Gpu.Project(new Vector3(1.5f, 1.5f, -7.5f), width, height));
            Vector3 tinted = image.Color(Gpu.Project(new Vector3(-1, 0, -5), width, height));
            Vector3 hidden = image.Color(Gpu.Project(new Vector3(1, 0, -7.5f), width, height));
            wall.X.Should().BeGreaterThan(0.5f, "the wall is lit");
            MathF.Abs(wall.X - wall.Y).Should().BeLessThan(0.05f, "the wall is white: {0}", wall);
            // Half red glass over a white wall, compared in linear light: red stays, green drops to about half
            // (the wall still shows through).
            Vector3 wallLinear = Measure.ToLinear(wall), tintedLinear = Measure.ToLinear(tinted);
            tintedLinear.X.Should().BeGreaterThan(0.8f * wallLinear.X, "red glass keeps the red of the wall behind it");
            (tintedLinear.Y / wallLinear.Y).Should().BeInRange(0.35f, 0.7f, "half-opaque red glass lets about half the green through");
            Vector3.Distance(hidden, wall).Should().BeLessThan(0.05f, "the sphere behind the wall does not show ({0} vs {1})", hidden, wall);
            // Both spheres share a mesh and a range, so after sorting back to front they form one command.
            renderer.LastFrameStats.TransparentCommands.Should().Be(1);
            renderer.LastFrameStats.GpuInstances.Should().Be(3);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ObjectsAcrossDepthRangeBoundariesHaveNoGaps()
    {
        const int width = 640, height = 360;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1), gloss: 0.1f));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0, 1, 0.2f));
            // A rail 200 m below the eye from 1 km to 3 km, across the near/middle boundary at 2 km, and a slab
            // 400 km below and to the left from 1,000 km to 3,000 km, across the middle/far boundary at 2,000 km.
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, -200, -2_000), new Vector3(30, 4, 2_000)));
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(-400_000, -400_000, -2_000_000), new Vector3(200_000, 4_000, 2_000_000)));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "range-boundaries");
            RenderStats stats = renderer.LastFrameStats;
            // The rail is drawn in the near and middle ranges, the slab in the middle and far ranges. In the middle
            // range they share the box mesh, so they form one command with two instances.
            stats.NearCommands.Should().Be(1);
            stats.MiddleCommands.Should().Be(1);
            stats.FarCommands.Should().Be(1);
            stats.GpuInstances.Should().Be(4);

            // The center line of each top face projects to a screen segment. Every pixel along it must be covered,
            // in particular where it crosses a range boundary.
            void AssertCovered(Vector3 nearEnd, Vector3 farEnd, Vector3 boundary)
            {
                Vector2 a = Gpu.Project(nearEnd, width, height);
                Vector2 b = Gpu.Project(farEnd, width, height);
                Vector2 crossing = Gpu.Project(boundary, width, height);
                // Skip the last pixels at both ends, where a pixel center can fall just past the object's end.
                for (int i = 10; i <= 190; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / 200f);
                    image.Luma(p).Should().BeGreaterThan(0.2f, "{0} on {1}-{2} is covered; the range boundary crosses at {3}", p, a, b, crossing);
                }
            }
            AssertCovered(new Vector3(0, -198, -1_020), new Vector3(0, -198, -2_980), new Vector3(0, -198, -2_000));
            AssertCovered(new Vector3(-400_000, -396_000, -1_020_000), new Vector3(-400_000, -396_000, -2_980_000), new Vector3(-400_000, -396_000, -2_000_000));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGroundPlaneReachingBehindTheCameraAndPastTwoKilometresDoesNotHideWhatStandsOnIt()
    {
        const int width = 960, height = 540;
        const float fov = MathF.PI / 6f;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1), gloss: 0.3f));
            MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.1f, 0.1f, 1), gloss: 0.3f));
            // Looking 20 degrees down at a red box 1.5 km away. The ground's two triangles reach 1 km behind the camera
            // and 4 km ahead, past the near range's far boundary: with a far clipping plane, MoltenVK drew the ground
            // with wrong depth over the box.
            Quaternion camera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -20f * MathF.PI / 180f);
            float ground = -1_500f * MathF.Tan(20f * MathF.PI / 180f);
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(1, 1, 0));
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            snapshot.Instances.Add(Gpu.Instance(plane, white, new Vector3(0, ground, -1_500f), new Vector3(100f, 1, 2_500f)));
            snapshot.Instances.Add(Gpu.Instance(box, red, new Vector3(0, ground + 20f, -1_500f), new Vector3(40f)));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "ground-behind-and-beyond");
            Vector3 top = image.Color(Gpu.Project(new Vector3(0, ground + 40f, -1_500f), camera, width, height, fov));
            top.X.Should().BeGreaterThan(0.4f, "the box's top is lit").And.BeGreaterThan(2 * top.Y, "the box's top is red, not the ground: {0}", top);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
