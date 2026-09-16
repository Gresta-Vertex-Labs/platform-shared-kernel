using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;

internal sealed class FakeResponse(int status = 200) : Response
{
    public static readonly FakeResponse Ok = new();

    public override int Status { get; } = status;

    public override string ReasonPhrase => "OK";

    public override Stream? ContentStream { get; set; }

    public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString();

    public override void Dispose()
    {
    }

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }

    protected override bool ContainsHeader(string name) => false;

    protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
}
