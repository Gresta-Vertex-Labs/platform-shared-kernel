using global::Npgsql;
using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Validates <see cref="NpgsqlPersistenceOptions"/> beyond what Data Annotations can express —
/// specifically, that a downgraded <see cref="NpgsqlPersistenceOptions.SslMode"/> is always paired
/// with an explicit <see cref="NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode"/> opt-down.
/// </summary>
/// <remarks>
/// Registered alongside the Data Annotations validator by
/// <c>AddSharedKernelNpgsql(IServiceCollection, IConfiguration,...)</c> via
/// <c>AddValidatedOptions&lt;NpgsqlPersistenceOptions, NpgsqlPersistenceOptionsValidator&gt;(...,
/// validateDataAnnotations: true)</c>.
/// </remarks>
public sealed class NpgsqlPersistenceOptionsValidator : IValidateOptions<NpgsqlPersistenceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NpgsqlPersistenceOptions options)
    {
        if (options.SslMode < SslMode.VerifyFull && !options.AcknowledgeInsecureSslMode)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(NpgsqlPersistenceOptions.SslMode)} is '{options.SslMode}', below the "
                    + $"secure default of '{SslMode.VerifyFull}'. Set "
                    + $"{nameof(NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode)} to true to "
                    + "explicitly acknowledge this downgrade (e.g. for local development against a "
                    + "database with no verifiable TLS certificate).");
        }

        return ValidateOptionsResult.Success;
    }
}
