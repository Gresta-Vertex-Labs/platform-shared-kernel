using System.Security.Cryptography.X509Certificates;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.Security.Mtls.Tests.TestSupport;

internal sealed class RecordingValidator(Func<X509Certificate2, MtlsValidationResult> decide) : IMtlsCertificateValidator
{
    private readonly Lock _gate = new();
    private readonly List<X509Certificate2> _certificates = [];

    public RecordingValidator()
        : this(_ => MtlsValidationResult.Success("default-client"))
    {
    }

    public IReadOnlyList<X509Certificate2> Certificates
    {
        get
        {
            lock (_gate)
            {
                return [.. _certificates];
            }
        }
    }

    public ValueTask<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _certificates.Add(certificate);
        }

        return ValueTask.FromResult(decide(certificate));
    }
}
