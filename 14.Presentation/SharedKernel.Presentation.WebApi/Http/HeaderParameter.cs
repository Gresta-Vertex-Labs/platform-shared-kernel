using System.Reflection;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Tells from a handler parameter whether the header it binds is optional, for the <see cref="IdempotencyKey"/> and
/// <see cref="IfMatch{TVersion}"/> parameters to add accepted or required metadata.
/// </summary>
internal static class HeaderParameter
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="parameter"/> is declared nullable (<c>IdempotencyKey? key</c>),
    /// which makes its header optional. Declared not-null, or in code compiled without nullable annotations, the header
    /// is required: the safe reading of an intent that cannot be told.
    /// </summary>
    public static bool IsOptional(ParameterInfo parameter)
    {
        try
        {
            return new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;
        }
        catch (InvalidOperationException)
        {
            // Nullability information is switched off (the System.Reflection.NullabilityInfoContext.IsSupported switch,
            // which trimming may turn off): the declaration cannot be read.
            return false;
        }
    }
}
