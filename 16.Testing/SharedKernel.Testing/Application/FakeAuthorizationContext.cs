using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.Authorization;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="IAuthorizationContext"/> (<c>05.Application.Behaviors</c>)
/// for use in unit tests.
/// </summary>
/// <remarks>
/// This is <b>not</b> <c>SharedKernel.Security.Abstractions.IUserContext</c> — it fakes
/// <c>05.Application</c>'s own narrower local seam only. Unconfigured requirement strings evaluate to
/// the constructor-supplied default result, mirroring <see cref="FakeUserContext"/>'s
/// authenticated-by-default convention.
/// </remarks>
public sealed class FakeAuthorizationContext : IAuthorizationContext
{
    private readonly ConcurrentDictionary<string, bool> _configured = new();
    private readonly ConcurrentQueue<string> _requirementsChecked = new();
    private readonly bool _defaultResult;

    /// <summary>
    /// Initializes a new instance of <see cref="FakeAuthorizationContext"/>.
    /// </summary>
    /// <param name="defaultResult">
    /// The result returned for any requirement that has not been explicitly configured via
    /// <see cref="Allow"/>/<see cref="Deny"/>. Defaults to <see langword="true"/> so most pipeline
    /// tests need zero configuration.
    /// </param>
    public FakeAuthorizationContext(bool defaultResult = true)
    {
        _defaultResult = defaultResult;
    }

    /// <summary>Configures <paramref name="requirement"/> to resolve to <see langword="true"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeAuthorizationContext Allow(string requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        _configured[requirement] = true;
        return this;
    }

    /// <summary>Configures <paramref name="requirement"/> to resolve to <see langword="false"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeAuthorizationContext Deny(string requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        _configured[requirement] = false;
        return this;
    }

    /// <summary>
    /// Gets every requirement string passed to <see cref="IsAuthorizedAsync"/>, <see cref="AllOf"/>, or
    /// <see cref="AnyOf"/> so far, in call order.
    /// </summary>
    public IReadOnlyList<string> RequirementsChecked => _requirementsChecked.ToArray();

    /// <summary>Clears the configured requirement map and the recorded call list.</summary>
    public void Reset()
    {
        _configured.Clear();
        _requirementsChecked.Clear();
    }

    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return Task.FromResult(Evaluate(requirement));
    }

    /// <inheritdoc />
    public Task<bool> AllOf(IEnumerable<string> requirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var all = true;
        foreach (var requirement in requirements)
        {
            if (!Evaluate(requirement))
            {
                all = false;
                break;
            }
        }

        return Task.FromResult(all);
    }

    /// <inheritdoc />
    public Task<bool> AnyOf(IEnumerable<string> requirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var any = false;
        foreach (var requirement in requirements)
        {
            if (Evaluate(requirement))
            {
                any = true;
                break;
            }
        }

        return Task.FromResult(any);
    }

    private bool Evaluate(string requirement)
    {
        _requirementsChecked.Enqueue(requirement);
        return _configured.TryGetValue(requirement, out var configuredResult) ? configuredResult : _defaultResult;
    }
}
