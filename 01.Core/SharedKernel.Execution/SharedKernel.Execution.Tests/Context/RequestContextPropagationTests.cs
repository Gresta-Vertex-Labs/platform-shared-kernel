using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Execution.Tests.Context;

/// <summary>
/// P-566: the one header mapping every transport shares, and the correlation-id rules every inbound adapter applies.
/// </summary>
public sealed class RequestContextPropagationTests
{
    private static Dictionary<string, string> Write(IRequestContext? context, string? correlationId = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RequestContextPropagation.WriteHeaders(context, headers, static (h, k, v) => h[k] = v, correlationId);
        return headers;
    }

    private static string? Read(Dictionary<string, string> headers, string key)
        => headers.TryGetValue(key, out var value) ? value : null;

    [Fact]
    public void WriteHeaders_FullCaller_WritesEveryHeaderUnderItsWellKnownName()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var context = new PropagatedRequestContext(tenant, "user-7", ActorKind.User, "spa", "corr-abc");

        var headers = Write(context);

        headers.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [WellKnownHeaders.CorrelationId] = "corr-abc",
            [WellKnownHeaders.TenantId] = tenant.ToString(),
            [WellKnownHeaders.ActorId] = "user-7",
            [WellKnownHeaders.ActorKind] = "User",
            [WellKnownHeaders.ClientId] = "spa",
        });
    }

    [Fact]
    public void WriteHeaders_AnonymousWithoutTenant_WritesOnlyCorrelationAndActorKind()
    {
        var headers = Write(AnonymousRequestContext.Instance.WithCorrelationId("corr-1"));

        headers.Keys.Should().BeEquivalentTo([WellKnownHeaders.CorrelationId, WellKnownHeaders.ActorKind]);
        headers[WellKnownHeaders.ActorKind].Should().Be("Anonymous");
    }

    [Fact]
    public void WriteHeaders_NoContextNoCorrelation_WritesNothing()
    {
        Write(context: null).Should().BeEmpty();
    }

    [Fact]
    public void WriteHeaders_ExplicitCorrelationId_WinsOverTheContext()
    {
        var headers = Write(new PropagatedRequestContext(null, correlationId: "from-context"), "explicit");

        headers[WellKnownHeaders.CorrelationId].Should().Be("explicit");
    }

    /// <summary>
    /// Defect 4: the correlation id falls back to the value the inbound adapter put in baggage, never to the
    /// Activity's own id, which changes at every new trace.
    /// </summary>
    [Fact]
    public void WriteHeaders_ContextWithoutCorrelation_UsesBaggageNeverTheActivityId()
    {
        using var activity = new Activity("test").Start();
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "from-baggage");

        var headers = Write(AnonymousRequestContext.Instance);

        headers[WellKnownHeaders.CorrelationId].Should().Be("from-baggage");
        headers[WellKnownHeaders.CorrelationId].Should().NotBe(activity.Id);
    }

    [Fact]
    public void WriteHeaders_ActivityWithoutBaggage_WritesNoCorrelationRatherThanTheActivityId()
    {
        using var activity = new Activity("test").Start();

        Write(AnonymousRequestContext.Instance).Should().NotContainKey(WellKnownHeaders.CorrelationId);
    }

    [Fact]
    public void ReadHeaders_RoundTripsWhatWriteHeadersWrote()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var original = new PropagatedRequestContext(tenant, "svc-1", ActorKind.Service, "billing", "corr-xyz");

        var rebuilt = RequestContextPropagation.ReadHeaders(Write(original), Read);

        rebuilt.TenantId.Should().Be(tenant);
        rebuilt.UserId.Should().Be("svc-1");
        rebuilt.ActorKind.Should().Be(ActorKind.Service);
        rebuilt.ClientId.Should().Be("billing");
        rebuilt.CorrelationId.Should().Be("corr-xyz");
    }

    [Fact]
    public void ReadHeaders_MalformedValues_DegradeSafely()
    {
        var headers = new Dictionary<string, string>
        {
            [WellKnownHeaders.TenantId] = "not-a-guid",
            [WellKnownHeaders.ActorKind] = "7",
            [WellKnownHeaders.CorrelationId] = "bad value\nwith newline",
        };

        var rebuilt = RequestContextPropagation.ReadHeaders(headers, Read);

        rebuilt.TenantId.Should().BeNull();
        rebuilt.ActorKind.Should().Be(ActorKind.Anonymous);
        rebuilt.CorrelationId.Should().NotBe("bad value\nwith newline");
        CorrelationIds.IsValid(rebuilt.CorrelationId).Should().BeTrue("an invalid id is replaced by a new one");
    }

    [Fact]
    public void ReadHeaders_WithoutCorrelationAndCreationDisabled_LeavesItNull()
    {
        var rebuilt = RequestContextPropagation.ReadHeaders(new Dictionary<string, string>(), Read, createCorrelationId: false);

        rebuilt.CorrelationId.Should().BeNull();
    }

    [Theory]
    [InlineData("User", ActorKind.User)]
    [InlineData("Service", ActorKind.Service)]
    [InlineData("System", ActorKind.System)]
    [InlineData("user", ActorKind.Anonymous)]
    [InlineData("0", ActorKind.Anonymous)]
    [InlineData("99", ActorKind.Anonymous)]
    [InlineData("", ActorKind.Anonymous)]
    [InlineData(null, ActorKind.Anonymous)]
    public void ParseActorKind_AcceptsOnlyExactMemberNames(string? value, ActorKind expected)
    {
        RequestContextPropagation.ParseActorKind(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", true)]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV", true)]
    [InlineData("order-42:retry.1_a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("line\nbreak", false)]
    [InlineData("ünicode", false)]
    public void CorrelationIds_IsValid_AcceptsOnlySafeTokens(string? value, bool expected)
    {
        CorrelationIds.IsValid(value).Should().Be(expected);
    }

    [Fact]
    public void CorrelationIds_IsValid_RejectsOverlongValues()
    {
        CorrelationIds.IsValid(new string('a', CorrelationIds.MaxLength)).Should().BeTrue();
        CorrelationIds.IsValid(new string('a', CorrelationIds.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void CorrelationIds_AcceptOrCreate_KeepsValidAndReplacesInvalid()
    {
        CorrelationIds.AcceptOrCreate("keep-me").Should().Be("keep-me");
        Guid.TryParseExact(CorrelationIds.AcceptOrCreate("no way"), "D", out _).Should().BeTrue();
    }

    [Fact]
    public void WithCorrelationId_OverridesOnlyTheCorrelationId()
    {
        var tenant = new TenantId(Guid.NewGuid());
        IRequestContext inner = new SystemRequestContext(["p"], "job", tenant, "old");

        var derived = inner.WithCorrelationId("new");

        derived.CorrelationId.Should().Be("new");
        derived.TenantId.Should().Be(tenant);
        derived.UserId.Should().Be("job");
        derived.ActorKind.Should().Be(ActorKind.System);
    }

    [Fact]
    public async Task WithTenant_OverridesOnlyTheTenantAndForwardsPermissions()
    {
        var tenant = new TenantId(Guid.NewGuid());
        IRequestContext inner = new SystemRequestContext(["orders.read"], "job", tenantId: null, "corr");

        var derived = inner.WithTenant(tenant);

        derived.TenantId.Should().Be(tenant);
        derived.CorrelationId.Should().Be("corr");
        (await derived.HasPermissionAsync("orders.read", CancellationToken.None)).Should().BeTrue();
        inner.WithTenant(null).TenantId.Should().BeNull("a null override removes the tenant rather than keeping it");
    }

    [Fact]
    public void SystemRequestContext_CarriesItsCorrelationId()
    {
        new SystemRequestContext([], correlationId: "job-run-1").CorrelationId.Should().Be("job-run-1");
        new SystemRequestContext([]).CorrelationId.Should().BeNull();
    }
}
