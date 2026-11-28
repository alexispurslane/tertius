"""Print the mean scene-linear red and green of whole frames from the probe's HDR readback (volume.bin.hdr)."""
import argparse
import struct
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", nargs="+", help="capture folders that hold volume.bin.hdr")
    for folder in parser.parse_args().captures:
        data = Path(folder, "volume.bin.hdr").read_bytes()
        rgba = struct.unpack(f"<{len(data)//4}f", data)
        red, green = rgba[0::4], rgba[1::4]
        print(f"{folder}: mean red {sum(red)/len(red):.4f}, mean green {sum(green)/len(green):.5f} W/(m² sr)")


if __name__ == "__main__":
    main()
