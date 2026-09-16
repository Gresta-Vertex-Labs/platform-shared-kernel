namespace SharedKernel.Cryptography.Totp;

/// <summary>Generates one-time recovery codes for users who lose their authenticator.</summary>
/// <remarks>
/// Show the codes once. Store each one hashed with <c>IOneWayHasher</c> over <see cref="RecoveryCodeGenerator.Normalize"/>,
/// delete a code when it is used, and limit attempts as for any credential.
/// </remarks>
public interface IRecoveryCodeGenerator
{
    /// <summary>Generates <paramref name="count"/> distinct codes, formatted like <c>K7Q2M-XF4PA</c>.</summary>
    /// <param name="count">The number of codes, 1 to 50. Defaults to 10.</param>
    /// <returns>The codes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is outside 1 to 50.</exception>
    IReadOnlyList<string> GenerateCodes(int count = 10);
}
