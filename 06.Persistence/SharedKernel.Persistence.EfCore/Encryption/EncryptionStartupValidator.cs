using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Validates at application startup (P-227) that <see cref="ISymmetricEncryptionService"/> is
/// registered in the DI container when <c>EfCorePersistenceBuilder.WithEncryption()</c> was called.
/// </summary>
/// <remarks>
/// Throws an <see cref="InvalidOperationException"/> with an actionable message naming
/// <c>AddSharedKernelCryptography()</c> if the service is missing. The consuming service is
/// responsible for that registration — this domain does not call it.
/// </remarks>
internal sealed class EncryptionStartupValidator : IValidateOptions<EncryptionStartupOptions>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initialises a new <see cref="EncryptionStartupValidator"/>.</summary>
    /// <param name="serviceProvider">The root DI service provider.</param>
    public EncryptionStartupValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EncryptionStartupOptions options)
    {
        // Check whether ISymmetricEncryptionService is resolvable.
        var service = _serviceProvider.GetService<ISymmetricEncryptionService>();
        if (service is null)
        {
            return ValidateOptionsResult.Fail(
                "EfCorePersistenceBuilder.WithEncryption() was called, but " +
                "ISymmetricEncryptionService is not registered in the DI container. " +
                "Call services.AddSharedKernelCryptography(configuration) before " +
                ".AddSharedKernelEfCore<TContext>(...).WithEncryption().Build() to register it.");
        }

        return ValidateOptionsResult.Success;
    }
}
