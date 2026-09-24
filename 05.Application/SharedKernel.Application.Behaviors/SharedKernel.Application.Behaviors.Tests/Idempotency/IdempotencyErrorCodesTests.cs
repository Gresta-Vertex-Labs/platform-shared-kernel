using FluentAssertions;
using SharedKernel.Application.Behaviors.Idempotency;

namespace SharedKernel.Application.Behaviors.Tests.Idempotency;

/// <summary>Pins the wire values of <see cref="IdempotencyErrorCodes"/>: clients and dashboards branch on them.</summary>
public sealed class IdempotencyErrorCodesTests
{
    [Fact]
    public void Codes_HaveTheirDocumentedValues()
    {
        IdempotencyErrorCodes.KeyRequired.Should().Be("idempotency.key_required");
        IdempotencyErrorCodes.InProgress.Should().Be("idempotency.in_progress");
        IdempotencyErrorCodes.KeyReused.Should().Be("idempotency.key_reused");
    }
}
