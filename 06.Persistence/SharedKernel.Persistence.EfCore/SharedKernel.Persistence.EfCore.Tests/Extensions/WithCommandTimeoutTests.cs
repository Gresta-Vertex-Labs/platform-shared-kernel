using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

/// <summary>
/// <see cref="EfCorePersistenceBuilder{TContext}.WithCommandTimeout(int)"/> —
/// the provider-neutral <c>RelationalOptionsExtension.WithCommandTimeout</c>/<c>AddOrUpdateExtension</c>
/// wiring technique, proven under SQLite (a Microsoft.EntityFrameworkCore.Relational-based provider,
/// so the extension applies identically regardless of the concrete provider).
/// </summary>
public sealed class WithCommandTimeoutTests
{
    [Fact]
    public void WithCommandTimeout_ConfiguresDatabaseCommandTimeout()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithCommandTimeout(45)
            .Build();

        var provider = services.BuildServiceProvider();
        using var ctx = provider.GetRequiredService<TestDbContext>();

        // Assert
        ctx.Database.GetCommandTimeout().Should().Be(45);
    }

    [Fact]
    public void WithoutCommandTimeout_PreservesProviderDefaultCommandTimeout()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — no.WithCommandTimeout() call.
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                        .Build();

        var provider = services.BuildServiceProvider();
        using var ctx = provider.GetRequiredService<TestDbContext>();

        // Assert — SQLite's own provider default (null, meaning no explicit timeout override).
        ctx.Database.GetCommandTimeout().Should().BeNull();
    }
}
