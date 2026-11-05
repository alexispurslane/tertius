# Third-party notices

This file lists the third-party code that a build of the game carries, with the notices that its license asks for. `Tertius.Demo.csproj` copies it into the build and publish output. Jolt and joltc, Silk.NET, MoltenVK and the NuGet packages are not listed yet.

## XeGTAO

- Source: https://github.com/GameTechDev/XeGTAO, commit `a5b1686c7ea37788eeb3576b5be47f7c03db532c`.
- Used in: the GLSL port of ground-truth ambient occlusion, `src/Tertius.Rendering/Shaders/gtao.glsl`, which the four occlusion shaders include. The reference files are in `src/Tertius.Rendering/ThirdParty/XeGTAO`.
- License: MIT. The license asks that the notice goes with every substantial portion of the software, and the compiled shaders are such a portion.

```
MIT License

Copyright (C) 2016-2021, Intel Corporation 

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## AMD FidelityFX Super Resolution 3.1

- Source: https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, tag `v1.1.4`, commit `c6efa6bf7f2027b3ec94f28578bb5965eabb9e55`, with the FSR 3.1.4 upscaler.
- Used in: the FSR 3.1 upscaler. The build compiles AMD's GLSL passes and headers in `src/Tertius.Rendering/ThirdParty/Fsr3`, with two changed declarations and one line of AMD's 3.1.6 fix, which its README records. `Fsr3Upscaler` repeats AMD's host code, `ffx_fsr3upscaler.cpp`, in C#. The tool `tools/fsr3-golden/` builds AMD's host code from the same commit and does not ship.
- License: MIT. The license asks that the notice goes with every copy and every substantial portion of the software, and the compiled shaders and the C# copy of the host are such portions.

```
This file is part of the FidelityFX SDK.

Copyright (C) 2024 Advanced Micro Devices, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files(the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and /or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions :

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
