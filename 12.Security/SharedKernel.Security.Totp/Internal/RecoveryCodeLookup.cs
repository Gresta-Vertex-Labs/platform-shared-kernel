namespace SharedKernel.Security.Totp.Internal;

internal static class RecoveryCodeLookup
{
    private const int Length = 2;

    // The normalized code's first characters; an empty or short input gets a lookup no stored code has.
    public static string For(string normalizedCode) =>
        normalizedCode.Length >= Length ? normalizedCode[..Length] : string.Empty;
}
