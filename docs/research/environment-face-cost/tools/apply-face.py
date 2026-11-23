# Tool code of ter-yglw (the environment face cost). Applies the scratch switches of FaceProbe.cs and FrameTimeProbe.cs
# to a plain build copy of main: the face size, the size cull, faces in every second drawn frame, and the per-face and
# per-frame logs. The edits never enter src. Usage: python3 -I apply-face.py <build copy root>
import os, shutil, sys

root = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:70])
    open(path, 'w').write(s.replace(old, new))


edit('src/Tertius.Rendering/EnvironmentMapSetting.cs',
     'public static SceneEnvironmentMap Default => new(FaceSize: 128, FacesPerFrame: 1);',
     'public static SceneEnvironmentMap Default => new(FaceSize: FaceProbe.Size, FacesPerFrame: 1); // PROBE (ter-yglw)')
edit('src/Tertius.Rendering/Frame/EnvironmentMap.cs',
     """        EnvironmentFaces.InTurn(frame, Setting.FacesPerFrame, faces);
""",
     """        // PROBE (ter-yglw): faces in every second frame, and the per-face log.
        FaceProbe.Begin(frame);
        long inTurn = FaceProbe.InTurnFrame(frame);
        if (inTurn >= 0)
            EnvironmentFaces.InTurn(inTurn, Setting.FacesPerFrame, faces);
        else
            faces.Clear();
""")
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     """        int opaqueCount = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly PreparedInstance instance = ref instances[i];
            if (
                !DrawsInMaps(instance)
                || !EnvironmentChain.Draws(map: map, objectMap: instance.Map)
                || CubeFaces.Culls(basis, instance.Position, instance.Radius)
            )
                continue;
            int mask = DepthRanges.OpaqueMask(Vector3.Dot(instance.Position, basis.Forward), instance.Radius);
""",
     """        int opaqueCount = 0;
        int probeKept = 0, probeCulled = 0; // PROBE (ter-yglw)
        long probeTriangles = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly PreparedInstance instance = ref instances[i];
            if (
                !DrawsInMaps(instance)
                || !EnvironmentChain.Draws(map: map, objectMap: instance.Map)
                || CubeFaces.Culls(basis, instance.Position, instance.Radius)
            )
                continue;
            if (FaceProbe.CullsSmall(instance.Position, instance.Radius))
            {
                probeCulled++;
                continue;
            }
            int mask = DepthRanges.OpaqueMask(Vector3.Dot(instance.Position, basis.Forward), instance.Radius);
            probeKept++;
            probeTriangles += meshes[instance.Mesh].IndexCount / 3 * BitOperations.PopCount((uint)mask);
""")
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     """                Near: faceRanges[DepthRanges.Near].Sided
            )
        );
    }
""",
     """                Near: faceRanges[DepthRanges.Near].Sided
            )
        );
        FaceProbe.Face(
            map,
            (int)face,
            faceRanges[DepthRanges.Far].Count + faceRanges[DepthRanges.Middle].Count + faceRanges[DepthRanges.Near].Count,
            probeTriangles,
            probeKept,
            probeCulled
        );
    }
""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        disposed = true;
        if (window != null && onFramebufferResize != null)""",
     """        disposed = true;
        if (environmentMap is not null) // PROBE (ter-yglw): the face dump
            FaceProbe.Dump(this, environmentMap.DrawnFor.Length);
        if (window != null && onFramebufferResize != null)""")
edit('src/Tertius.Host/ScriptCommand.cs',
     """                    readings.Add(renderer.LastFrameStats);
""",
     """                    readings.Add(renderer.LastFrameStats);
                    FrameTimeProbe.Write(renderer.LastFrameStats); // PROBE (ter-yglw)
""")
shutil.copy(os.path.join(here, 'FaceProbe.cs'), os.path.join(root, 'src/Tertius.Rendering/Scene/FaceProbe.cs'))
shutil.copy(os.path.join(here, 'FrameTimeProbe.cs'), os.path.join(root, 'src/Tertius.Host/FrameTimeProbe.cs'))
print('face edits applied to', root)
