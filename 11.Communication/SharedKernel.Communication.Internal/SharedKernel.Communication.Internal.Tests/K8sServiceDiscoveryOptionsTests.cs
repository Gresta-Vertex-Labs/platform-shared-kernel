using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal.Options;

namespace SharedKernel.Communication.Internal.Tests;

public sealed class K8sServiceDiscoveryOptionsTests
{
    private static readonly K8sServiceDiscoveryOptionsValidator Validator = new();

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var opts = new K8sServiceDiscoveryOptions();

        var result = Validator.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyNamespace_Fails(string ns)
    {
        var opts = new K8sServiceDiscoveryOptions { Namespace = ns };

        var result = Validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains(nameof(K8sServiceDiscoveryOptions.Namespace)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyClusterDomain_Fails(string domain)
    {
        var opts = new K8sServiceDiscoveryOptions { ClusterDomain = domain };

        var result = Validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains(nameof(K8sServiceDiscoveryOptions.ClusterDomain)));
    }

    [Fact]
    public void Validate_BothEmpty_IncludesMultipleFailures()
    {
        var opts = new K8sServiceDiscoveryOptions { Namespace = "", ClusterDomain = "" };

        var result = Validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new K8sServiceDiscoveryOptions();

        opts.Namespace.Should().Be("default");
        opts.ClusterDomain.Should().Be("cluster.local");
        opts.SchemeOverride.Should().BeNull();
        opts.EndpointCacheTtlSeconds.Should().Be(30);
    }

    [Fact]
    public void Validate_CustomNamespaceAndDomain_Succeeds()
    {
        var opts = new K8sServiceDiscoveryOptions
        {
            Namespace = "production",
            ClusterDomain = "svc.example.com"
        };

        var result = Validator.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void SchemeOverride_WhenNull_IsResolvedAsHttp()
    {
        var opts = new K8sServiceDiscoveryOptions();

        // SchemeOverride = null → consumers treat as "http"
        var scheme = opts.SchemeOverride ?? "http";
        scheme.Should().Be("http");
    }
}
