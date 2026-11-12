# Timing ablation: the walks' step caps come from the variables SWEEP_RSTEPS and SWEEP_DSTEPS when they are set, and the
# guided share from SWEEP_GUIDED. One build then gives the cost of the rays as a function of each cap.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.envexp(root)
common.sub(root + '/src/Tertius.Rendering/Scene/GpuTypes.cs',
    "            ReflectionSteps = (uint)trace.ReflectionSteps,\n            DiffuseSteps = (uint)trace.DiffuseSteps,\n            GuidedShare = guided ? LightPyramid.GuidedShare : 0,\n",
    "            ReflectionSteps = Environment.GetEnvironmentVariable(\"SWEEP_RSTEPS\") is { Length: > 0 } sweepR ? uint.Parse(s: sweepR, provider: System.Globalization.CultureInfo.InvariantCulture) : (uint)trace.ReflectionSteps,\n"
    "            DiffuseSteps = Environment.GetEnvironmentVariable(\"SWEEP_DSTEPS\") is { Length: > 0 } sweepD ? uint.Parse(s: sweepD, provider: System.Globalization.CultureInfo.InvariantCulture) : (uint)trace.DiffuseSteps,\n"
    "            GuidedShare = guided ? float.Parse(s: Environment.GetEnvironmentVariable(\"SWEEP_GUIDED\") ?? LightPyramid.GuidedShare.ToString(System.Globalization.CultureInfo.InvariantCulture), provider: System.Globalization.CultureInfo.InvariantCulture) : 0,\n")
