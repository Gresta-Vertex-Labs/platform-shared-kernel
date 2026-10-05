using Azure.Core;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;

internal sealed class FakeTokenCredential : TokenCredential
{
    public int Calls;

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new AccessToken("fake-token", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}
