using SharedKernel.Execution.Context;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Authentication;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Tests.TestSupport;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Extensions;

public sealed class MtlsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMtlsAuthentication_RegistersSchemeValidatorMapperAndContexts()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMtlsAuthentication<RecordingValidator>();

        ServiceDescriptor validator = Assert.Single(services, d => d.ServiceType == typeof(IMtlsCertificateValidator));
        Assert.Equal(ServiceLifetime.Scoped, validator.Lifetime);
        Assert.Equal(typeof(RecordingValidator), validator.ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IUserContextMapper) && d.ImplementationType == typeof(MtlsUserContextMapper));
        ServiceDescriptor userContext = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        Assert.Equal(ServiceLifetime.Scoped, userContext.Lifetime);
        Assert.NotNull(userContext.ImplementationFactory);
        Assert.Contains(services, d => d.ServiceType == typeof(IHttpContextAccessor));

        using ServiceProvider provider = services.BuildServiceProvider();
        AuthenticationOptions authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Contains(authentication.Schemes, s => s.Name == MtlsAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddMtlsAuthentication_Scheme_IsNotMadeDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMtlsAuthentication<RecordingValidator>();

        using ServiceProvider provider = services.BuildServiceProvider();
        AuthenticationOptions options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Null(options.DefaultScheme);
        Assert.Null(options.DefaultAuthenticateScheme);
        Assert.Null(options.DefaultChallengeScheme);
        Assert.Null(options.DefaultForbidScheme);
    }

    [Fact]
    public async Task AddMtlsAuthentication_WithOtherDefaultScheme_DefaultEndpointIgnoresCertificate()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        using X509Certificate2 certificate = ca.IssueLeaf();
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await MtlsTestHost.StartAsync(services =>
        {
            services.AddAuthentication("Other").AddScheme<AuthenticationSchemeOptions, NoResultHandler>("Other", _ => { });
            services.AddSingleton<IMtlsCertificateValidator>(validator);
            services.AddMtlsAuthentication<RecordingValidator>(options =>
            {
                options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
                options.CustomTrustStore.Add(ca.Certificate);
                options.RevocationMode = X509RevocationMode.NoCheck;
            });
        });

        CallerSnapshot onDefault = await host.GetCallerAsync("/default", certificate);
        CallerSnapshot onCertificate = await host.GetCallerAsync("/certificate", certificate);

        Assert.False(onDefault.IsAuthenticated);
        Assert.True(onCertificate.IsAuthenticated);
        Assert.Single(validator.Certificates);
    }

    [Fact]
    public void AddMtlsAuthentication_ExistingUserContext_IsKept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserContext>(SystemUserContext.Instance);

        services.AddMtlsAuthentication<RecordingValidator>();

        Assert.Same(SystemUserContext.Instance, Assert.Single(services, d => d.ServiceType == typeof(IUserContext)).ImplementationInstance);
    }

    [Fact]
    public void AddMtlsAuthentication_ExistingValidator_IsKept()
    {
        var existing = new RecordingValidator();
        var services = new ServiceCollection();
        services.AddSingleton<IMtlsCertificateValidator>(existing);

        services.AddMtlsAuthentication<RecordingValidator>();

        Assert.Same(existing, Assert.Single(services, d => d.ServiceType == typeof(IMtlsCertificateValidator)).ImplementationInstance);
    }

    [Fact]
    public void AddMtlsAuthentication_AnonymousPlaceholder_IsReplacedByResolver()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);

        services.AddMtlsAuthentication<RecordingValidator>();

        ServiceDescriptor userContext = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        Assert.Null(userContext.ImplementationInstance);
        Assert.NotNull(userContext.ImplementationFactory);
    }

    [Fact]
    public void AddMtlsAuthentication_KeyedAnonymousRegistration_IsKept()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IUserContext>("audit", AnonymousUserContext.Instance);

        services.AddMtlsAuthentication<RecordingValidator>();

        Assert.Single(services, d => d.ServiceType == typeof(IUserContext) && d.IsKeyedService);
        Assert.Single(services, d => d.ServiceType == typeof(IUserContext) && !d.IsKeyedService);
    }

    [Fact]
    public void AddMtlsAuthentication_ResolvedOutsideRequest_IsAnonymous()
    {
        var services = new ServiceCollection();
        services.AddMtlsAuthentication<RecordingValidator>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(AnonymousUserContext.Instance, scope.ServiceProvider.GetRequiredService<IUserContext>());
    }

    [Fact]
    public void AddMtlsAuthentication_CertificatePrincipal_ResolvesServicePrincipal()
    {
        var services = new ServiceCollection();
        services.AddMtlsAuthentication<RecordingValidator>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(SecurityClaimTypes.Subject, "tpp-42")],
                MtlsAuthenticationDefaults.AuthenticationScheme)),
        };

        IUserContext user = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.Equal(ActorKind.Service, user.ActorKind);
        Assert.Equal("tpp-42", user.SubjectId);
    }

    [Fact]
    public async Task AddMtlsAuthentication_ValidOptions_HostStarts()
    {
        using IHost host = MtlsTestHost.Build(services => services.AddMtlsAuthentication<RecordingValidator>());

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task AddMtlsAuthentication_CustomRootTrustWithoutTrustStore_HostStartFails()
    {
        using IHost host = MtlsTestHost.Build(services => services.AddMtlsAuthentication<RecordingValidator>(
            options => options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust));

        await AssertStartupFailsAsync(host, "CustomTrustStore");
    }

    [Fact]
    public async Task AddMtlsAuthentication_TrustStoreWithSystemTrust_HostStartFails()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        using IHost host = MtlsTestHost.Build(services => services.AddMtlsAuthentication<RecordingValidator>(
            options => options.CustomTrustStore.Add(ca.Certificate)));

        await AssertStartupFailsAsync(host, "CustomTrustStore");
    }

    [Fact]
    public async Task AddMtlsAuthentication_UndefinedRevocationMode_HostStartFails()
    {
        using IHost host = MtlsTestHost.Build(services => services.AddMtlsAuthentication<RecordingValidator>(
            options => options.RevocationMode = (X509RevocationMode)99));

        await AssertStartupFailsAsync(host, "RevocationMode");
    }

    [Fact]
    public async Task AddMtlsAuthentication_NoAllowedCertificateTypes_HostStartFails()
    {
        using IHost host = MtlsTestHost.Build(services => services.AddMtlsAuthentication<RecordingValidator>(
            options => options.AllowedCertificateTypes = 0));

        await AssertStartupFailsAsync(host, "AllowedCertificateTypes");
    }

    [Fact]
    public async Task AddMtlsAuthentication_EventsTypeOnCertificateScheme_HostStartFails()
    {
        using IHost host = MtlsTestHost.Build(services =>
        {
            services.AddMtlsAuthentication<RecordingValidator>();
            services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.EventsType = typeof(CertificateAuthenticationEvents));
        });

        await AssertStartupFailsAsync(host, "EventsType");
    }

    [Fact]
    public void AddMtlsAuthentication_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddMtlsAuthentication<RecordingValidator>());
    }

    // The certificate scheme options read the mTLS options, so an invalid setting can fail both validations at once and
    // the host reports them together.
    private static async Task AssertStartupFailsAsync(IHost host, string expectedFragment)
    {
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        IEnumerable<Exception> exceptions = exception is AggregateException aggregate ? aggregate.InnerExceptions : [exception];
        string[] failures = [.. exceptions.OfType<OptionsValidationException>().SelectMany(e => e.Failures)];
        Assert.NotEmpty(failures);
        Assert.Equal(exceptions.Count(), exceptions.OfType<OptionsValidationException>().Count());
        Assert.Contains(failures, failure => failure.Contains(expectedFragment, StringComparison.Ordinal));
    }

    private sealed class NoResultHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
