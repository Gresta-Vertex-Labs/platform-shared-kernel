using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Idempotency.Abstractions.Tests;

public sealed class IdempotencyStoreUnavailableExceptionTests
{
    private const string CanonicalMessage = "The idempotency store is unavailable.";

    [Fact]
    public void ConvenienceConstructor_BuildsTheCanonicalError_AndKeepsTheInnerException()
    {
        var inner = new TimeoutException("provider text with host redis.internal:6379");

        var exception = new IdempotencyStoreUnavailableException(inner);

        Assert.Equal(ErrorCodes.Idempotency.StoreUnavailable, exception.Error.Code);
        Assert.Equal("idempotency.store_unavailable", exception.Error.Code);
        Assert.Equal(ErrorType.Unavailable, exception.Error.Type);
        Assert.Equal(CanonicalMessage, exception.Error.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void Message_IsTheProviderNeutralSentence_WithNoInnerText()
    {
        var exception = new IdempotencyStoreUnavailableException(new InvalidOperationException("secret-inner-text"));

        Assert.Equal(CanonicalMessage, exception.Message);
        Assert.DoesNotContain("secret-inner-text", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-inner-text", exception.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorConstructor_CarriesTheSuppliedError_WithoutAnInnerException()
    {
        var error = Error.Unavailable(ErrorCodes.Idempotency.StoreUnavailable, "Custom sentence.");

        var exception = new IdempotencyStoreUnavailableException(error);

        Assert.Same(error, exception.Error);
        Assert.Equal("Custom sentence.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ErrorAndInnerConstructor_CarriesBoth()
    {
        var error = Error.Unavailable(ErrorCodes.Idempotency.StoreUnavailable, "Custom sentence.");
        var inner = new InvalidOperationException("inner");

        var exception = new IdempotencyStoreUnavailableException(error, inner);

        Assert.Same(error, exception.Error);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void Constructors_RejectANullError() =>
        Assert.Throws<ArgumentNullException>(() => new IdempotencyStoreUnavailableException((Error)null!));

    [Fact]
    public void Exception_IsASharedKernelException_ExposingTheErrorSurface()
    {
        var exception = new IdempotencyStoreUnavailableException(new TimeoutException());

        Assert.IsAssignableFrom<SharedKernelException>(exception);
        var hasError = Assert.IsAssignableFrom<IHasError>(exception);
        Assert.Same(exception.Error, hasError.Error);
    }
}

public sealed class IdempotencyPurposesTests
{
    [Theory]
    [InlineData(IdempotencyPurpose.Request)]
    [InlineData(IdempotencyPurpose.Message)]
    public void ThrowIfUndefined_ForADefinedValue_DoesNotThrow(IdempotencyPurpose purpose) =>
        IdempotencyPurposes.ThrowIfUndefined(purpose);

    [Fact]
    public void ThrowIfUndefined_ForAnUndefinedValue_ThrowsNamingTheValueAndTheArgument()
    {
        var purpose = (IdempotencyPurpose)9;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => IdempotencyPurposes.ThrowIfUndefined(purpose));

        Assert.Equal("purpose", exception.ParamName);
        Assert.Contains("'9' is not a defined IdempotencyPurpose value.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIdempotencyStore_ForAnUndefinedPurpose_UsesTheSharedGuard()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddIdempotencyStore<NullStore>((IdempotencyPurpose)7));

        Assert.Contains("'7' is not a defined IdempotencyPurpose value.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIdempotencyStore_TwiceForOnePurpose_ThrowsTheExactSentence()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NullStore>(IdempotencyPurpose.Request);

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddIdempotencyStore<NullStore>(IdempotencyPurpose.Request));

        Assert.Equal(
            "An IIdempotencyStore is already registered for IdempotencyPurpose 'Request'. Register one store per purpose.",
            exception.Message);
    }

    private sealed class NullStore : IIdempotencyStore
    {
        public Task<IdempotencyReservation> TryBeginAsync(
            IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken) =>
            Task.FromResult(IdempotencyReservation.InProgress());

        public Task<bool> CompleteAsync(
            IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ReleaseAsync(
            IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}

public sealed class IdempotencyTelemetryNamesTests
{
    [Fact]
    public void SourceAndMeter_AreTheDomainName()
    {
        Assert.Equal("SharedKernel.Idempotency", IdempotencyTelemetry.SourceName);
        Assert.Equal("SharedKernel.Idempotency", IdempotencyTelemetry.MeterName);
    }

    [Fact]
    public void SpanNames_AreLowercaseCapabilityOperation()
    {
        Assert.Equal("idempotency begin", IdempotencyTelemetry.BeginSpanName);
        Assert.Equal("idempotency complete", IdempotencyTelemetry.CompleteSpanName);
        Assert.Equal("idempotency release", IdempotencyTelemetry.ReleaseSpanName);
    }

    [Fact]
    public void Instruments_AreTheSharedKernelIdempotencyNames()
    {
        Assert.Equal("sharedkernel.idempotency.operation.duration", IdempotencyTelemetry.OperationDurationInstrument);
        Assert.Equal("sharedkernel.idempotency.fail_open", IdempotencyTelemetry.FailOpenInstrument);
    }

    [Fact]
    public void TagKeys_AreDomainPrefixedSnakeCase()
    {
        Assert.Equal("idempotency.purpose", IdempotencyTagKeys.Purpose);
        Assert.Equal("idempotency.operation", IdempotencyTagKeys.Operation);
        Assert.Equal("idempotency.result", IdempotencyTagKeys.Result);
        Assert.Equal("idempotency.provider", IdempotencyTagKeys.Provider);
        Assert.Equal("idempotency.fail_open", IdempotencyTagKeys.FailOpen);
    }

    [Fact]
    public void OperationAndPurposeValues_ArePinned()
    {
        Assert.Equal(["begin", "complete", "release"],
            new[] { IdempotencyTelemetry.OperationBegin, IdempotencyTelemetry.OperationComplete, IdempotencyTelemetry.OperationRelease });
        Assert.Equal("request", IdempotencyTelemetry.PurposeRequest);
        Assert.Equal("message", IdempotencyTelemetry.PurposeMessage);
    }

    [Fact]
    public void ResultSet_IsClosedAndPinned()
    {
        Assert.Equal(
            ["started", "in_progress", "completed", "fingerprint_mismatch", "applied", "lost", "fail_open"],
            new[]
            {
                IdempotencyTelemetry.ResultStarted,
                IdempotencyTelemetry.ResultInProgress,
                IdempotencyTelemetry.ResultCompleted,
                IdempotencyTelemetry.ResultFingerprintMismatch,
                IdempotencyTelemetry.ResultApplied,
                IdempotencyTelemetry.ResultLost,
                IdempotencyTelemetry.ResultFailOpen,
            });
    }
}
