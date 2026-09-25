using Grpc.Core;
using SharedKernel.Execution.Context;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Trivial server implementation of the test-only <c>TestService</c> (see <c>test.proto</c>) —
/// exists solely so this package's real-host integration tests have real RPCs to call. No
/// production behavior.
/// </summary>
internal sealed class TestServiceImpl : TestService.TestServiceBase
{
    public override Task<EchoReply> Echo(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = request.Value });

    public override Task<EchoReply> ThrowKnown(EchoRequest request, ServerCallContext context) =>
        throw new NotFoundException(Error.NotFound("test.not_found", "The requested test resource was not found."));

    public override Task<EchoReply> ThrowUnknown(EchoRequest request, ServerCallContext context) =>
        throw new InvalidOperationException("boom");

    public override async Task StreamThrowKnown(EchoRequest request, IServerStreamWriter<EchoReply> responseStream, ServerCallContext context)
    {
        await responseStream.WriteAsync(new EchoReply { Value = "first" });
        throw new ConflictException(Error.Conflict("test.conflict", "A conflicting test resource already exists."));
    }

    public override Task<ContextReply> GetContext(EchoRequest request, ServerCallContext context)
    {
        // Read from the call's ambient request context, which is what outbound clients inside the method see.
        var correlationId = RequestContextScope.Current?.CorrelationId ?? string.Empty;

        var tenantId = RequestContextScope.Current?.TenantId?.ToString() ?? string.Empty;

        return Task.FromResult(new ContextReply { CorrelationId = correlationId, TenantId = tenantId });
    }

    [RequireRole("admin")]
    public override Task<EchoReply> RequireRoleMethod(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = request.Value });

    [RequireFreshAuthentication(60)]
    public override Task<EchoReply> RequireFreshAuthMethod(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = request.Value });

    [RequireAuthenticationMethod("mfa")]
    public override Task<EchoReply> RequireAuthMethodMethod(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = request.Value });
}
