using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests;

public sealed class SecurityRequestContextTests
{
    [Fact]
    public void AuthenticatedUser_MapsIdentityTenantAndAttribution()
    {
        var tenantId = Guid.NewGuid();
        var user = new FakeUserContext { ClientId = "spa", SessionId = "s-1" };
        var context = new SecurityRequestContext(user, new FakeTenantProvider(tenantId));

        context.IsAuthenticated.Should().BeTrue();
        context.UserId.Should().Be(FakeUserContext.DefaultSubjectId);
        context.TenantId.Should().Be(tenantId);
        context.ActorKind.Should().Be(ActorKind.User);
        context.ClientId.Should().Be("spa");
        context.SessionId.Should().Be("s-1");
    }

    [Fact]
    public void ServicePrincipalWithoutSubject_IsAServiceActor_IdentifiedByClientId()
    {
        var user = new FakeUserContext { IdentityKind = IdentityKind.ServicePrincipal, SubjectId = null, ClientId = "billing-worker" };
        var context = new SecurityRequestContext(user, new FakeTenantProvider());

        context.ActorKind.Should().Be(ActorKind.Service);
        context.UserId.Should().Be("billing-worker");
    }

    [Fact]
    public void Anonymous_HasNoUser_AndIsAnAnonymousActor_NeverTheSystem()
    {
        // Finding S7: unauthenticated callers were attributed to ActorKind.System, so the audit trail could not tell
        // an anonymous request from the platform's own background work.
        var user = new FakeUserContext { IdentityKind = IdentityKind.Anonymous };
        var context = new SecurityRequestContext(user, new FakeTenantProvider());

        context.IsAuthenticated.Should().BeFalse();
        context.UserId.Should().BeNull();
        context.ActorKind.Should().Be(ActorKind.Anonymous);
    }

    [Fact]
    public void AuthenticatedSystemIdentity_IsTheSystemActor()
    {
        var user = new FakeUserContext { IdentityKind = IdentityKind.System, SubjectId = "scheduler" };
        var context = new SecurityRequestContext(user, new FakeTenantProvider());

        context.IsAuthenticated.Should().BeTrue();
        context.ActorKind.Should().Be(ActorKind.System);
    }

    [Fact]
    public void EmptyTenant_FailsClosedAsNull()
    {
        var context = new SecurityRequestContext(new FakeUserContext(), new FakeTenantProvider(Guid.Empty) { TenantId = Guid.Empty });

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task HasPermissionAsync_UsesTheUserContextsOrdinalCheck()
    {
        var context = new SecurityRequestContext(
            new FakeUserContext { Permissions = ["orders.read"] },
            new FakeTenantProvider());

        (await context.HasPermissionAsync("orders.read", CancellationToken.None)).Should().BeTrue();
        (await context.HasPermissionAsync("Orders.Read", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void AddSharedKernelRequestContext_WinsOverAnEarlierAnonymousDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);   // e.g. EfCore Build()'s default
        services.AddScoped<IUserContext>(_ => new FakeUserContext());

        services.AddSharedKernelRequestContext();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRequestContext>().Should().BeOfType<SecurityRequestContext>();
        scope.ServiceProvider.GetRequiredService<ITenantProvider>().Should().BeOfType<UserContextTenantProvider>();
    }

    [Fact]
    public void AddSharedKernelRequestContext_KeepsAnExistingTenantProvider()
    {
        var services = new ServiceCollection();
        var tenantProvider = new FakeTenantProvider();
        services.AddSingleton<ITenantProvider>(tenantProvider);
        services.AddScoped<IUserContext>(_ => new FakeUserContext());

        services.AddSharedKernelRequestContext();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantProvider>().Should().BeSameAs(tenantProvider);
    }
}
