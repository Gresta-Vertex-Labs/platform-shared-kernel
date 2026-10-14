using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Communication.Grpc.Internal;
using SharedKernel.Communication.Internal;
using SharedKernel.Communication.Grpc.Tests.Probe;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Logging;
using ProtoMoney = Google.Type.Money;

namespace SharedKernel.Communication.Grpc.Tests;

/// <summary>
/// The observability and error-code conformance of the gRPC client: the propagation log event, the money error, the
/// status codes and the cross-domain contracts of the reverse maps.
/// </summary>
public sealed class GrpcConformanceTests
{
    private static readonly TenantId Tenant = TenantId.Parse("7f0c0f0e-5c1a-4a8e-9a55-2b3c4d5e6f70");

    private static Action<IServiceCollection> Capture(InMemoryLoggerFactory logs, bool failingCaller = false) => services =>
    {
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(logs));
        if (failingCaller)
        {
            services.Replace(ServiceDescriptor.Singleton<IRequestContextAccessor>(new ThrowingAccessor()));
        }
    };

    private static IReadOnlyList<LogRecord> KernelRecords(InMemoryLoggerFactory logs) =>
        [.. logs.Loggers.Values.SelectMany(l => l.Records).Where(r => r.EventId.Id >= LoggingEventIdRanges.Communication)];

    // --- Event ids ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_grpc_event_id_is_pinned_and_lives_in_the_grpc_sub_block()
    {
        CommunicationGrpcLoggingEventIds.PropagationFailed.Should().Be(11100);

        int[] ids = LoggerMessageIds(typeof(GrpcClientOptions).Assembly);
        ids.Should().Equal(11100);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Communication, 1));
    }

    [Fact]
    public void The_base_event_ids_are_unchanged_and_live_in_the_base_sub_block()
    {
        CommunicationLoggingEventIds.TokenAcquired.Should().Be(11000);
        CommunicationLoggingEventIds.TokenRequestRefused.Should().Be(11001);
        CommunicationLoggingEventIds.TokenEndpointUnreachable.Should().Be(11002);
        CommunicationLoggingEventIds.AccessTokenUnavailable.Should().Be(11003);
        CommunicationLoggingEventIds.RetryingWithNewAccessToken.Should().Be(11004);

        int[] ids = LoggerMessageIds(typeof(CommunicationOptions).Assembly);
        ids.Should().BeEquivalentTo([11000, 11001, 11002, 11003, 11004]);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Communication, 0));
    }

    [Fact]
    public void The_retired_sub_block_is_unused_and_no_id_repeats_across_the_base_and_grpc_assemblies()
    {
        int[] all =
        [
            .. LoggerMessageIds(typeof(CommunicationOptions).Assembly),
            .. LoggerMessageIds(typeof(GrpcClientOptions).Assembly),
        ];

        all.Should().OnlyHaveUniqueItems();
        all.Should().NotContain(id => id >= 11300 && id <= 11399);
        all.Should().OnlyContain(id => id >= LoggingEventIdRanges.Communication && id < LoggingEventIdRanges.Communication + 1000);
    }

    private static int[] LoggerMessageIds(Assembly assembly) =>
        [.. assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(a => a.EventId)
            .Order()];

    // --- 11100: the interceptor -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_propagation_failure_logs_11100_with_its_exception_and_the_call_still_goes_out()
    {
        var logs = new InMemoryLoggerFactory();
        await using var harness = await GrpcHarness.StartAsync(services: Capture(logs, failingCaller: true));

        EchoReply reply = await harness.Client.EchoAsync(new EchoRequest { Text = "propagation-failure" });

        reply.Text.Should().Be("propagation-failure");
        LogRecord record = KernelRecords(logs).ShouldHaveLogged(new EventId(11100), LogLevel.Error);
        record.Exception.Should().BeOfType<InvalidOperationException>();
        record.Message.Should().Contain("'/");
        KernelRecords(logs).ShouldHaveLoggedCount(new EventId(11100), 1);
        logs.ShouldHaveCategory(typeof(RequestContextInterceptor));
    }

    [Fact]
    public async Task Every_event_follows_the_log_conventions_and_leaks_no_caller()
    {
        var logs = new InMemoryLoggerFactory();
        await using var harness = await GrpcHarness.StartAsync(services: Capture(logs, failingCaller: true));

        await harness.Client.EchoAsync(new EchoRequest { Text = "propagation-failure-2" });

        KernelRecords(logs)
            .ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Communication, 1)
            .ShouldNotLeak(Tenant.ToString(), "actor-canary-1");
    }

    [Fact]
    public async Task A_working_caller_with_tenant_actor_and_client_ids_logs_nothing()
    {
        var logs = new InMemoryLoggerFactory();
        await using var harness = await GrpcHarness.StartAsync(services: Capture(logs));
        var caller = TestRequestContext.ForTenant(Tenant, "actor-canary-1");

        using (RequestContextScope.Begin(caller))
        {
            await harness.Client.EchoAsync(new EchoRequest { Text = "propagation-ok" });
        }

        KernelRecords(logs).Should().BeEmpty();
    }

    // --- Money and the status codes ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(123456789L, -5)]
    [InlineData(-987654321L, 7)]
    [InlineData(1L, 1_000_000_000)]
    public void An_invalid_money_message_is_a_validation_error_whose_message_has_no_amounts(long units, int nanos)
    {
        var money = new ProtoMoney { Units = units, Nanos = nanos, CurrencyCode = "EUR" };

        Error error = money.ToMoney().Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(CommunicationErrorCodes.GrpcInvalidMoney);
        error.Code.Should().Be("communication.grpc.invalid_money");
        error.Type.Should().Be(ErrorType.Validation);
        error.Message.Should().NotContain(units.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .And.NotContain(nanos.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .And.EndWith(".");

        Action read = () => money.ToDecimal();
        read.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(error.Message)
            .And.NotContain(units.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_status_without_error_info_still_reads_its_grpc_name_from_the_constants()
    {
        await using var harness = await GrpcHarness.StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "bare" }).ToResultAsync();

        reply.Error.Code.Should().Be(CommunicationErrorCodes.GrpcStatus.FailedPrecondition);
    }

    // --- Cross-domain contracts -------------------------------------------------------------------------------------

    [Fact]
    public async Task An_error_info_reason_unexpected_default_passes_through_as_unexpected()
    {
        await using var harness = await GrpcHarness.StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "unexpected" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = "unexpected.default" });
    }

    [Fact]
    public async Task A_deadline_exceeded_commit_outcome_unknown_is_a_timeout_and_is_not_retried_by_the_channel()
    {
        await using var harness = await GrpcHarness.StartAsync();
        int before = ProbeService.AttemptsFor("commit-unknown");

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "commit-unknown" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Timeout, Code = "execution.commit_outcome_unknown" });
        (ProbeService.AttemptsFor("commit-unknown") - before).Should().Be(1);
    }

    [Fact]
    public void The_retired_code_names_survive_nowhere_in_the_compiled_assembly()
    {
        byte[] assembly = File.ReadAllBytes(typeof(GrpcClientOptions).Assembly.Location);

        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("The gRPC call was canceled by the caller.")).Should().BeGreaterThan(0, "the scan must find a literal that is there");
        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("communication.unreachable")).Should().Be(-1);
        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("money.proto.invalid")).Should().Be(-1);
    }

    private sealed class ThrowingAccessor : IRequestContextAccessor
    {
        public IRequestContext? Current => throw new InvalidOperationException("The caller is not available.");
    }
}
