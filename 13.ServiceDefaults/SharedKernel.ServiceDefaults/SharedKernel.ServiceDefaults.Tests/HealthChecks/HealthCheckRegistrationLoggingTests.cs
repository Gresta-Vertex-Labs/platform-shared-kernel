using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

/// <summary>
/// Covers <see cref="HealthCheckRegistrationLogging.LogRegistration"/> directly, now that WO-084 made it
/// public for the separately-published integration packages and for readiness checks written outside
/// the platform. Its integration-package callers are covered in those packages' own tests.
/// </summary>
public sealed class HealthCheckRegistrationLoggingTests
{
    private const string Category = "Contoso.Ledger.LedgerHealthCheckExtensions";

    [Fact]
    public void LogRegistration_NullServices_Throws()
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(null!, Category, "ledger", ["ready"]);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LogRegistration_MissingCategoryName_Throws(string? categoryName)
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(
            new ServiceCollection(), categoryName!, "ledger", ["ready"]);

        act.Should().Throw<ArgumentException>().WithParameterName("categoryName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LogRegistration_MissingName_Throws(string? name)
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(
            new ServiceCollection(), Category, name!, ["ready"]);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void LogRegistration_NullTags_Throws()
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(
            new ServiceCollection(), Category, "ledger", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tags");
    }

    [Fact]
    public void LogRegistration_ForACheckWrittenOutsideThePlatform_LogsEventId13002OnceUnderTheGivenCategory()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        string[] tags = [HealthCheckTags.Ready, "ledger"];

        HealthCheckRegistrationLogging.LogRegistration(services, Category, "ledger", tags);
        services.AddHealthChecks().AddCheck("ledger", () => HealthCheckResult.Healthy(), tags);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        var logger = ((InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>()).GetLogger(Category);
        logger.Records.ShouldHaveLoggedCount(13002, 1);
        var record = logger.Records.ShouldHaveLogged(13002, LogLevel.Information);
        record.TryGetProperty("HealthCheckName", out var loggedName).Should().BeTrue();
        loggedName.Should().Be("ledger");
        record.TryGetProperty("Tags", out var loggedTags).Should().BeTrue();
        loggedTags.Should().Be("ready, ledger");
    }

    [Fact]
    public void LogRegistration_WithNoLoggingRegistered_StillResolvesHealthCheckOptions()
    {
        // The usual shape of a DI registration test: no logger factory at all. Registration logging
        // must never make health checks unresolvable.
        var services = new ServiceCollection();
        HealthCheckRegistrationLogging.LogRegistration(services, Category, "ledger", [HealthCheckTags.Ready]);
        services.AddHealthChecks().AddCheck("ledger", () => HealthCheckResult.Healthy());

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        act.Should().NotThrow();
    }
}
