using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Options;

/// <summary>
/// Tests verifying that an anonymous caller (<c>AnonymousRequestContext</c>, the default) is attributed to
/// <c>PersistenceServiceOptions.ServiceName</c> — the unauthenticated audit fallback
/// (the fallback lives in <c>AuditInterceptor</c>, which reads the service name from its options).
/// </summary>
public sealed class AuditServiceNameTests
{
    [Fact]
    public async Task Unauthenticated_AuditValue_Uses_ServiceName()
    {
        // Arrange — override ServiceName to something distinguishable from "system"
        const string serviceName = "invoice-service";
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var actorContext = TestDbContextFactory.CreateUnauthenticatedActorContext();

        using var ctx = TestDbContextFactory.CreateTestDbContext(actorContext, clock, serviceName);

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "test", clock));

        // Act
        await ctx.SaveChangesAsync();

        // Assert — CreatedBy should be the service name, not "system"
        var saved = await ctx.AuditableAggregates
            .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

        saved.Should().NotBeNull();
        saved!.CreatedBy.Should().Be(serviceName,
            "unauthenticated audit fallback should come from PersistenceServiceOptions.ServiceName");
    }

    [Fact]
    public async Task Default_ServiceName_System_IsStillUsed_WhenNoOverride()
    {
        // Arrange — default ServiceName is "system"
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var actorContext = TestDbContextFactory.CreateUnauthenticatedActorContext();

        using var ctx = TestDbContextFactory.CreateTestDbContext(actorContext, clock);

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "test", clock));

        // Act
        await ctx.SaveChangesAsync();

        // Assert — default fallback "system" still works when no override
        var saved = await ctx.AuditableAggregates
            .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

        saved.Should().NotBeNull();
        saved!.CreatedBy.Should().Be("system");
    }

    [Fact]
    public async Task Authenticated_User_AuditValue_Uses_UserId_Regardless_Of_ServiceName()
    {
        // Arrange — authenticated user → CreatedBy should be the GUID, not service name
        var userId = Guid.NewGuid();
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(userId);

        using var ctx = TestDbContextFactory.CreateTestDbContext(actorContext, clock);

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "test", clock));

        // Act
        await ctx.SaveChangesAsync();

        // Assert — authenticated path: CreatedBy = userId.ToString("D")
        var saved = await ctx.AuditableAggregates
            .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

        saved.Should().NotBeNull();
        saved!.CreatedBy.Should().Be(userId.ToString("D"),
            "authenticated user audit value must be userId.ToString(\"D\") regardless of ServiceName");
    }
}
