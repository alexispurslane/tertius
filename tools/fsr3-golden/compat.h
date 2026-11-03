// Tool code (tools/README.md, "FSR 3.1 golden values"): the few Windows C runtime names that AMD's host code of FSR 3.1.4
// uses, for a build with clang on macOS or Linux. The build force-includes this header before each of AMD's files, so
// that AMD's sources stay unchanged. The build also takes -fshort-wchar: AMD's context holds wide-character names, and
// only with Windows' 2-byte wchar_t does it fit the opaque context of FFX_FSR3UPSCALER_CONTEXT_SIZE words. So nothing
// here calls the C library's wide-string functions, which expect 4-byte characters.
#pragma once

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <cwchar>

#ifndef _countof
#define _countof(array) (sizeof(array) / sizeof((array)[0]))
#endif

inline int wcscpy_s(wchar_t* destination, size_t size, const wchar_t* source)
{
    size_t length = 0;
    for (; length + 1 < size && source[length] != L'\0'; ++length)
        destination[length] = source[length];
    destination[length] = L'\0';
    return 0;
}

template <size_t Size> inline int wcscpy_s(wchar_t (&destination)[Size], const wchar_t* source)
{
    return wcscpy_s(destination, Size, source);
}

// ffx_message.cpp uses FFX_UNUSED without its header outside Windows.
#ifdef __cplusplus
#include <FidelityFX/host/ffx_util.h>
#endif
