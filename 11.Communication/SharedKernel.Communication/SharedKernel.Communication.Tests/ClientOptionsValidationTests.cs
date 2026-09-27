using System.ComponentModel.DataAnnotations;
using System.Reflection;
using SharedKernel.ArchitectureTests;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Tests;

public sealed class ClientOptionsValidationTests
{
    [Fact]
    public void Client_credentials_need_an_endpoint_an_id_and_a_secret()
    {
        var options = new TestClientOptions
        {
            Authentication = { Mode = ClientAuthenticationMode.ClientCredentials },
        };

        Errors(options).Should().Contain(m => m.Contains("TokenEndpoint")).And.Contain(m => m.Contains("ClientId and ClientSecret"));
    }

    [Fact]
    public void An_API_key_needs_a_value()
    {
        var options = new TestClientOptions { Authentication = { Mode = ClientAuthenticationMode.ApiKey } };

        Errors(options).Should().ContainSingle().Which.Should().Contain("ApiKey:HeaderName and Value");
    }

    [Fact]
    public void A_missing_certificate_file_is_reported()
    {
        var options = new TestClientOptions { Tls = { CertificatePath = "/does/not/exist.crt" } };

        Errors(options).Should().ContainSingle().Which.Should().Contain("does not exist");
    }

    [Fact]
    public void A_key_without_a_certificate_is_reported()
    {
        var options = new TestClientOptions { Tls = { PrivateKeyPath = "tls.key" } };

        Errors(options).Should().Contain(m => m.Contains("without a CertificatePath"));
    }

    [Fact]
    public void Nothing_configured_is_valid()
    {
        Errors(new TestClientOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Every_log_event_id_is_unique_and_in_the_communication_range()
    {
        Assembly assembly = typeof(CommunicationOptions).Assembly;
        var range = (LoggingEventIdRanges.Communication, LoggingEventIdRanges.Communication + 999);

        var check = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(
            new Dictionary<Assembly, (int RangeMin, int RangeMax)> { [assembly] = range });

        check.Should().NotThrow();
    }

    private static List<string> Errors(CommunicationClientOptions options) =>
        [.. options.Validate(new ValidationContext(options)).Select(r => r.ErrorMessage!)];

    private sealed class TestClientOptions : CommunicationClientOptions;
}
