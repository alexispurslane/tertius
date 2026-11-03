namespace Tertius.Rendering;

/// <summary>
/// One entry of the alias table of the sampled lights (Vose 1991): a draw of entry i takes <see cref="Light"/> with the
/// probability <see cref="Threshold"/>, and else the light of entry <see cref="Alias"/>.
/// </summary>
/// <param name="Light">The light's index in the frame's light list.</param>
/// <param name="Alias">The entry whose light a draw of this entry takes above the threshold.</param>
/// <param name="Threshold">The share of this entry's draws that take its own light, from 0 to 1.</param>
/// <param name="Share">The light's source probability: the probability with which a draw of the table takes it.</param>
internal readonly record struct LightSample(int Light, int Alias, double Threshold, double Share);

/// <summary>
/// ReSTIR's light table of one frame (docs/plans/temporal-renderer.md, section 7, "The candidates" and "The reservoirs"):
/// the main view's sampled lights in an alias table weighted half by their rank irradiance at the eye and half uniformly,
/// the exact lights, whose shadow slot is a cascade set (decision 28), and the remap of the last frame's light indices by
/// the lights' keys. <see cref="Fill"/> builds all three in doubles, in the light list's order, so the same lights give the
/// same table on every run.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>The shares of the samples add to 1, and each alias names an entry.</item>
/// <item>No light is both sampled and exact.</item>
/// <item>Every remapped index names a sampled light.</item>
/// </list>
/// </remarks>
internal sealed class LightTable
{
    /// <summary>
    /// The remap of a light that this frame does not sample, and the index of no light in a reservoir, whose index holds 16
    /// bits. So a light list holds at most this many sampled lights.
    /// </summary>
    public const int NoLight = 0xFFFF;

    private LightSample[] samples = new LightSample[16];
    private int sampleCount;
    private readonly int[] exact = new int[RendererOptions.MaxCascadedLights];
    private int exactCount;
    private int[] remap = new int[16];
    private int remapCount;

    // Scratch of Fill: each sampled light's scaled share, and the two work lists of Vose's method.
    private double[] scaled = new double[16];
    private int[] small = new int[16];
    private int[] large = new int[16];
    private KeptLight[] keyed = new KeptLight[16];
    private bool[] sampled = new bool[16];
    private UInt4[] packed = new UInt4[16];

    /// <summary>The alias table of the sampled lights, one entry per light, in the light list's order.</summary>
    public ReadOnlySpan<LightSample> Samples => samples.AsSpan(0, sampleCount);

    /// <summary>The exact lights' indices in the light list, in its order: at most <see cref="RendererOptions.MaxCascadedLights"/>.</summary>
    public ReadOnlySpan<int> Exact => exact.AsSpan(0, exactCount);

    /// <summary>For each index of the last drawn frame's light list, the same light's index in this frame's, or <see cref="NoLight"/>.</summary>
    public ReadOnlySpan<int> Remap => remap.AsSpan(0, remapCount);

    /// <summary>
    /// Builds the table of <paramref name="lights"/>, the frame's light list with its shadow slots chosen, joins their keys
    /// with the last drawn frame's of <paramref name="frame"/>, and writes this frame's keys into it.
    /// </summary>
    public void Fill(ReadOnlySpan<PreparedLight> lights, FrameHistory.PendingFrame frame)
    {
        Grow(lights.Length);
        Classify(lights);
        BuildAlias();
        JoinKeys(lights, frame);
    }

    /// <summary>
    /// The table as the shaders read it (restir.glsl, LightTableBlock): texel 0 the counts of the sampled lights, the exact
    /// lights and the remap, texel 1 the exact lights, then one texel per entry, the light, its alias and the bits of its
    /// threshold and its share, then the remap, four indices per texel. The shaders draw in single floats: a threshold or
    /// a share rounds to the nearest one.
    /// </summary>
    public ReadOnlySpan<UInt4> Packed()
    {
        int length = 2 + sampleCount + (remapCount + 3) / 4;
        if (packed.Length < length)
            Array.Resize(ref packed, Math.Max(length, packed.Length * 2));
        packed[0] = new(X: (uint)sampleCount, Y: (uint)exactCount, Z: (uint)remapCount, W: 0);
        Span<uint> exactIndices = stackalloc uint[4];
        for (int i = 0; i < exactCount; i++)
            exactIndices[i] = (uint)exact[i];
        packed[1] = new(X: exactIndices[0], Y: exactIndices[1], Z: exactIndices[2], W: exactIndices[3]);
        for (int k = 0; k < sampleCount; k++)
        {
            LightSample sample = samples[k];
            packed[2 + k] = new(
                X: (uint)sample.Light,
                Y: (uint)sample.Alias,
                Z: BitConverter.SingleToUInt32Bits((float)sample.Threshold),
                W: BitConverter.SingleToUInt32Bits((float)sample.Share)
            );
        }

        Span<uint> four = stackalloc uint[4];
        for (int first = 0; first < remapCount; first += 4)
        {
            for (int i = 0; i < 4; i++)
                four[i] = first + i < remapCount ? (uint)remap[first + i] : NoLight;
            packed[2 + sampleCount + first / 4] = new(X: four[0], Y: four[1], Z: four[2], W: four[3]);
        }

        return packed.AsSpan(0, length);
    }

    /// <summary>Checks the invariants in the class comment. O(lights).</summary>
    internal void AssertInvariants()
    {
        double sum = 0;
        foreach (LightSample sample in Samples)
        {
            sum += sample.Share;
            Contract.Invariant(sample.Alias >= 0 && sample.Alias < sampleCount);
            Contract.Invariant(!Exact.Contains(sample.Light));
        }

        Contract.Invariant(sampleCount == 0 || Math.Abs(sum - 1) < 1e-9);
        foreach (int index in Remap)
            Contract.Invariant(index == NoLight || sampled[index]);
    }

    private void Grow(int lights)
    {
        if (samples.Length >= lights)
            return;
        int size = Math.Max(lights, samples.Length * 2);
        Array.Resize(ref samples, size);
        Array.Resize(ref scaled, size);
        Array.Resize(ref small, size);
        Array.Resize(ref large, size);
        Array.Resize(ref keyed, size);
        Array.Resize(ref sampled, size);
    }

    /// <summary>
    /// Sorts the main view's lights into the exact ones, which hold a cascade set, and the sampled ones, and gives each
    /// sampled light half its share of the rank irradiance plus half a uniform share.
    /// </summary>
    private void Classify(ReadOnlySpan<PreparedLight> lights)
    {
        sampleCount = 0;
        exactCount = 0;
        double rankSum = 0;
        sampled.AsSpan(0, lights.Length).Clear();
        for (int i = 0; i < lights.Length; i++)
        {
            ref readonly PreparedLight light = ref lights[i];
            if (!light.InMainView)
                continue;
            if (light.Shadow is CascadeSet)
            {
                exact[exactCount++] = i;
                continue;
            }

            if (sampleCount == NoLight)
                continue;
            double rank = LightUnits.RankIrradiance(light);
            scaled[sampleCount] = rank;
            samples[sampleCount++] = new(Light: i, Alias: 0, Threshold: 1, Share: 0);
            sampled[i] = true;
            rankSum += rank;
        }

        int count = sampleCount;
        for (int k = 0; k < count; k++)
        {
            // A table of lights whose ranks are all 0 falls back to the uniform choice.
            double share = rankSum > 0 ? 0.5 * scaled[k] / rankSum + 0.5 / count : 1.0 / count;
            samples[k] = samples[k] with { Share = share };
            scaled[k] = share * count;
        }
    }

    /// <summary>Vose's alias method over the scaled shares, with work lists in the order of the table, so it is deterministic.</summary>
    private void BuildAlias()
    {
        int smallCount = 0,
            largeCount = 0;
        for (int k = 0; k < sampleCount; k++)
            if (scaled[k] < 1)
                small[smallCount++] = k;
            else
                large[largeCount++] = k;
        while (smallCount > 0 && largeCount > 0)
        {
            int less = small[--smallCount];
            int more = large[--largeCount];
            samples[less] = samples[less] with { Alias = more, Threshold = scaled[less] };
            scaled[more] = scaled[more] + scaled[less] - 1;
            if (scaled[more] < 1)
                small[smallCount++] = more;
            else
                large[largeCount++] = more;
        }

        // What is left holds a scaled share of 1, up to rounding: its entry takes its own light.
        while (largeCount > 0)
            SetOwn(large[--largeCount]);
        while (smallCount > 0)
            SetOwn(small[--smallCount]);
    }

    private void SetOwn(int entry) => samples[entry] = samples[entry] with { Alias = entry, Threshold = 1 };

    /// <summary>
    /// Keys this frame's lights, merges them with the last drawn frame's, which rise by slot, writes the remap of each of
    /// the last frame's indices, and writes this frame's keys into <paramref name="frame"/>. A light whose slot is not above
    /// the slot of every light before it has no key, so a reservoir that held it starts empty.
    /// </summary>
    private void JoinKeys(ReadOnlySpan<PreparedLight> lights, FrameHistory.PendingFrame frame)
    {
        ReadOnlySpan<KeptLight> last = frame.PreviousLights;
        remapCount = frame.PreviousLightCount;
        if (remap.Length < remapCount)
            Array.Resize(ref remap, Math.Max(remapCount, remap.Length * 2));
        remap.AsSpan(0, remapCount).Fill(NoLight);

        int keyedCount = 0;
        int next = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            var key = new LightKey(Slot: lights[i].EntityIndex, Generation: lights[i].EntityGeneration);
            if (keyedCount > 0 && key.Slot <= keyed[keyedCount - 1].Key.Slot)
                continue;
            keyed[keyedCount++] = new(Key: key, Index: i);
            while (next < last.Length && last[next].Key.Slot < key.Slot)
                next++;
            if (next < last.Length && last[next].Key == key && sampled[i])
                remap[last[next].Index] = i;
        }

        frame.WriteLights(count: lights.Length, keyed: keyed.AsSpan(0, keyedCount));
    }
}
