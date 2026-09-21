using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.Testing.Tests;

public sealed class Widget : AggregateRoot<Guid>
{
    public Widget(Guid id, string name) : base(id, new SystemClock()) => Name = name;

    public string Name { get; set; }
}

public sealed class TestRequestContextTests
{
    [Fact]
    public void Factories_ReportTheExpectedActor()
    {
        var tenant = Guid.NewGuid();

        TestRequestContext.ForUser().Should().BeEquivalentTo(new { IsAuthenticated = true, UserId = "test-user", TenantId = (Guid?)null, ActorKind = ActorKind.User });
        TestRequestContext.ForTenant(tenant, "ann").Should().BeEquivalentTo(new { UserId = "ann", TenantId = (Guid?)tenant, ActorKind = ActorKind.User });
        TestRequestContext.Service("billing-worker", tenant).Should().BeEquivalentTo(new { UserId = "billing-worker", ClientId = "billing-worker", ActorKind = ActorKind.Service });
        TestRequestContext.System("nightly-job").Should().BeEquivalentTo(new { IsAuthenticated = true, UserId = "nightly-job", ActorKind = ActorKind.System });
        TestRequestContext.Anonymous(tenant).Should().BeEquivalentTo(new { IsAuthenticated = false, UserId = (string?)null, TenantId = (Guid?)tenant, ActorKind = ActorKind.Anonymous });
    }

    [Fact]
    public async Task Permissions_AreFailClosed_AndOrdinal()
    {
        var context = TestRequestContext.ForUser();
        (await context.HasPermissionAsync("orders.read", CancellationToken.None)).Should().BeFalse();

        context.WithPermissions("orders.read").WithSession("s-1").WithImpersonator("support").WithTenant(Guid.Empty);

        (await context.HasPermissionAsync("orders.read", CancellationToken.None)).Should().BeTrue();
        (await context.HasPermissionAsync("ORDERS.READ", CancellationToken.None)).Should().BeFalse();
        context.SessionId.Should().Be("s-1");
        context.ImpersonatorId.Should().Be("support");
        ((IRequestContext)context).ActorKind.Should().Be(ActorKind.User);
    }
}

public sealed class FakeCrossTenantScopeTests
{
    [Fact]
    public void Enter_RecordsTheReason_AndNests()
    {
        var scope = new FakeCrossTenantScope();

        using (scope.Enter("report"))
        {
            scope.IsActive.Should().BeTrue();
            using (scope.Enter("nested"))
                scope.Depth.Should().Be(2);
        }

        scope.IsActive.Should().BeFalse();
        scope.EnteredReasons.Should().Equal("report", "nested");
        scope.ShouldHaveEntered("report");
        FluentActions.Invoking(() => scope.ShouldHaveEntered("other")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Enter_CanBeDenied_AndRequiresAReason()
    {
        var scope = new FakeCrossTenantScope { DenyWith = new UnauthorizedAccessException("no") };

        FluentActions.Invoking(() => scope.Enter("report")).Should().Throw<UnauthorizedAccessException>();
        scope.IsActive.Should().BeFalse();
        FluentActions.Invoking(() => new FakeCrossTenantScope().Enter(" ")).Should().Throw<ArgumentException>();

        scope.Reset();
        scope.ForceActive = true;
        scope.IsActive.Should().BeTrue();
    }

    [Fact]
    public void DisposingTwice_LeavesTheDepthAlone()
    {
        var scope = new FakeCrossTenantScope();
        var outer = scope.Enter("outer");
        var inner = scope.Enter("inner");

        inner.Dispose();
        inner.Dispose();

        scope.Depth.Should().Be(1);
        outer.Dispose();
    }
}

public sealed class PersistenceTestingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task TheFakes_ReplaceExistingRegistrations_AndWorkTogether()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(_ => throw new InvalidOperationException("the production unit of work"));

        var repository = services.AddFakeRepository<Widget, Guid>([new Widget(Guid.NewGuid(), "seeded")]);
        var unitOfWork = services.AddFakeUnitOfWork();
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(Guid.NewGuid()));
        var scope = services.AddFakeCrossTenantScope();
        var audit = services.AddFakeAuditTrailWriter();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRepository<Widget, Guid>>().Should().BeSameAs(repository);
        provider.GetRequiredService<IReadRepository<Widget, Guid>>().Should().BeSameAs(repository);
        provider.GetRequiredService<IUnitOfWork>().Should().BeSameAs(unitOfWork);
        provider.GetRequiredService<IRequestContext>().Should().BeSameAs(caller);
        provider.GetRequiredService<ICrossTenantScope>().Should().BeSameAs(scope);
        provider.GetRequiredService<IAuditTrailWriter>().Should().BeSameAs(audit);

        var id = Guid.NewGuid();
        await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            await provider.GetRequiredService<IRepository<Widget, Guid>>().AddAsync(new Widget(id, "new"), ct);
            unitOfWork.OnBeforeCommit(c => audit.RecordAsync(
                new AuditEntry { Action = "widget.create", ResourceType = "Widget", ResourceId = id.ToString(), Outcome = AuditOutcome.Succeeded }, c));
        });

        repository.Items.Should().HaveCount(2);
        unitOfWork.CommitCount.Should().Be(1);
        audit.ShouldHaveAudited("widget.create", "Widget", id.ToString());
    }
}
