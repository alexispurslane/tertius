// The sample age of a signal (docs/plans/bounce-light-confidence.md, section 8.5): the R16UI image per signal that the
// screen tracer's resolve writes and both dispatches of the accumulation read (TemporalSignal.SampleAge). It carries what
// the rays of the pixel's sample saw in this draw: bits 0 to 14 the youngest light code of those rays, and bit 15 the
// sample's motion bit. A light code is 0 for no light age, else the light age of the hit plus 1. The motion bit is set
// where one of the rays hit a moved surface, or where the pixel itself moves on screen by at least LeastMotion. The
// constants of the evidence that the spatial dispatch derives from it live here too, so that the writer and the readers
// share one layout. TemporalSignal.SampleMotionBit and TemporalSignal.LightCodeMask hold the same layout for the C#
// readers.
#ifndef TERTIUS_SAMPLE_AGE_GLSL
#define TERTIUS_SAMPLE_AGE_GLSL

// The sample's motion bit.
const uint SampleMotionBit = 0x8000u;

// The bits of the youngest light code.
const uint LightCodeMask = 0x7FFFu;

// The light ages below which a light change is recent: 0 to 6, the light codes 1 to 7. The record counts a change after
// it lasts 4 draws, so a recent change began less than 11 draws ago, and the fast history, which keeps (3 / 4)^k of a
// step after k draws, holds it within 5 % by the end (section 8.3).
const uint RecentLightAge = 7u;

// The least motion on screen that counts, in pixels per draw: the gate's threshold of ter-18hc.
const float LeastMotion = 0.0;

// The sample age of a sample whose rays bring the light code `lightCode`, with the motion bit where `motion` holds.
uint sampleAgeOf(uint lightCode, bool motion)
{
    return (lightCode & LightCodeMask) | (motion ? SampleMotionBit : 0u);
}

// The youngest light code of a sample age.
uint lightCodeOf(uint sampleAge)
{
    return sampleAge & LightCodeMask;
}

// Whether a sample age holds the motion bit.
bool sawMotion(uint sampleAge)
{
    return (sampleAge & SampleMotionBit) != 0u;
}

// Whether a light code is of a recent light change: a light age below RecentLightAge.
bool recentLightCode(uint lightCode)
{
    return lightCode != 0u && lightCode - 1u < RecentLightAge;
}

#endif
