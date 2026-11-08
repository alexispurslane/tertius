namespace Tertius.Rendering;

/// <summary>
/// Whether the specular signal's reflected place competes with the surface place (SignalParameters.ReflectedPlaceBelow): a
/// debug switch of ticket tiunr-wk29, which tells the reflected place's hit-distance test apart from the rest of the
/// reprojection.
/// </summary>
internal enum DebugReflectedPlace
{
    /// <summary>The renderer's rule: below the hand-over band's start, the reflected place competes.</summary>
    On,

    /// <summary>The reflected place never competes: every pixel reads its surface place.</summary>
    Off,
}

/// <summary>The selection of <see cref="DebugReflectedPlace"/>, once per renderer.</summary>
internal static class DebugReflectedPlaceSelection
{
    extension(DebugReflectedPlace)
    {
        /// <summary>
        /// The rule that TERTIUS_DEBUG_REFLECTED_PLACE selects: unset, empty and "on" select the renderer's rule, "off" no
        /// reflected place.
        /// </summary>
        /// <exception cref="ArgumentException">The variable holds another value.</exception>
        public static DebugReflectedPlace Selected() =>
            Environment.GetEnvironmentVariable("TERTIUS_DEBUG_REFLECTED_PLACE") switch
            {
                null or "" or "on" => DebugReflectedPlace.On,
                "off" => DebugReflectedPlace.Off,
                var value => throw new ArgumentException(
                    $"Unknown TERTIUS_DEBUG_REFLECTED_PLACE value '{value}': use on or off."
                ),
            };
    }
}
