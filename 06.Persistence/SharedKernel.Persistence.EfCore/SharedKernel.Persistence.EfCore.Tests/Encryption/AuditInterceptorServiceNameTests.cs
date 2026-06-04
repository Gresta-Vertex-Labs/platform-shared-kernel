using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Tests verifying that <c>AuditInterceptor</c> uses <c>PersistenceServiceOptions.ServiceName</c>
/// as the unauthenticated audit fallback (C-73 — replaces hardcoded "system").
/// </summary>
public sealed class AuditInterceptorServiceNameTests
{
    [Fact]
    public async Task Unauthenticated_AuditValue_Uses_ServiceName()
    {
        // Arrange — override ServiceName to something distinguishable from "system"
        const string serviceName = "invoice-service";
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var unauthenticatedUser = TestDbContextFactory.CreateUnauthenticatedUserContext();
        var svcOpts = TestDbContextFactory.ServiceOptions(serviceName);

        using var ctx = TestDbContextFactory.CreateTestDbContextWithOptions(
            unauthenticatedUser, clock, svcOpts);

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
        var unauthenticatedUser = TestDbContextFactory.CreateUnauthenticatedUserContext();

        using var ctx = TestDbContextFactory.CreateTestDbContext(unauthenticatedUser, clock);

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
        const string serviceName = "should-not-appear-in-audit";
        var userId = Guid.NewGuid();
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var authenticatedUser = TestDbContextFactory.CreateAuthenticatedUserContext(userId);
        var svcOpts = TestDbContextFactory.ServiceOptions(serviceName);

        using var ctx = TestDbContextFactory.CreateTestDbContextWithOptions(
            authenticatedUser, clock, svcOpts);

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
