# Fixes the exposure at the deck's value under main, so that a variant that changes the image keeps the lamps' reach.
def apply(root):
    p = root + '/src/Tertius.Host/ScriptCommand.Options.cs'
    s = open(p).read()
    a = "            if (ToneCurve is { } curve)\n                options.ToneCurve = curve;\n"
    assert s.count(a) == 1
    s = s.replace(a, "            options.Exposure = new FixedExposure(0.7041458f);\n" + a)
    open(p, 'w').write(s)
