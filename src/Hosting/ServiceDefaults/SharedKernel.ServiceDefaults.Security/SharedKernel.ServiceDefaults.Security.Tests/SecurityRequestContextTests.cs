using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests;

public sealed class SecurityRequestContextTests
{
    [Fact]
    public void AuthenticatedUser_MapsIdentityTenantAndAttribution()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var user = new FakeUserContext { ClientId = "spa", SessionId = "s-1", TenantId = tenantId };
        var context = new SecurityRequestContext(user);

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
        var user = new FakeUserContext { ActorKind = ActorKind.Service, SubjectId = null, ClientId = "billing-worker" };
        var context = new SecurityRequestContext(user);

        context.ActorKind.Should().Be(ActorKind.Service);
        context.UserId.Should().Be("billing-worker");
    }

    [Fact]
    public void Anonymous_HasNoUser_AndIsAnAnonymousActor_NeverTheSystem()
    {
        // Finding S7: unauthenticated callers were attributed to ActorKind.System, so the audit trail could not tell
        // an anonymous request from the platform's own background work.
        var user = new FakeUserContext { ActorKind = ActorKind.Anonymous };
        var context = new SecurityRequestContext(user);

        context.IsAuthenticated.Should().BeFalse();
        context.UserId.Should().BeNull();
        context.ActorKind.Should().Be(ActorKind.Anonymous);
    }

    [Fact]
    public void AuthenticatedSystemIdentity_IsTheSystemActor()
    {
        var user = new FakeUserContext { ActorKind = ActorKind.System, SubjectId = "scheduler" };
        var context = new SecurityRequestContext(user);

        context.IsAuthenticated.Should().BeTrue();
        context.ActorKind.Should().Be(ActorKind.System);
    }

    [Fact]
    public void NoTenant_FailsClosedAsNull()
    {
        var context = new SecurityRequestContext(new FakeUserContext { TenantId = null });

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task HasPermissionAsync_UsesTheUserContextsOrdinalCheck()
    {
        var context = new SecurityRequestContext(new FakeUserContext { Permissions = ["orders.read"] });

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
        scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Should().BeOfType<RequestContextAccessor>();
    }

    [Fact]
    public void AddSharedKernelRequestContext_ReturnsTheSameSecurityContextWithinAScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => new FakeUserContext());
        services.AddSharedKernelRequestContext();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRequestContext>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<IRequestContext>());
    }

    [Fact]
    public void AddSharedKernelRequestContext_PrefersTheAmbientContextAnInboundAdapterOpened()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => new FakeUserContext());
        services.AddSharedKernelRequestContext();
        var ambient = new SystemRequestContext([], "job", new TenantId(Guid.NewGuid()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using (RequestContextScope.Begin(ambient))
        {
            scope.ServiceProvider.GetRequiredService<IRequestContext>().Should().BeSameAs(ambient);
        }

        scope.ServiceProvider.GetRequiredService<IRequestContext>().Should().BeOfType<SecurityRequestContext>();
    }
}
