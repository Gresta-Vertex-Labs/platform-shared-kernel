using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using Xunit;

namespace SharedKernel.Idempotency.Abstractions.Tests;

public sealed class IdempotencyReservationTests
{
    [Fact]
    public void Started_CarriesTheToken_AndNoResponse()
    {
        var reservation = IdempotencyReservation.Started("token-1");

        Assert.Equal(IdempotencyReservationStatus.Started, reservation.Status);
        Assert.Equal("token-1", reservation.Token);
        Assert.Null(reservation.StoredResponse);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Started_WithoutAToken_Throws(string? token) =>
        Assert.ThrowsAny<ArgumentException>(() => IdempotencyReservation.Started(token!));

    [Fact]
    public void Completed_CarriesTheResponse_AndNoToken()
    {
        var reservation = IdempotencyReservation.Completed("{\"ok\":true}");

        Assert.Equal(IdempotencyReservationStatus.Completed, reservation.Status);
        Assert.Equal("{\"ok\":true}", reservation.StoredResponse);
        Assert.Null(reservation.Token);
    }

    [Fact]
    public void Completed_WithoutAResponse_IsAMessageCompletion()
    {
        var reservation = IdempotencyReservation.Completed(null);

        Assert.Equal(IdempotencyReservationStatus.Completed, reservation.Status);
        Assert.Null(reservation.StoredResponse);
    }

    [Fact]
    public void InProgress_AndFingerprintMismatch_CarryNothing()
    {
        Assert.Equal(new IdempotencyReservation(IdempotencyReservationStatus.InProgress, null, null), IdempotencyReservation.InProgress());
        Assert.Equal(
            new IdempotencyReservation(IdempotencyReservationStatus.FingerprintMismatch, null, null),
            IdempotencyReservation.FingerprintMismatch());
    }
}

public sealed class IdempotencyTenantScopeTests
{
    private sealed class FixedAccessor(IRequestContext? current) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } = current;
    }

    [Fact]
    public void For_ATenant_IsItsIdInDForm()
    {
        var tenant = new TenantId(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

        Assert.Equal("7c9e6679-7425-40de-944b-e07fc1f90ae7", IdempotencyTenantScope.For(tenant));
        Assert.Equal(IdempotencyTenantScope.MaxLength, IdempotencyTenantScope.For(tenant).Length);
    }

    [Fact]
    public void For_NoTenant_IsTheNoTenantValue_WhichIsNeverAGuid()
    {
        Assert.Equal(IdempotencyTenantScope.NoTenant, IdempotencyTenantScope.For(null));
        Assert.False(Guid.TryParse(IdempotencyTenantScope.NoTenant, out _));
    }

    [Fact]
    public void Current_ReadsTheAmbientTenant()
    {
        var tenantId = Guid.NewGuid();
        var accessor = new FixedAccessor(new SystemRequestContext([], tenantId: new TenantId(tenantId)));

        Assert.Equal(tenantId.ToString("D"), IdempotencyTenantScope.Current(accessor));
    }

    [Fact]
    public void Current_WithoutAContext_OrWithoutATenant_IsTheNoTenantScope()
    {
        Assert.Equal(IdempotencyTenantScope.NoTenant, IdempotencyTenantScope.Current(new FixedAccessor(null)));
        Assert.Equal(
            IdempotencyTenantScope.NoTenant,
            IdempotencyTenantScope.Current(new FixedAccessor(new SystemRequestContext([]))));
    }

    [Fact]
    public void Current_ReadsRequestContextScope_ThroughTheDefaultAccessor()
    {
        var tenantId = Guid.NewGuid();
        using (RequestContextScope.Begin(new SystemRequestContext([], tenantId: new TenantId(tenantId))))
            Assert.Equal(tenantId.ToString("D"), IdempotencyTenantScope.Current(new RequestContextAccessor()));
    }

    [Fact]
    public void Current_NullAccessor_Throws() =>
        Assert.Throws<ArgumentNullException>(() => IdempotencyTenantScope.Current(null!));
}

public sealed class IdempotencyPurposeSelectionTests
{
    [Fact]
    public void ForRequests_ForMessages_SelectsBoth_InOrder_WithoutDuplicates()
    {
        var selection = new IdempotencyPurposeSelection().ForMessages().ForRequests().ForMessages();

        Assert.Equal([IdempotencyPurpose.Message, IdempotencyPurpose.Request], selection.Purposes);
    }

    [Fact]
    public void For_AnUndefinedPurpose_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyPurposeSelection().For((IdempotencyPurpose)9));
}

public sealed class IdempotencyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIdempotencyStore_RegistersAScopedStoreKeyedByPurpose()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NullStore>(IdempotencyPurpose.Request);

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IIdempotencyStore), descriptor.ServiceType);
        Assert.Equal(IdempotencyPurpose.Request, descriptor.ServiceKey);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.True(services.HasIdempotencyStore(IdempotencyPurpose.Request));
        Assert.False(services.HasIdempotencyStore(IdempotencyPurpose.Message));
    }

    [Fact]
    public void AddIdempotencyStore_DifferentBackendsPerPurpose_ResolveIndependently()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NullStore>(IdempotencyPurpose.Request);
        services.AddIdempotencyStore<OtherStore>(IdempotencyPurpose.Message);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<NullStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request));
        Assert.IsType<OtherStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Message));
    }

    [Fact]
    public void AddIdempotencyStore_WithASelection_RegistersEverySelectedPurpose()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NullStore>(p => p.ForRequests().ForMessages());

        Assert.True(services.HasIdempotencyStore(IdempotencyPurpose.Request));
        Assert.True(services.HasIdempotencyStore(IdempotencyPurpose.Message));
    }

    [Fact]
    public void AddIdempotencyStore_TwiceForOnePurpose_Throws()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NullStore>(IdempotencyPurpose.Message);

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddIdempotencyStore<OtherStore>(IdempotencyPurpose.Message));
        Assert.Contains("Message", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIdempotencyStore_WithAnEmptySelection_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddIdempotencyStore<NullStore>(_ => { }));

    [Fact]
    public void GetRequiredIdempotencyStore_WithoutARegistration_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request));
    }

    [Fact]
    public void HasIdempotencyStore_IgnoresAnUnkeyedRegistration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyStore, NullStore>();

        Assert.False(services.HasIdempotencyStore(IdempotencyPurpose.Request));
    }

    private class NullStore : IIdempotencyStore
    {
        public Task<IdempotencyReservation> TryBeginAsync(
            IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken) =>
            Task.FromResult(IdempotencyReservation.Started("t"));

        public Task<bool> CompleteAsync(
            IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class OtherStore : NullStore;
}
