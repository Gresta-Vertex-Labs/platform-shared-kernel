using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Default <see cref="ICurrentActorContext"/> registered by <c>EfCorePersistenceBuilder.Build()</c>
/// when the consuming service has not registered its own — always reports the configured
/// service-name fallback, attributed as <see cref="ActorKind.System"/>.
/// </summary>
/// <remarks>
/// A consuming
/// service bridges this seam to its real identity source, typically via
/// <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c>, which registers a real
/// <see cref="ICurrentActorContext"/> bridge over <c>IUserContext</c> and removes the need for this
/// placeholder (last-registration-wins).
/// </remarks>
public sealed class AnonymousActorContext : ICurrentActorContext
{
    private readonly IOptions<PersistenceServiceOptions> _serviceOptions;

    /// <summary>Initialises a new <see cref="AnonymousActorContext"/>.</summary>
    /// <param name="serviceOptions">Options providing the unauthenticated audit fallback string.</param>
    public AnonymousActorContext(IOptions<PersistenceServiceOptions> serviceOptions)
    {
        _serviceOptions = serviceOptions;
    }

    /// <inheritdoc />
    public string ActorId => _serviceOptions.Value.ServiceName;

    /// <inheritdoc />
    public ActorKind ActorKind => ActorKind.System;
}
