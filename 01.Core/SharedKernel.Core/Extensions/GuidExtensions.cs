namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="Guid"/>.
/// </summary>
public static class GuidExtensions
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="value"/> equals <see cref="Guid.Empty"/>.
    /// </summary>
    /// <param name="value">The GUID to test.</param>
    public static bool IsEmpty(this Guid value) => value == Guid.Empty;
}
