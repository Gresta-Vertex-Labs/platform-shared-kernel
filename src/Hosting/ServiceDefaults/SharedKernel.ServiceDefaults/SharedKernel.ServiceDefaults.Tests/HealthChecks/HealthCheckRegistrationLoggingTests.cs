using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

/// <summary>
/// Covers <see cref="HealthCheckRegistrationLogging.LogRegistration"/> directly: it is public for the
/// separately-published integration packages and for readiness checks written outside the platform. Its
/// integration-package callers are covered in those packages' own tests.
/// </summary>
public sealed class HealthCheckRegistrationLoggingTests
{
    /// <summary>The category is a kernel marker type of the base, never a string a caller supplies.</summary>
    private static readonly Type CategoryType =
        typeof(HealthCheckRegistrationLogging).Assembly.GetType("SharedKernel.ServiceDefaults.Diagnostics.ServiceDefaultsLogCategory")!;

    [Fact]
    public void LogRegistration_NullServices_Throws()
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(null!, "ledger", ["ready"]);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LogRegistration_MissingName_Throws(string? name)
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(
            new ServiceCollection(), name!, ["ready"]);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void LogRegistration_NullTags_Throws()
    {
        var act = () => HealthCheckRegistrationLogging.LogRegistration(
            new ServiceCollection(), "ledger", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tags");
    }

    [Fact]
    public void LogRegistration_ForACheckWrittenOutsideThePlatform_Logs13000OnceUnderTheKernelCategory()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        string[] tags = [HealthCheckTags.Ready, "ledger"];

        HealthCheckRegistrationLogging.LogRegistration(services, "ledger", tags);
        services.AddHealthChecks().AddCheck("ledger", () => HealthCheckResult.Healthy(), tags);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        factory.ShouldHaveCategory(CategoryType);
        var logger = factory.GetLogger(CategoryType.FullName!);
        logger.Records.ShouldHaveLoggedCount(13000, 1);
        var record = logger.Records.ShouldHaveLogged(13000, LogLevel.Information);
        record.TryGetProperty("HealthCheckName", out var loggedName).Should().BeTrue();
        loggedName.Should().Be("ledger");
        record.TryGetProperty("Tags", out var loggedTags).Should().BeTrue();
        loggedTags.Should().Be("ready, ledger");
        logger.Records.ShouldFollowLogConvention();
        logger.Records.ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.ServiceDefaults, 0);
    }

    [Fact]
    public void LogRegistration_WithNoLoggingRegistered_FailsWhenTheOptionsAreResolved_NotSilently()
    {
        // The logger is a required dependency (L8): a host without logging services fails loudly instead of
        // registering a check nobody can see in the startup log.
        var services = new ServiceCollection();
        HealthCheckRegistrationLogging.LogRegistration(services, "ledger", [HealthCheckTags.Ready]);
        services.AddHealthChecks().AddCheck("ledger", () => HealthCheckResult.Healthy());

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        act.Should().Throw<InvalidOperationException>().WithMessage("*ILoggerFactory*");
    }
}
