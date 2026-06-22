using Microsoft.AspNetCore.Http;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Resolves the tenant identifier from a named HTTP request header.
/// </summary>
/// <remarks>
/// Intended for B2B / API-key clients that have no tenant claim embedded in their access token.
/// Returns <see langword="null"/> when the header is absent or its value does not parse as a
/// <see cref="Guid"/> — never throws.
/// </remarks>
public sealed class HeaderTenantResolutionStrategy(string headerName = HeaderTenantResolutionStrategy.DefaultHeaderName)
    : ITenantResolutionStrategy
{
    /// <summary>The default HTTP request header name probed when no header name is supplied.</summary>
    public const string DefaultHeaderName = "X-Tenant-Id";

    /// <inheritdoc/>
    public string StrategyName => TenantResolutionStrategyNames.Header;

    /// <inheritdoc/>
    public Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            return Task.FromResult<Guid?>(null);
        }

        var rawValue = headerValues.ToString();

        return Task.FromResult(Guid.TryParse(rawValue, out var tenantId)
            ? tenantId
            : (Guid?)null);
    }
}
