using System.Collections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Revocation;

namespace SharedKernel.Security.Oidc.Extensions;

/// <summary>
/// Returned by <see cref="SecurityServiceCollectionExtensions.AddSharedKernelSecurity"/> and
/// <see cref="SecurityServiceCollectionExtensions.AddAzureB2CAuthentication"/>, enabling optional,
/// chainable opt-ins with no change in behavior for callers that ignore the return value.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IServiceCollection"/> by forwarding to the wrapped collection, so a caller
/// that only ever used the bare <see cref="IServiceCollection"/> return value of the pre-WO-058
/// registration methods keeps compiling and behaving identically — this type is a drop-in superset,
/// never a breaking return-type change in practice (WO-058, P-376/P-379).
/// </para>
/// </remarks>
public sealed class SecurityAuthenticationBuilder : IServiceCollection
{
    private readonly IServiceCollection _services;

    internal SecurityAuthenticationBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Opts into DPoP (RFC 9449) sender-constrained access-token validation.
    /// </summary>
    /// <typeparam name="TReplayCache">
    /// The consumer-supplied <see cref="IDpopProofReplayCache"/> implementation. This package never
    /// dictates a storage mechanism.
    /// </typeparam>
    /// <returns>The same <see cref="SecurityAuthenticationBuilder"/> for chaining.</returns>
    /// <remarks>
    /// Disabled by default — adds no behavior until called. Wires DPoP proof validation into
    /// <c>JwtBearerEvents.OnTokenValidated</c> for the <see cref="JwtBearerDefaults.AuthenticationScheme"/>
    /// scheme, running after any previously-registered <c>OnTokenValidated</c> handler and only when
    /// that handler did not already terminate the request (WO-058, P-376).
    /// </remarks>
    public SecurityAuthenticationBuilder RequireDpop<TReplayCache>()
        where TReplayCache : class, IDpopProofReplayCache
    {
        _services.AddScoped<IDpopProofReplayCache, TReplayCache>();
        _services.AddOptions<DpopOptions>();

        _services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure(jwtBearerOptions =>
            {
                jwtBearerOptions.Events ??= new JwtBearerEvents();
                var previousOnTokenValidated = jwtBearerOptions.Events.OnTokenValidated;
                jwtBearerOptions.Events.OnTokenValidated = async context =>
                {
                    if (previousOnTokenValidated is not null)
                    {
                        await previousOnTokenValidated(context).ConfigureAwait(false);
                    }

                    if (context.Result is not null)
                    {
                        // A previously-registered handler already terminated the request.
                        return;
                    }

                    await DpopProofValidator.ValidateAsync(context).ConfigureAwait(false);
                };
            });

        return this;
    }

    /// <summary>
    /// Opts into a post-validation revocation/introspection check for every incoming access token.
    /// </summary>
    /// <typeparam name="TCheck">
    /// The consumer-supplied <see cref="ITokenRevocationCheck"/> implementation.
    /// </typeparam>
    /// <returns>The same <see cref="SecurityAuthenticationBuilder"/> for chaining.</returns>
    /// <remarks>
    /// Disabled by default. Runs only after standard signature/issuer/audience/lifetime validation
    /// succeeds, and after DPoP validation when both are opted into together (registration order
    /// determines chaining order). Fails CLOSED — an unavailable or throwing
    /// <see cref="ITokenRevocationCheck"/> rejects the request (WO-058, P-379).
    /// </remarks>
    public SecurityAuthenticationBuilder WithRevocationCheck<TCheck>()
        where TCheck : class, ITokenRevocationCheck
    {
        _services.AddScoped<ITokenRevocationCheck, TCheck>();

        _services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure(jwtBearerOptions =>
            {
                jwtBearerOptions.Events ??= new JwtBearerEvents();
                var previousOnTokenValidated = jwtBearerOptions.Events.OnTokenValidated;
                jwtBearerOptions.Events.OnTokenValidated = async context =>
                {
                    if (previousOnTokenValidated is not null)
                    {
                        await previousOnTokenValidated(context).ConfigureAwait(false);
                    }

                    if (context.Result is not null)
                    {
                        return;
                    }

                    await RevocationCheckRunner.ValidateAsync(context).ConfigureAwait(false);
                };
            });

        return this;
    }

    // ---- IServiceCollection forwarding (IList<ServiceDescriptor>) ----

    /// <inheritdoc/>
    public ServiceDescriptor this[int index]
    {
        get => _services[index];
        set => _services[index] = value;
    }

    /// <inheritdoc/>
    public int Count => _services.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => _services.IsReadOnly;

    /// <inheritdoc/>
    public void Add(ServiceDescriptor item) => _services.Add(item);

    /// <inheritdoc/>
    public void Clear() => _services.Clear();

    /// <inheritdoc/>
    public bool Contains(ServiceDescriptor item) => _services.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) => _services.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<ServiceDescriptor> GetEnumerator() => _services.GetEnumerator();

    /// <inheritdoc/>
    public int IndexOf(ServiceDescriptor item) => _services.IndexOf(item);

    /// <inheritdoc/>
    public void Insert(int index, ServiceDescriptor item) => _services.Insert(index, item);

    /// <inheritdoc/>
    public bool Remove(ServiceDescriptor item) => _services.Remove(item);

    /// <inheritdoc/>
    public void RemoveAt(int index) => _services.RemoveAt(index);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
