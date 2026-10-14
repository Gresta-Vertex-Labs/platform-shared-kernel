using Temporalio.Testing;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// A real local dev server (<see cref="WorkflowEnvironment.StartLocalAsync"/>) for the behaviour the time-skipping test
/// server does not have: it accepts any namespace name, while a real server answers <c>NOT_FOUND</c> with a
/// <c>NamespaceNotFoundFailure</c> detail.
/// </summary>
public sealed class LocalDevServerFixture : IAsyncLifetime
{
    /// <summary>The running local dev server.</summary>
    public WorkflowEnvironment Environment { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => Environment = await WorkflowEnvironment.StartLocalAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await Environment.DisposeAsync();
}
