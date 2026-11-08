using System.Numerics;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

/// <summary>Tool-only numeric inputs of one renderer frame, without modifying any live resource.</summary>
internal static class RasterDiagnostic
{
    public static JsonElement ReadFrame(FrameContext context, MeshData boxGeometry)
    {
        CameraView camera = context.Scene.Camera;
        GpuFrameConstants constants = context.Scene.Constants;
        var properties = context.Device.Properties;
        var limits = properties.Limits;
        return JsonSerializer.SerializeToElement(new
        {
            Scope = "Numeric inputs of the first recorded renderer frame. No lighting reference, tolerance change or vertex-shader-output readback.",
            context.Number,
            Device = new
            {
                context.Device.DeviceName, Driver = context.Device.Driver.ToString(),
                properties.ApiVersion, properties.DriverVersion, properties.VendorID, properties.DeviceID,
                limits.SubPixelPrecisionBits, limits.ViewportSubPixelBits, limits.SubPixelInterpolationOffsetBits,
                MinInterpolationOffset = (double)limits.MinInterpolationOffset, MaxInterpolationOffset = (double)limits.MaxInterpolationOffset,
            },
            NumericStorage = "Float components are widened exactly to double before JSON serialization; matrices are System.Numerics rows, using row vectors. GLSL upload interprets their transpose.",
            Camera = new
            {
                Offset = Components(camera.Offset), Rotation = new double[] { camera.Rotation.X, camera.Rotation.Y, camera.Rotation.Z, camera.Rotation.W },
                Right = Components(camera.Right), Up = Components(camera.Up), Forward = Components(camera.Forward),
                VerticalFov = (double)camera.VerticalFov, Aspect = (double)camera.Aspect,
                TanHalfX = (double)camera.TanHalfX, TanHalfY = (double)camera.TanHalfY,
                camera.Width, camera.Height, View = Rows(camera.View),
            },
            ShaderFrame = new
            {
                CameraRight = Components(constants.CameraRight), CameraUp = Components(constants.CameraUp), CameraForward = Components(constants.CameraForward),
                Viewport = Components(constants.Viewport), OutputViewport = Components(constants.OutputViewport), RenderParams = Components(constants.RenderParams),
                FarViewProjection = Rows(constants.FarViewProj), MiddleViewProjection = Rows(constants.MiddleViewProj), NearViewProjection = Rows(constants.NearViewProj),
            },
            Viewport = new { X = 0, Y = 0, context.Extent.Width, context.Extent.Height, MinDepth = 0, MaxDepth = 1 },
            ViewportProvenance = "GeometryPass calls FrameContext.SetFullViewport with the recorded extent; RenderParams Y/Z contain pixel jitter.",
            RangeViewProjections = context.Scene.RangeViewProjection.Select(Rows).ToArray(),
            RangeIndices = new { DepthRanges.Far, DepthRanges.Middle, DepthRanges.Near },
            Instances = context.Scene.Instances.Take(context.Scene.InstanceCount).Select(instance => Instance(instance, boxGeometry)).ToArray(),
            Commands = context.Scene.Commands.Take(context.Scene.CommandCount).Select(command => new
            {
                command.IndexCount, command.InstanceCount, command.FirstIndex, command.VertexOffset, command.FirstInstance,
            }).ToArray(),
            OpaqueRanges = context.Scene.Opaque.Select(runs => new { runs.OneSided, runs.TwoSided }).ToArray(),
            BoxGeometry = new
            {
                Provenance = "The same MeshData object supplied to VulkanRenderer.CreateMesh, not a separately reconstructed box.",
                LocalPositions = boxGeometry.Vertices.Select(vertex => Components(vertex.Position)).ToArray(),
                LocalNormals = boxGeometry.Vertices.Select(vertex => Components(vertex.Normal)).ToArray(),
                boxGeometry.Indices,
            },
            TransformProvenance = "CPU-derived camera-relative positions use the recorded model rows and source vertices with Vector4.Dot. These are not captured GPU vertex outputs; GPU arithmetic/interpolation details remain unproven.",
        });
    }

    private static object Instance(GpuInstance instance, MeshData boxGeometry) => new
    {
        instance.Material, instance.PlacedIndex,
        ModelRow0 = Components(instance.ModelRow0), ModelRow1 = Components(instance.ModelRow1), ModelRow2 = Components(instance.ModelRow2),
        InverseScaleSquared = Components(instance.InverseScaleSquared),
        CpuTransformedPositionsCameraRelative = boxGeometry.Vertices.Select(vertex =>
        {
            var local = new Vector4(vertex.Position, 1);
            return new double[] { Vector4.Dot(instance.ModelRow0, local), Vector4.Dot(instance.ModelRow1, local), Vector4.Dot(instance.ModelRow2, local) };
        }).ToArray(),
    };

    private static double[] Components(Vector3 value) => [value.X, value.Y, value.Z];

    private static double[] Components(Vector4 value) => [value.X, value.Y, value.Z, value.W];

    private static double[][] Rows(Matrix4x4 value) =>
    [
        [value.M11, value.M12, value.M13, value.M14],
        [value.M21, value.M22, value.M23, value.M24],
        [value.M31, value.M32, value.M33, value.M34],
        [value.M41, value.M42, value.M43, value.M44],
    ];
}
