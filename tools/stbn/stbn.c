// Research prototype for ticket ter-s97m: a spatiotemporal blue-noise (STBN) mask of 2D values.
//
//   cc -O3 -o stbn stbn.c -lm && ./stbn <swaps in millions> <out.bin>
//
// The mask holds W x H pixels over T frames. Each texel holds a 2D value (r, a) in [0, 1)^2, as two bytes: r is the
// radial number of a direction draw (the cosine draw's u.x, the GGX cap's u.y) and a is its angle, which wraps.
//
// Method (Georgiev and Fajardo 2016, with the cross-shaped energy of the STBN paper, Wolfe et al. 2022):
//  1. Each pixel's T values over the frames are a fixed stratified set: the first 64 points of the 2D Sobol sequence,
//     with an Owen scramble of their 6 bits per pixel and 2 random low bits. So every pixel covers [0, 1)^2 evenly
//     over one period.
//  2. Each pixel's values start in a random order over the frames.
//  3. Greedy swaps of two frames' values at one pixel lower an energy with two terms: pairs of pixels in one frame, by
//     their distance in space, and pairs of frames at one pixel, by their distance in time. Each pair adds
//     exp(-d^2 / sigma^2) * exp(-|dv|), with dv the difference of the two values (the angle wraps). A swap keeps each
//     pixel's set, so step 1 holds at the end, and the energy makes each frame blue over space and each pixel's
//     nearby frames spread over [0, 1)^2.
// The program is deterministic: a fixed seed and one thread give the same file on every run.
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

enum { W = 128, H = 128, T = 64, R = 3, RT = 4 };
static const float SigmaS = 2.1f, SigmaT = 1.9f;

static float vr[T][H][W], va[T][H][W];
static float spatialWeight[2 * R + 1][2 * R + 1], temporalWeight[RT + 1];

static uint64_t state = 0x9E3779B97F4A7C15ull;
static uint64_t next(void)
{
    state ^= state >> 12;
    state ^= state << 25;
    state ^= state >> 27;
    return state * 0x2545F4914F6CDD1Dull;
}
static uint32_t below(uint32_t n) { return (uint32_t)((next() >> 32) * (uint64_t)n >> 32); }

static float pairEnergy(float r0, float a0, float r1, float a1)
{
    float dr = r0 - r1, da = fabsf(a0 - a1);
    da = fminf(da, 1.0f - da);
    return expf(-sqrtf(dr * dr + da * da));
}

// The energy of every pair that holds texel (x, y, t), apart from the pair with frame `skip` at the same pixel.
static float texelEnergy(int x, int y, int t, int skip)
{
    float r = vr[t][y][x], a = va[t][y][x], e = 0.0f;
    for (int dy = -R; dy <= R; dy++)
        for (int dx = -R; dx <= R; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            int nx = (x + dx + W) % W, ny = (y + dy + H) % H;
            e += spatialWeight[dy + R][dx + R] * pairEnergy(r, a, vr[t][ny][nx], va[t][ny][nx]);
        }
    for (int dt = -RT; dt <= RT; dt++)
    {
        int nt = (t + dt + T) % T;
        if (dt == 0 || nt == skip)
            continue;
        e += temporalWeight[abs(dt)] * pairEnergy(r, a, vr[nt][y][x], va[nt][y][x]);
    }
    return e;
}

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

static float spectralBlur(float (*v)[H][W]);

int main(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: stbn <swaps in millions> <out.bin>\n");
        return 2;
    }
    double millions = atof(argv[1]);
    for (int dy = -R; dy <= R; dy++)
        for (int dx = -R; dx <= R; dx++)
            spatialWeight[dy + R][dx + R] = expf(-(float)(dx * dx + dy * dy) / (SigmaS * SigmaS));
    for (int dt = 0; dt <= RT; dt++)
        temporalWeight[dt] = expf(-(float)(dt * dt) / (SigmaT * SigmaT));

    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            uint32_t pixelSeed = hash32((uint32_t)(y * W + x) * 2u + 1u);
            int order[T];
            for (int t = 0; t < T; t++)
                order[t] = t;
            for (int t = T - 1; t > 0; t--)
            {
                int k = (int)below((uint32_t)t + 1u), s = order[t];
                order[t] = order[k];
                order[k] = s;
            }
            for (int i = 0; i < T; i++)
            {
                uint32_t r6 = owen6(reverse6((uint32_t)i), pixelSeed), a6 = owen6(sobol2((uint32_t)i) >> 26, hash32(pixelSeed));
                uint32_t r8 = (r6 << 2) | below(4), a8 = (a6 << 2) | below(4);
                vr[order[i]][y][x] = ((float)r8 + 0.5f) / 256.0f;
                va[order[i]][y][x] = ((float)a8 + 0.5f) / 256.0f;
            }
        }
    fprintf(stderr, "start: blurred variance ratio %.4f\n", spectralBlur(vr));

    long swaps = (long)(millions * 1e6), accepted = 0;
    for (long s = 0; s < swaps; s++)
    {
        int x = (int)below(W), y = (int)below(H), a = (int)below(T), b = (int)below(T);
        if (a == b)
            continue;
        float before = texelEnergy(x, y, a, b) + texelEnergy(x, y, b, a);
        float tr = vr[a][y][x], ta = va[a][y][x];
        vr[a][y][x] = vr[b][y][x];
        va[a][y][x] = va[b][y][x];
        vr[b][y][x] = tr;
        va[b][y][x] = ta;
        float after = texelEnergy(x, y, a, b) + texelEnergy(x, y, b, a);
        if (after < before)
            accepted++;
        else
        {
            vr[b][y][x] = vr[a][y][x];
            va[b][y][x] = va[a][y][x];
            vr[a][y][x] = tr;
            va[a][y][x] = ta;
        }
        if ((s + 1) % 10000000 == 0)
            fprintf(stderr, "%ld M swaps, %ld accepted, blurred variance ratio %.4f\n", (s + 1) / 1000000, accepted,
                spectralBlur(vr));
    }

    // The file: a 1024 x 1024 image of two bytes per texel, frame t in tile (t % 8, t / 8) of 128 x 128.
    FILE *out = fopen(argv[2], "wb");
    if (!out)
        return 1;
    for (int row = 0; row < 8 * H; row++)
        for (int col = 0; col < 8 * W; col++)
        {
            int t = (row / H) * 8 + col / W, y = row % H, x = col % W;
            unsigned char texel[2] = {(unsigned char)(vr[t][y][x] * 256.0f), (unsigned char)(va[t][y][x] * 256.0f)};
            fwrite(texel, 1, 2, out);
        }
    fclose(out);
    return 0;
}

// The variance of frame 0's r after a 5 x 5 box blur, over the variance that white noise keeps after it (1 / 25).
static float spectralBlur(float (*v)[H][W])
{
    double sum = 0, sq = 0, mean = 0;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            mean += v[0][y][x];
    mean /= W * H;
    double var = 0;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            var += (v[0][y][x] - mean) * (v[0][y][x] - mean);
    var /= W * H;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            double b = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    b += v[0][(y + dy + H) % H][(x + dx + W) % W];
            b = b / 25.0 - mean;
            sum += b;
            sq += b * b;
        }
    return (float)((sq / (W * H)) / (var / 25.0));
}
