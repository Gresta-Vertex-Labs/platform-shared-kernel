using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.Npgsql.Tests.Options;

/// <summary>
/// <see cref="NpgsqlPersistenceOptionsValidator"/> unit tests (no database needed — pure
/// validation logic).
/// </summary>
public sealed class NpgsqlPersistenceOptionsValidatorTests
{
    private readonly NpgsqlPersistenceOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultSslMode_Succeeds()
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DowngradedSslModeWithoutAcknowledgement_Fails()
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            SslMode = SslMode.Disable,
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode));
    }

    [Fact]
    public void Validate_DowngradedSslModeWithAcknowledgement_Succeeds()
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            SslMode = SslMode.Disable,
            AcknowledgeInsecureSslMode = true,
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(SslMode.VerifyFull)]
    public void Validate_SecureOrAboveSslMode_NeverRequiresAcknowledgement(SslMode sslMode)
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            SslMode = sslMode,
            AcknowledgeInsecureSslMode = false,
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
