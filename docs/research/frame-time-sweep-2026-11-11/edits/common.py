# Shared by the edit scripts: exact-string replacement that fails loudly, and the fixed exposure of the deck.
def sub(path, a, b, n=1):
    s = open(path).read()
    assert s.count(a) == n, (path, a[:80], s.count(a))
    open(path, 'w').write(s.replace(a, b))

def fixexp(root):
    # Fixes the exposure at the deck's value under main (the tile agent's 0.704 stops), so that a variant that changes
    # the image keeps every lamp's reach.
    sub(root + '/src/Tertius.Host/ScriptCommand.Options.cs',
        "            if (ToneCurve is { } curve)\n                options.ToneCurve = curve;\n",
        "            options.Exposure = new FixedExposure(0.7041458f);\n            if (ToneCurve is { } curve)\n                options.ToneCurve = curve;\n")

SH = '/src/Tertius.Rendering/Shaders/'

def envexp(root):
    # The exposure comes from the variable SWEEP_EXPOSURE, in stops, when it is set: one build serves every scene.
    sub(root + '/src/Tertius.Host/ScriptCommand.Options.cs',
        "            if (ToneCurve is { } curve)\n                options.ToneCurve = curve;\n",
        "            if (Environment.GetEnvironmentVariable(\"SWEEP_EXPOSURE\") is { Length: > 0 } sweepStops)\n"
        "                options.Exposure = new FixedExposure(float.Parse(s: sweepStops, provider: System.Globalization.CultureInfo.InvariantCulture));\n"
        "            if (ToneCurve is { } curve)\n                options.ToneCurve = curve;\n")
