// The reflection rays' spatiotemporal blue-noise (STBN) mask (ARCHITECTURE.md, "Randomness and determinism").
// native/stbn-mask.nix builds and runs it; nothing else does.
//
//   cc -O2 -o stbn stbn.c -lm && ./stbn <out.bin>
//
// The mask holds 128 x 128 cells over 64 frames. Each texel holds two bytes: a radial number and an angle, which wraps.
// The file is a 1024 x 1024 image of two bytes per texel, frame t in tile (t % 8, t / 8), rows top first.
//
// The method (Georgiev and Fajardo 2016, with the cross-shaped energy of the STBN paper, Wolfe et al. 2022):
//  1. Each cell's 64 values over the frames are the first 64 points of the 2D Sobol sequence, with an Owen scramble
//     of their 6 bits per cell and 2 random low bits. So each cell's values fill each of the 8 x 8 strata of the unit
//     square once per period.
//  2. Each cell's values start in a random order over the frames.
//  3. Greedy swaps of two frames' values at one cell lower an energy with two terms: pairs of cells in one frame, by
//     their distance in space, and pairs of frames at one cell, by their distance in time. A swap keeps each cell's
//     set, so step 1 holds at the end.
// The energy is integer arithmetic over tables, and the random numbers come from a fixed seed, so every machine writes
// the same bytes. The tables round libm's exp and sqrt to 2^-20, which no last-bit difference of libm can move.
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

enum
{
    Side = 128,
    Frames = 64,
    Reach = 3,         // the spatial pairs: cells up to 3 apart in x and y, toroidally
    TimeReach = 4,     // the temporal pairs: frames up to 4 apart, toroidally
};

// 100 million swaps: the 5 x 5 blurred variance of a frame was 0.203 of white noise's at 80 million and 0.201 at
// 200 million in the study of ter-s97m.
static const long Swaps = 100000000L;
static const double SigmaSpace = 2.1, SigmaTime = 1.9;

static uint8_t radial[Frames][Side][Side], angle[Frames][Side][Side];
static int64_t spaceWeight[2 * Reach + 1][2 * Reach + 1], timeWeight[TimeReach + 1];
// exp(-|dv|) of two values whose radial bytes differ by dr and whose angle bytes lie da apart around the circle.
static int64_t valueWeight[256][129];

static uint64_t state = 0x9E3779B97F4A7C15ull;

static uint64_t next(void)
{
    state ^= state >> 12;
    state ^= state << 25;
    state ^= state >> 27;
    return state * 0x2545F4914F6CDD1Dull;
}

static uint32_t below(uint32_t n)
{
    return (uint32_t)((next() >> 32) * (uint64_t)n >> 32);
}

static int64_t pairEnergy(int r0, int a0, int r1, int a1)
{
    int da = abs(a0 - a1);
    return valueWeight[abs(r0 - r1)][da < 256 - da ? da : 256 - da];
}

// The energy of every pair that holds the value at (x, y, t), apart from the pair with frame `skip` at the same cell.
static int64_t texelEnergy(int x, int y, int t, int skip)
{
    int r = radial[t][y][x], a = angle[t][y][x];
    int64_t e = 0;
    for (int dy = -Reach; dy <= Reach; dy++)
        for (int dx = -Reach; dx <= Reach; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            int nx = (x + dx + Side) % Side, ny = (y + dy + Side) % Side;
            e += spaceWeight[dy + Reach][dx + Reach] * pairEnergy(r, a, radial[t][ny][nx], angle[t][ny][nx]);
        }
    for (int dt = -TimeReach; dt <= TimeReach; dt++)
    {
        int nt = (t + dt + Frames) % Frames;
        if (dt == 0 || nt == skip)
            continue;
        e += timeWeight[abs(dt)] * pairEnergy(r, a, radial[nt][y][x], angle[nt][y][x]);
    }
    return e;
}

// The second dimension of the Sobol sequence, 32 bits.
static uint32_t sobol2(uint32_t i)
{
    uint32_t v = 1u << 31, y = 0;
    for (; i; i >>= 1, v ^= v >> 1)
        if (i & 1)
            y ^= v;
    return y;
}

static uint32_t reverse6(uint32_t i)
{
    uint32_t r = 0;
    for (int b = 0; b < 6; b++)
        r |= ((i >> b) & 1u) << (5 - b);
    return r;
}

static uint32_t hash32(uint32_t x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

// An Owen scramble of a 6-bit value: each bit flips by a hash of the seed and the bits above it.
static uint32_t owen6(uint32_t v, uint32_t seed)
{
    uint32_t out = 0;
    for (int b = 5; b >= 0; b--)
    {
        uint32_t prefix = (v >> (b + 1)) | (1u << (5 - b));
        uint32_t flip = hash32(seed ^ hash32(prefix * 0x9E3779B9u)) & 1u;
        out |= (((v >> b) & 1u) ^ flip) << b;
    }
    return out;
}

static void makeTables(void)
{
    for (int dy = -Reach; dy <= Reach; dy++)
        for (int dx = -Reach; dx <= Reach; dx++)
            spaceWeight[dy + Reach][dx + Reach] = llround(1024.0 * exp(-(dx * dx + dy * dy) / (SigmaSpace * SigmaSpace)));
    for (int dt = 0; dt <= TimeReach; dt++)
        timeWeight[dt] = llround(1024.0 * exp(-(dt * dt) / (SigmaTime * SigmaTime)));
    for (int dr = 0; dr < 256; dr++)
        for (int da = 0; da <= 128; da++)
            valueWeight[dr][da] = llround(1048576.0 * exp(-sqrt((double)(dr * dr + da * da)) / 256.0));
}

static void seed(void)
{
    for (int y = 0; y < Side; y++)
        for (int x = 0; x < Side; x++)
        {
            uint32_t cellSeed = hash32((uint32_t)(y * Side + x) * 2u + 1u);
            int order[Frames];
            for (int t = 0; t < Frames; t++)
                order[t] = t;
            for (int t = Frames - 1; t > 0; t--)
            {
                int k = (int)below((uint32_t)t + 1u), swap = order[t];
                order[t] = order[k];
                order[k] = swap;
            }
            for (int i = 0; i < Frames; i++)
            {
                uint32_t r6 = owen6(reverse6((uint32_t)i), cellSeed);
                uint32_t a6 = owen6(sobol2((uint32_t)i) >> 26, hash32(cellSeed));
                radial[order[i]][y][x] = (uint8_t)((r6 << 2) | below(4));
                angle[order[i]][y][x] = (uint8_t)((a6 << 2) | below(4));
            }
        }
}

static void swapValues(int x, int y, int a, int b)
{
    uint8_t r = radial[a][y][x], an = angle[a][y][x];
    radial[a][y][x] = radial[b][y][x];
    angle[a][y][x] = angle[b][y][x];
    radial[b][y][x] = r;
    angle[b][y][x] = an;
}

int main(int argc, char **argv)
{
    if (argc != 2)
    {
        fprintf(stderr, "usage: stbn <out.bin>\n");
        return 2;
    }
    makeTables();
    seed();
    for (long s = 0; s < Swaps; s++)
    {
        int x = (int)below(Side), y = (int)below(Side), a = (int)below(Frames), b = (int)below(Frames);
        if (a == b)
            continue;
        int64_t before = texelEnergy(x, y, a, b) + texelEnergy(x, y, b, a);
        swapValues(x, y, a, b);
        int64_t after = texelEnergy(x, y, a, b) + texelEnergy(x, y, b, a);
        if (after >= before)
            swapValues(x, y, a, b);
    }
    FILE *out = fopen(argv[1], "wb");
    if (!out)
        return 1;
    for (int row = 0; row < 8 * Side; row++)
        for (int col = 0; col < 8 * Side; col++)
        {
            int t = (row / Side) * 8 + col / Side, y = row % Side, x = col % Side;
            uint8_t texel[2] = {radial[t][y][x], angle[t][y][x]};
            if (fwrite(texel, 1, 2, out) != 2)
                return 1;
        }
    return fclose(out) == 0 ? 0 : 1;
}
