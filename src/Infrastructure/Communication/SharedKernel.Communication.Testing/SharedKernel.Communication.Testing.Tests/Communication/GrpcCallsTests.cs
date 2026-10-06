using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SharedKernel.Communication;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;
using GrpcCore = global::Grpc.Core;

namespace SharedKernel.Communication.Testing.Tests.Communication;

public sealed class GrpcCallsTests
{
    [Fact]
    public async Task A_successful_call_returns_its_response()
    {
        Result<StringValue> result = await GrpcCalls.Success(new StringValue { Value = "ok" }).ToResultAsync();

        result.Value.Value.Should().Be("ok");
    }

    [Fact]
    public async Task A_platform_failure_carries_the_errors_code_and_category()
    {
        Result<StringValue> result = await GrpcCalls
            .Failure<StringValue>(Error.NotFound("inventory.sku_not_found", "No such SKU."))
            .ToResultAsync();

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.NotFound, Code = "inventory.sku_not_found", Message = "No such SKU." });
    }

    [Fact]
    public async Task A_validation_failure_carries_its_field_errors()
    {
        Error invalid = Error.Validation([Error.Validation("quantity", "Must be positive.")]);

        Result<StringValue> result = await GrpcCalls.Failure<StringValue>(invalid).ToResultAsync();

        result.Error.Details.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Code = "quantity", Message = "Must be positive." });
    }

    [Fact]
    public async Task A_bare_failure_has_the_status_only()
    {
        Result<StringValue> result = await GrpcCalls.Failure<StringValue>(GrpcCore.StatusCode.ResourceExhausted, "slow down").ToResultAsync();

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "grpc.resource_exhausted", Message = "slow down" });
    }
}
