// netstandard2.0 has no IsExternalInit, which the compiler needs for the init accessors of records.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
