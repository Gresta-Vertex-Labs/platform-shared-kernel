using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.Testing.ServiceDefaults;

/// <summary>
/// Plain-exception assertion helpers over <see cref="HealthCheckRegistration"/> tag composition,
/// without booting a <c>WebApplicationFactory</c>.
/// </summary>
public static class HealthCheckAssertionExtensions
{
    /// <summary>Asserts that <paramref name="registration"/> is tagged <c>"ready"</c>.</summary>
    /// <param name="registration">The health check registration to inspect.</param>
    /// <exception cref="InvalidOperationException">The <c>"ready"</c> tag is absent.</exception>
    public static void ShouldBeTaggedReady(this HealthCheckRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!registration.Tags.Contains("ready"))
        {
            throw new InvalidOperationException(
                $"Expected health check '{registration.Name}' to carry the \"ready\" tag, but it did not. " +
                $"Tags: [{string.Join(", ", registration.Tags)}].");
        }
    }

    /// <summary>Asserts that <paramref name="registration"/> is NOT tagged <c>"live"</c>.</summary>
    /// <param name="registration">The health check registration to inspect.</param>
    /// <exception cref="InvalidOperationException">The <c>"live"</c> tag is present.</exception>
    public static void ShouldNotBeTaggedLive(this HealthCheckRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.Tags.Contains("live"))
        {
            throw new InvalidOperationException(
                $"Expected health check '{registration.Name}' NOT to carry the \"live\" tag, but it did.");
        }
    }
}
