namespace SharedKernel.Security.Totp;

/// <summary>A recovery code as stored: never the code itself.</summary>
/// <param name="Id">A random identifier for the code.</param>
/// <param name="Lookup">
/// The code's first two characters, so redemption hashes only matching codes. Leaks 10 of the code's 50 bits, which a
/// slow hash still protects.
/// </param>
/// <param name="Hash">The PHC hash of the normalized code, from <c>IOneWayHasher</c>.</param>
public sealed record StoredRecoveryCode(string Id, string Lookup, string Hash);
