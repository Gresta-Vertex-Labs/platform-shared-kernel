using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// Registration for <see cref="IClock"/>.
/// </summary>
/// <remarks>
/// This is the only dependency-injection surface in <c>SharedKernel.Primitives</c>, and the sole
/// reason the package references <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> at
/// all. Nothing else here is registered, and no other type in the package ships an
/// <c>Add*</c> method — <see cref="Identifiers.IIdGenerator"/> deliberately does not, because one
/// implementation and one line of registration is not worth a package-owned extension.
/// </remarks>
public static class ClockExtensions
{
    /// <summary>
    /// Registers <see cref="SystemClock"/> as the singleton <see cref="IClock"/> for the
    /// application.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call this once in every production host. All three behaviours below were verified by
    /// executing them against a real container, not inferred from the registration code.
    /// </para>
    /// <para>
    /// <b>Calling it twice is harmless.</b> Registration goes through <c>TryAddSingleton</c>, so
    /// the second call is a no-op rather than a second registration.
    /// </para>
    /// <para>
    /// <b>Your own <see cref="IClock"/> wins, if you register it FIRST.</b> <c>TryAddSingleton</c>
    /// is first-registration-wins, not last: a custom clock registered before this call is kept,
    /// and one registered after is ignored because this call already filled the slot. If you are
    /// replacing the platform clock, register yours and then either skip this call or make it
    /// first.
    /// </para>
    /// <para>
    /// <b>A registered <see cref="System.TimeProvider"/> is picked up automatically.</b> If the
    /// container can resolve a <c>TimeProvider</c>, the container selects
    /// <see cref="SystemClock(System.TimeProvider)"/> — it chooses the greediest constructor whose
    /// arguments it can satisfy — and the clock reads from your provider. With no
    /// <c>TimeProvider</c> registered it falls back to the parameterless constructor and
    /// <see cref="System.TimeProvider.System"/>. So a host doing coordinated simulation or
    /// deterministic replay only has to register its <c>TimeProvider</c>; nothing about this call
    /// changes. Registration ORDER does not matter for this, unlike for the <see cref="IClock"/>
    /// case above, because the provider is resolved when the clock is first constructed rather
    /// than when it is registered.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
