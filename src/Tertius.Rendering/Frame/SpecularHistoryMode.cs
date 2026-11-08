namespace Tertius.Rendering;

/// <summary>The uncapped specular history and the explicit historical comparison controls.</summary>
internal enum SpecularHistoryMode
{
    Original,
    PreciseCapped,
    PreciseUncapped,
}

/// <summary>The selection of <see cref="SpecularHistoryMode"/>, once per renderer.</summary>
internal static class SpecularHistorySelection
{
    extension(SpecularHistoryMode mode)
    {
        /// <summary>
        /// Whether the specular history keeps the light-change age (ticket tiunr-7haj,
        /// <see cref="ScreenTraceImages.LightAge(int)"/>): every precise history does. The original history keeps H13's
        /// shaders, which have none.
        /// </summary>
        public bool KeepsLightAge => mode != SpecularHistoryMode.Original;
    }

    extension(SpecularHistoryMode)
    {
        /// <summary>
        /// The history that TERTIUS_SPECULAR_HISTORY selects: unset, empty and "precise-uncapped" the uncapped default,
        /// "precise-capped" its age limit and "original" H13's storage and shaders.
        /// </summary>
        /// <exception cref="ArgumentException">The variable holds another value.</exception>
        public static SpecularHistoryMode Selected() =>
            Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY") switch
            {
                null or "" or "precise-uncapped" => SpecularHistoryMode.PreciseUncapped,
                "original" => SpecularHistoryMode.Original,
                "precise-capped" => SpecularHistoryMode.PreciseCapped,
                var value => throw new ArgumentException($"Unknown TERTIUS_SPECULAR_HISTORY value '{value}'."),
            };
    }
}
