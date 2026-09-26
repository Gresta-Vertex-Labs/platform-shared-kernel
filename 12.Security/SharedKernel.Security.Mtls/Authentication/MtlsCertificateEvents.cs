using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Logging;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.Security.Mtls.Authentication;

// Runs IMtlsCertificateValidator first, then the application's own events. A failure from the validator is final: a
// throwing validator rejects the certificate, and an application AuthenticationFailed event cannot turn the rejection
// into a success.
internal sealed class MtlsCertificateEvents(CertificateAuthenticationEvents inner) : CertificateAuthenticationEvents
{
    private static readonly object RejectedByValidator = new();

    public override async Task CertificateValidated(CertificateValidatedContext context)
    {
        IServiceProvider services = context.HttpContext.RequestServices;
        string thumbprint = Base64Url.EncodeToString(SHA256.HashData(context.ClientCertificate.RawDataMemory.Span));

        MtlsValidationResult result;
        try
        {
            result = await services
                .GetRequiredService<IMtlsCertificateValidator>()
                .ValidateAsync(context.ClientCertificate, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MtlsAuthenticationLog.ValidatorFailed(CreateLogger(services), ex, thumbprint);
            Reject(context);
            return;
        }

        if (!result.IsValid)
        {
            MtlsAuthenticationLog.CertificateRejected(CreateLogger(services), result.FailureReason ?? "Rejected", thumbprint);
            Reject(context);
            return;
        }

        var claims = new List<Claim>
        {
            new(SecurityClaimTypes.Subject, result.ClientId!),
            new(SecurityClaimTypes.ClientId, result.ClientId!),
            new(MtlsAuthenticationDefaults.CertificateThumbprintClaimType, thumbprint),
        };

        if (result.TenantId is { } tenantId)
        {
            claims.Add(new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()));
        }

        claims.AddRange(result.Roles.Select(role => new Claim(SecurityClaimTypes.Roles, role)));
        claims.AddRange(result.Permissions.Select(permission => new Claim(SecurityClaimTypes.Scope, permission)));

        context.Principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, context.Scheme.Name, SecurityClaimTypes.Subject, SecurityClaimTypes.Roles));

        await inner.CertificateValidated(context).ConfigureAwait(false);

        if (context.Result is null)
        {
            context.Success();
        }
    }

    public override async Task AuthenticationFailed(CertificateAuthenticationFailedContext context)
    {
        await inner.AuthenticationFailed(context).ConfigureAwait(false);

        if (context.HttpContext.Items.ContainsKey(RejectedByValidator) && context.Result is { Succeeded: true })
        {
            context.Fail("Client certificate rejected.");
        }
    }

    private static void Reject(CertificateValidatedContext context)
    {
        context.HttpContext.Items[RejectedByValidator] = true;
        context.Fail("Client certificate rejected.");
    }

    private static ILogger CreateLogger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger<MtlsCertificateEvents>();

    public override Task Challenge(CertificateChallengeContext context) => inner.Challenge(context);
}
