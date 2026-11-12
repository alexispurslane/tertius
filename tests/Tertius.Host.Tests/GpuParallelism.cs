// At most 10 tests run at once, each with its own renderer. xunit's default runs one test per processor, so the count
// of renderers grows with the machine's cores while the GPU's memory does not: on the 48-core worker of the cloud, the
// RTX 2000 Ada's 16 GB ran out at the default (ticket ter-net6). 10 is the default of the 10-core Mac, so its runs keep
// their speed.
[assembly: Xunit.v3.Parallelization(MaxThreads = 10)]
