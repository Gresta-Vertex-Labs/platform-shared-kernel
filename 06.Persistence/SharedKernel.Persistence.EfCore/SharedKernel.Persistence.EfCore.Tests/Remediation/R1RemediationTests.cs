using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Transactions;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Remediation;

/// <summary>Regression tests for the wave-3b final review findings fixed by stream R1 (unit lane, SQLite).</summary>
public sealed class R1RemediationTests : IDisposable
{
    private static readonly IClock Clock = new SystemClock();
    private readonly R1Database _database = new();

    public void Dispose() => _database.Dispose();

    private R1PlainContext Plain(IDomainEventDispatcher? dispatcher = null) =>
        _database.Create<R1PlainContext>((o, d) => new R1PlainContext(o, d), dispatcher: dispatcher);

    private R1TenantedContext Tenanted(TenantId? tenantId) =>
        _database.Create<R1TenantedContext>((o, d) => new R1TenantedContext(o, d), new FakeAuditActorContext("actor", tenantId) { TenantId = tenantId });

    // ---- C6: a failed joined operation never commits ----

    [Fact]
    public async Task C6_JoinedFailedResult_RollsBackTheOuterTransaction_AndThrowsWhenTheOuterSucceeds()
    {
        await using var context = Plain();
        var unitOfWork = EfUnitOfWork.For(context);

        var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Items.Add(new R1Item(R1ItemId.New(), "outer", Clock));
            var nested = await unitOfWork.ExecuteInTransactionAsync(_ =>
            {
                context.Items.Add(new R1Item(R1ItemId.New(), "nested", Clock));
                return Task.FromResult(Result.Failure(Error.Validation("r1.nested", "nested failed")));
            }, ct);

            nested.IsFailure.Should().BeTrue();
            return Result.Success(); // the outer handler ignores the nested failure
        });

        await act.Should().ThrowAsync<TransactionRolledBackException>();

        await using var verify = Plain();
        (await verify.Items.CountAsync()).Should().Be(0, "failed nested work must never commit with the outer work");
    }

    [Fact]
    public async Task C6_JoinedException_CaughtByTheOuterOperation_StillRollsBack()
    {
        await using var context = Plain();
        var unitOfWork = EfUnitOfWork.For(context);

        var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Items.Add(new R1Item(R1ItemId.New(), "outer", Clock));
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(_ => throw new InvalidProgramException("nested"), ct);
            }
            catch (InvalidProgramException)
            {
                // swallowed by the outer handler
            }
        });

        await act.Should().ThrowAsync<TransactionRolledBackException>();

        await using var verify = Plain();
        (await verify.Items.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task C6_JoinedFailure_WithAFailedOuterResult_ReturnsTheOuterFailure()
    {
        await using var context = Plain();
        var unitOfWork = EfUnitOfWork.For(context);

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Items.Add(new R1Item(R1ItemId.New(), "outer", Clock));
            var nested = await unitOfWork.ExecuteInTransactionAsync(
                _ => Task.FromResult(Result.Failure(Error.Validation("r1.nested", "nested failed"))), ct);
            return nested;
        });

        result.IsFailure.Should().BeTrue();
        await using var verify = Plain();
        (await verify.Items.CountAsync()).Should().Be(0);
    }

    // ---- C2 (ambient transaction is restored, not cleared) ----

    [Fact]
    public async Task C2_AmbientTransaction_IsRestoredAfterAJoinedCall_AndClearedAfterTheOutermost()
    {
        await using var context = Plain();
        var ambient = new AmbientDbTransactionAccessor();
        var unitOfWork = EfUnitOfWork.For(context, ambientTransaction: ambient);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            ambient.Current.Should().NotBeNull();
            var outer = ambient.Current;

            await unitOfWork.ExecuteInTransactionAsync(_ => Task.CompletedTask, ct);

            ambient.Current.Should().Be(outer, "a joined call must not clear the outer transaction");
        });

        ambient.Current.Should().BeNull();
    }

    // ---- C8: a throwing dispatcher never loses events silently ----

    [Fact]
    public async Task C8_DirectSave_WithAThrowingDispatcher_ClearsTheTracker_SoNothingIsSavedWithoutItsEvents()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var context = Plain(dispatcher);
        var item = new R1Item(R1ItemId.New(), "with event", Clock);
        item.Raise();
        context.Items.Add(item);

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidProgramException>();

        context.ChangeTracker.Entries().Should().BeEmpty("the save was abandoned as a whole");
        (await context.SaveChangesAsync()).Should().Be(0, "a later save must not persist the change without its event");

        await using var verify = Plain();
        (await verify.Items.CountAsync()).Should().Be(0);
    }

    // ---- C7: keyset on a nullable key is refused ----

    [Fact]
    public async Task C7_KeysetPaging_OnANullableColumn_IsRejectedWithAClearError()
    {
        await using var context = Plain();
        var repository = new EfReadRepository<R1Item, R1ItemId>(context);

        var act = () => repository.ListKeysetAsync(
            new AllItems(), CursorPageRequest.Create(null, 10).Value, i => i.Nickname!);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Nickname*nullable*");
    }

    [Fact]
    public async Task C7_KeysetPaging_OnARequiredColumn_StillWorks()
    {
        await using var context = Plain();
        context.Items.Add(new R1Item(R1ItemId.New(), "a", Clock));
        await context.SaveChangesAsync();

        var page = await new EfReadRepository<R1Item, R1ItemId>(context)
            .ListKeysetAsync(new AllItems(), CursorPageRequest.Create(null, 10).Value, i => i.Name);

        page.Items.Should().ContainSingle();
    }

    // ---- F4: Money is mapped without configuration ----

    [Fact]
    public async Task F4_Money_IsMappedByConvention_AsTwoColumns_RequiredUnlessNullable()
    {
        await using var context = Plain();
        var entityType = context.Model.FindEntityType(typeof(R1Item))!;

        var price = entityType.FindComplexProperty(nameof(R1Item.Price));
        price.Should().NotBeNull("a Money property needs no builder.Money(...) call");
        price!.IsNullable.Should().BeFalse();
        price.ComplexType.FindProperty(nameof(Money.Amount))!.GetPrecision().Should().Be(19);
        price.ComplexType.FindProperty(nameof(Money.Amount))!.GetScale().Should().Be(4);
        price.ComplexType.FindProperty(nameof(Money.Currency))!.GetMaxLength().Should().Be(3);

        entityType.FindComplexProperty(nameof(R1Item.Discount))!.IsNullable.Should().BeTrue();

        var id = R1ItemId.New();
        context.Items.Add(new R1Item(id, "priced", Clock) { Price = Money.Create(12.5m, Currency.Usd).Value });
        await context.SaveChangesAsync();

        await using var verify = Plain();
        var loaded = await verify.Items.SingleAsync(i => i.Id == id);
        loaded.Price.Should().Be(Money.Create(12.5m, Currency.Usd).Value);
        loaded.Discount.Should().BeNull();
    }

    // ---- S1: every entity type of a tenanted model is tenant data ----

    [Fact]
    public void S1_AnEntityTypeWithoutIHasTenant_FailsTheModelBuild_UnlessDeclaredTenantShared()
    {
        var act = () => _database.Create<R1InvalidTenantedContext>((o, d) => new R1InvalidTenantedContext(o, d), new FakeAuditActorContext());

        act.Should().Throw<InvalidOperationException>().WithMessage("*R1Untenanted*IHasTenant*TenantShared*");
    }

    [Fact]
    public async Task S1_TenantSharedTypes_AreAllowed_AndChildrenGetTheTenantFilterAndConcurrencyToken()
    {
        await using var context = Tenanted(new TenantId(Guid.NewGuid()));
        var line = context.Model.FindEntityType(typeof(R1OrderLine))!;

        line.GetDeclaredQueryFilters().Select(f => f.Key).Should().Contain(SharedKernel.Persistence.EfCore.Context.PersistenceFilterNames.Tenant);
        line.FindProperty(nameof(IHasTenant.TenantId))!.IsConcurrencyToken.Should().BeTrue();
        context.Model.FindEntityType(typeof(R1Country))!.GetDeclaredQueryFilters().Should().BeEmpty();
        context.Model.FindEntityType(typeof(R1Currency))!.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public async Task S1_ChildRows_AreFilteredByTenant_AndAnAddedChildTakesItsOrdersTenant()
    {
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        var lineOfB = Guid.NewGuid();

        await using (var asB = Tenanted(tenantB))
        {
            asB.Orders.Add(new R1Order { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B", Lines = [new R1OrderLine { Id = lineOfB, Text = "secret" }] });
            await asB.SaveChangesAsync();
            (await asB.Lines.SingleAsync(l => l.Id == lineOfB)).TenantId.Should().Be(tenantB, "the added child took its order's tenant");
        }

        await using var asA = Tenanted(tenantA);
        (await asA.Lines.CountAsync()).Should().Be(0, "tenant A must not read tenant B's child rows");
    }

    [Fact]
    public async Task S1_DetachedGraph_CarryingAnotherTenantsChild_IsRejected_AndTheChildIsUntouched()
    {
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        var orderA = Guid.NewGuid();
        var lineOfB = Guid.NewGuid();

        await using (var asB = Tenanted(tenantB))
        {
            asB.Orders.Add(new R1Order { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B", Lines = [new R1OrderLine { Id = lineOfB, TenantId = tenantB, Text = "victim" }] });
            await asB.SaveChangesAsync();
        }

        await using (var asA = Tenanted(tenantA))
        {
            asA.Orders.Add(new R1Order { Id = orderA, TenantId = tenantA, Name = "A" });
            await asA.SaveChangesAsync();
        }

        await using (var attacker = Tenanted(tenantA))
        {
            // The hijack of the review: the attacker's own order carrying the victim's line id. With the child's own
            // TenantId stamped as the attacker's, the row (owned by B) matches nothing; with B's id, the guard refuses.
            attacker.Orders.Update(new R1Order
            {
                Id = orderA, TenantId = tenantA, Name = "A2",
                Lines = [new R1OrderLine { Id = lineOfB, OrderId = orderA, TenantId = tenantA, Text = "overwritten" }],
            });

            var act = () => attacker.SaveChangesAsync();
            await act.Should().ThrowAsync<SharedKernelException>();
        }

        await using var verify = Tenanted(tenantB);
        var line = await verify.Lines.SingleAsync(l => l.Id == lineOfB);
        line.Text.Should().Be("victim");
    }

    [Fact]
    public void S1_RowLevelSecurityForModel_CoversEveryTenantTable_ChildrenIncluded()
    {
        using var context = Tenanted(new TenantId(Guid.NewGuid()));
        var tables = RowLevelSecurityMigrationBuilderExtensions.TenantTables(context.GetService<IDesignTimeModel>().Model)
            .Select(t => t.Table).ToList();

        tables.Should().BeEquivalentTo(["Orders", "Lines"], "a tenant-shared type has no tenant column to protect");

        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.EnableTenantRowLevelSecurityForModel(context.GetService<IDesignTimeModel>().Model);
        var sql = migration.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
        sql.Should().Contain(s => s.Contains("\"Lines\"", StringComparison.Ordinal) && s.Contains("FORCE ROW LEVEL SECURITY", StringComparison.Ordinal));
    }

    // ---- S6: direct ExecuteUpdate cannot set protected columns ----

    [Fact]
    public async Task S6_DirectExecuteUpdate_OnTheTenantColumn_IsRejected()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var context = Tenanted(tenant);

        var act = () => context.Orders.Where(o => o.Name == "x")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.TenantId, new TenantId(Guid.NewGuid())));
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*TenantId*another tenant*");

        var viaEfProperty = () => context.Orders.Where(o => o.Name == "x")
            .ExecuteUpdateAsync(s => s.SetProperty(o => EF.Property<TenantId>(o, nameof(IHasTenant.TenantId)), new TenantId(Guid.NewGuid())));
        await viaEfProperty.Should().ThrowAsync<InvalidOperationException>();

        (await context.Orders.Where(o => o.Name == "x").ExecuteUpdateAsync(s => s.SetProperty(o => o.Name, "y")))
            .Should().Be(0, "an ordinary column stays settable");
    }

    // ---- S8: no existence oracle ----

    [Fact]
    public async Task S8_AnotherTenantsRow_AndAMissingRow_GetTheSameAnswer()
    {
        var victimTenant = new TenantId(Guid.NewGuid());
        var attackerTenant = new TenantId(Guid.NewGuid());
        var victimOrder = Guid.NewGuid();

        await using (var asVictim = Tenanted(victimTenant))
        {
            asVictim.Orders.Add(new R1Order { Id = victimOrder, TenantId = victimTenant, Name = "victim" });
            await asVictim.SaveChangesAsync();
        }

        async Task<ConflictException> AttemptAsync(Guid id)
        {
            await using var attacker = Tenanted(attackerTenant);
            attacker.Orders.Update(new R1Order { Id = id, TenantId = attackerTenant, Name = "probe" });
            return (await FluentActions.Awaiting(() => attacker.SaveChangesAsync()).Should().ThrowAsync<ConflictException>()).Which;
        }

        var existing = await AttemptAsync(victimOrder);
        var missing = await AttemptAsync(Guid.NewGuid());

        existing.Error.Should().Be(missing.Error);
        existing.Message.Should().Be(missing.Message);
        ConcurrencyVersion.TryGetCurrentVersion(existing, out _).Should().BeFalse();
    }

    // ---- F15: actionable tenant-write messages ----

    [Fact]
    public async Task F15_NoTenant_AndOtherTenant_AreDistinguished()
    {
        await using (var noTenant = Tenanted(tenantId: null))
        {
            noTenant.Orders.Add(new R1Order { Id = Guid.NewGuid(), TenantId = new TenantId(Guid.NewGuid()), Name = "x" });
            var ex = (await FluentActions.Awaiting(() => noTenant.SaveChangesAsync()).Should().ThrowAsync<ForbiddenException>()).Which;
            ex.Error.Message.Should().Contain("has no tenant").And.Contain("ICrossTenantScope");
        }

        await using var other = Tenanted(new TenantId(Guid.NewGuid()));
        other.Orders.Add(new R1Order { Id = Guid.NewGuid(), TenantId = new TenantId(Guid.NewGuid()), Name = "x" });
        var otherEx = (await FluentActions.Awaiting(() => other.SaveChangesAsync()).Should().ThrowAsync<ForbiddenException>()).Which;
        otherEx.Error.Message.Should().Contain("not the caller's tenant");
        otherEx.Error.Code.Should().Be(TenantIsolationErrors.Code);
    }

    // ---- F1: the startup signal ----

    [Fact]
    public async Task F1_StartupSignal_CompletesOnlyWhenEveryExpectedContextCompleted()
    {
        var signal = new PersistenceStartupSignal();
        signal.IsCompleted.Should().BeTrue("nothing runs at startup");
        await signal.WaitAsync();

        signal.Expect(typeof(R1PlainContext));
        signal.Expect(typeof(R1TenantedContext));
        var wait = signal.WaitAsync();

        signal.Complete(typeof(R1PlainContext));
        signal.IsCompleted.Should().BeFalse();
        wait.IsCompleted.Should().BeFalse();

        signal.Complete(typeof(R1TenantedContext));
        await wait;
        signal.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task F1_StartupSignal_FaultsWaitersWhenAStartupStepFails()
    {
        var signal = new PersistenceStartupSignal();
        signal.Expect(typeof(R1PlainContext));
        signal.Fail(typeof(R1PlainContext), new InvalidProgramException("migration failed"));

        await FluentActions.Awaiting(() => signal.WaitAsync()).Should().ThrowAsync<InvalidProgramException>();
        signal.IsCompleted.Should().BeFalse();
    }

    private sealed class AllItems : Specification<R1Item>;

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) =>
            throw new InvalidProgramException("handler failed");
    }
}
