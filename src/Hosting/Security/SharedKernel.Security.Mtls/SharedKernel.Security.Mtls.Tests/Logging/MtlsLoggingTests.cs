using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Tests.TestSupport;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Logging;

public sealed class MtlsLoggingTests : IDisposable
{
    private const string DistinctiveSubject = "CN=subject-vx92-distinctive,O=Distinctive Org";
    private const string DistinctiveClient = "client-vx92-distinctive";
    private static readonly Guid DistinctiveTenant = Guid.Parse("2b8e6c1d-34a7-4f90-b5d2-9c0a7e3f1d48");
    private static readonly EventId CertificateRejected = new(12300);

    private readonly TestCertificateAuthority _ca = TestCertificateAuthority.Create();

    public void Dispose() => _ca.Dispose();

    [Theory]
    [InlineData("UnknownClient")]
    [InlineData("NotRegistered")]
    [InlineData("custom.code-1_a")]
    public async Task Rejection_StableReasonCode_IsLoggedUnchanged(string reason)
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(_ => MtlsValidationResult.Failure(reason));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(CertificateRejected, "Reason", reason);
    }

    [Theory]
    [InlineData(DistinctiveSubject)]
    [InlineData("CN=client-one")]
    [InlineData("Expired\nsubject=CN=client-one")]
    [InlineData("client client-one is not registered")]
    [InlineData("client-one@example.com")]
    public async Task Rejection_ReasonNotIdentifierShaped_IsLoggedAsRejected(string reason)
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(_ => MtlsValidationResult.Failure(reason));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(CertificateRejected, "Reason", "Rejected");
        host.LogRecords.ShouldNotLeak(reason);
    }

    [Fact]
    public async Task Rejection_ReasonLongerThanMaxLength_IsLoggedAsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(_ => MtlsValidationResult.Failure(new string('a', 65)));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(CertificateRejected, "Reason", "Rejected");
    }

    [Fact]
    public async Task Sweep_AllPaths_NeverLogSubjectClientOrTenant()
    {
        using X509Certificate2 accepted = _ca.IssueLeaf(DistinctiveSubject);
        using X509Certificate2 rejected = _ca.IssueLeaf(DistinctiveSubject);
        using X509Certificate2 throwing = _ca.IssueLeaf(DistinctiveSubject);
        using TestCertificateAuthority otherCa = TestCertificateAuthority.Create("CN=Other Root");
        using X509Certificate2 untrusted = otherCa.IssueLeaf(DistinctiveSubject);

        await using MtlsTestHost host = await StartAsync(certificate =>
        {
            if (certificate.Thumbprint == rejected.Thumbprint)
            {
                return MtlsValidationResult.Failure(DistinctiveSubject);
            }

            if (certificate.Thumbprint == throwing.Thumbprint)
            {
                throw new InvalidOperationException("validator failed for " + DistinctiveClient);
            }

            return MtlsValidationResult.Success(DistinctiveClient, new TenantId(DistinctiveTenant), ["distinctive-role-55"]);
        });

        foreach (X509Certificate2 certificate in new[] { accepted, rejected, throwing, untrusted })
        {
            using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);
        }

        host.LogRecords.ShouldHaveLogged(new EventId(12300));
        host.LogRecords.ShouldHaveLogged(new EventId(12301));

        // ASP.NET Core's own certificate handler logs the subject under its framework category; only kernel categories
        // are swept. The thrown exception text is not part of the sweep: only the exception object is attached.
        host.LoggerFactory.Loggers
            .Where(logger => logger.Key.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .SelectMany(logger => logger.Value.Records)
            .Where(record => record.Exception is null)
            .ToList()
            .ShouldNotLeak(DistinctiveSubject, "subject-vx92", DistinctiveClient, DistinctiveTenant.ToString(), "distinctive-role-55");
    }

    private Task<MtlsTestHost> StartAsync(Func<X509Certificate2, MtlsValidationResult> decide) =>
        MtlsTestHost.StartAsync(services =>
        {
            var validator = new RecordingValidator(decide);
            services.AddSingleton<IMtlsCertificateValidator>(validator);
            services.AddMtlsAuthentication<RecordingValidator>(options =>
            {
                options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
                options.CustomTrustStore.Add(_ca.Certificate);
                options.RevocationMode = X509RevocationMode.NoCheck;
            });
        });
}
