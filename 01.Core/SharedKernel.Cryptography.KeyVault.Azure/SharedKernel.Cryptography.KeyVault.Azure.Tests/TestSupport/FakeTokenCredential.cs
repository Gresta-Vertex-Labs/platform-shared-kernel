using Azure.Core;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;

/// <summary>
/// A trivial <see cref="TokenCredential"/> that returns a canned, never-validated access token
/// instantly — used only to avoid <see cref="Azure.Identity.DefaultAzureCredential"/>'s real
/// (slow, multi-source) credential-resolution chain in tests that only need the Azure SDK client
/// to attempt a real HTTP call, never a genuinely authenticated one. The Key Vault service (or,
/// for an unreachable-endpoint test, the TCP/TLS layer itself) rejects the request before any
/// server-side token validation would matter.
/// </summary>
internal sealed class FakeTokenCredential : TokenCredential
{
    private static readonly AccessToken Token = new("fake-test-token", DateTimeOffset.UtcNow.AddHours(1));

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(Token);
}
