namespace SharedKernel.Security.Abstractions.Abstractions;

// Shared empty-dictionary singleton used by sentinel IUserContext implementations
// (AnonymousUserContext, SystemUserContext) to avoid a per-property-access allocation.
internal static class EmptyClaimsDictionary
{
    internal static readonly IReadOnlyDictionary<string, string> Instance = new Dictionary<string, string>(0);
}
