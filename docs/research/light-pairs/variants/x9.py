import sys
p = sys.argv[1]; s = open(p).read()
a = "            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);\n"
assert s.count(a) == 1
s = s.replace(a, """            DirectTerms terms;
            if (frame.renderParams.w < -1.0)
            {
                terms = pathLight(i, fiber, shape, s, lobes, V, p);
            }
            else
            {
                terms.diffuse = p.irradiance * 1e-3;
                terms.specular = vec3(0.0);
            }
""")
open(p, "w").write(s)
