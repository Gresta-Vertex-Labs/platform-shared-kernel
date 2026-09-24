// netstandard2.0 has no IsExternalInit, which the compiler needs for records and init accessors.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
