# Diagnostic only: recolor one decoded image (matched by encoded byte count) when TERTIUS_DEBUG_RECOLOR_BYTES is set.
# TERTIUS_DEBUG_RECOLOR_MODE: blue (swap red and blue), gray (luma), black (zero color: the helmet keeps its geometry).
import sys, pathlib
p = pathlib.Path(sys.argv[1]) / "src/Tertius.Assets/GlbReader.cs"
s = p.read_text()
old = "            return new(Width: result.Width, Height: result.Height, Rgba: result.Data, Srgb: srgb);"
new = '''            if (System.Environment.GetEnvironmentVariable("TERTIUS_DEBUG_RECOLOR_BYTES") == bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                string mode = System.Environment.GetEnvironmentVariable("TERTIUS_DEBUG_RECOLOR_MODE") ?? "blue";
                for (int i = 0; i + 3 < result.Data.Length; i += 4)
                {
                    byte r = result.Data[i], g = result.Data[i + 1], b = result.Data[i + 2];
                    if (mode == "black") { result.Data[i] = 0; result.Data[i + 1] = 0; result.Data[i + 2] = 0; }
                    else if (mode == "gray") { byte y = (byte)((r * 54 + g * 183 + b * 19) >> 8); result.Data[i] = y; result.Data[i + 1] = y; result.Data[i + 2] = y; }
                    else { result.Data[i] = b; result.Data[i + 2] = r; }
                }
            }
''' + old
assert s.count(old) == 1
p.write_text(s.replace(old, new))
