using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.Npgsql.Tests.Options;

/// <summary>
/// <see cref="NpgsqlPersistenceOptionsValidator"/>: the TLS option matrix (explicit option, connection-string
/// keyword, loopback host, Development environment, acknowledgement), connection-string presence and
/// parsing, and the row-level-security incompatibilities.
/// </summary>
public sealed class NpgsqlPersistenceOptionsValidatorTests
{
    private const string Remote = "Host=db.internal.example;Database=x;Username=u;Password=p";
    private const string Loopback = "Host=localhost;Database=x;Username=u;Password=p";

    public static TheoryData<string, SslMode?, bool, string, bool> SslMatrix => new()
    {
        // connection string, SslMode option, acknowledged, environment, expected valid
        { Remote, null, false, Environments.Production, true },                                   // default VerifyFull
        { Remote + ";SSL Mode=VerifyFull", null, false, Environments.Production, true },
        { Remote + ";SSL Mode=Require", null, false, Environments.Production, false },             // honoured, but insecure
        { Remote + ";SslMode=Disable", null, false, Environments.Production, false },              // synonym honoured
        { Remote + ";SSL Mode=Require", null, true, Environments.Production, true },               // acknowledged
        { Remote + ";SSL Mode=Require", null, false, Environments.Development, true },             // Development
        { Remote + ";SSL Mode=Disable", SslMode.VerifyFull, false, Environments.Production, true }, // option overrides
        { Remote, SslMode.Disable, false, Environments.Production, false },
        { Loopback + ";SSL Mode=Disable", null, false, Environments.Production, true },            // loopback
        { Loopback, null, false, Environments.Production, true },                                 // loopback default = Disable
        { "Host=127.0.0.1;Database=x;Username=u;Password=p;SSL Mode=Disable", null, false, Environments.Production, true },
        { "Host=[::1]:5433;Database=x;Username=u;Password=p;SSL Mode=Disable", null, false, Environments.Production, true },
        { "Host=/var/run/postgresql;Database=x;Username=u", SslMode.Disable, false, Environments.Production, true },
        { "Host=localhost,db.internal.example;Database=x;Username=u;SSL Mode=Disable", null, false, Environments.Production, false },
        { Loopback, SslMode.Disable, false, Environments.Production, true },
    };

    [Theory]
    [MemberData(nameof(SslMatrix))]
    public void Validate_SslMatrix(string connectionString, SslMode? sslMode, bool acknowledged, string environment, bool expectedValid)
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = connectionString,
            SslMode = sslMode,
            AcknowledgeInsecureSslMode = acknowledged,
        };

        var result = new NpgsqlPersistenceOptionsValidator(new FakeHostEnvironment(environment)).Validate(null, options);

        result.Succeeded.Should().Be(expectedValid, result.FailureMessage);
    }

    [Fact]
    public void EffectiveSslMode_HonoursTheConnectionString_UnlessTheOptionIsSet()
    {
        var options = new NpgsqlPersistenceOptions();

        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, Remote).Should().Be(SslMode.VerifyFull);
        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, Remote + ";SSL Mode=Require").Should().Be(SslMode.Require);
        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, Loopback).Should().Be(SslMode.Disable);
        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, Loopback + ";SSL Mode=Require").Should().Be(SslMode.Require);
        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, "Host=localhost,db.internal.example;Database=x").Should().Be(SslMode.VerifyFull);

        options.SslMode = SslMode.VerifyCA;
        NpgsqlConnectionStringPolicy.EffectiveSslMode(options, Remote + ";SSL Mode=Require").Should().Be(SslMode.VerifyCA);
    }

    [Fact]
    public void EffectiveGssEncryptionMode_IsOff_UnlessTheConnectionStringAsksForIt()
    {
        // Found by the BillingApi container: Npgsql's default (Prefer) probes GSSAPI on every new connection and
        // prints "libgssapi_krb5.so.2: cannot open shared object file" in the standard ASP.NET images.
        NpgsqlConnectionStringPolicy.EffectiveGssEncryptionMode(Remote).Should().Be(GssEncryptionMode.Disable);
        NpgsqlConnectionStringPolicy.EffectiveGssEncryptionMode(Remote + ";GSS Encryption Mode=Require").Should().Be(GssEncryptionMode.Require);
        NpgsqlConnectionStringPolicy.EffectiveGssEncryptionMode(Remote + ";GssEncryptionMode=Prefer").Should().Be(GssEncryptionMode.Prefer);
    }

    [Fact]
    public void Validate_InsecureSecondaryConnectionString_Fails_NamingTheSetting()
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = Remote,
            ReadOnlyConnectionString = "Host=replica.example;Database=x;Username=u;SSL Mode=Disable",
        };

        var result = new NpgsqlPersistenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(NpgsqlPersistenceOptions.ReadOnlyConnectionString));
    }

    [Fact]
    public void Validate_NoConnectionString_FailsWithTheConnectionStringName()
    {
        var options = new NpgsqlPersistenceOptions { ConnectionStringName = "orders" };

        var result = new NpgsqlPersistenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ConnectionStrings:orders");
    }

    [Fact]
    public void Validate_UnparsableConnectionString_Fails_WithoutEchoingIt()
    {
        var options = new NpgsqlPersistenceOptions { ConnectionString = "Host=x;Password=secret-value;NotAKeyword=1" };

        var result = new NpgsqlPersistenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotContain("secret-value");
    }

    [Theory]
    [InlineData(";Multiplexing=true", false)]
    [InlineData(";No Reset On Close=true", false)]
    [InlineData("", true)]
    public void Validate_RowLevelSecurity_RejectsMultiplexingAndNoResetOnClose(string suffix, bool expectedValid)
    {
        var options = new NpgsqlPersistenceOptions
        {
            ConnectionString = Remote + suffix,
            RowLevelSecurity = { Enabled = true },
        };

        var result = new NpgsqlPersistenceOptionsValidator().Validate(null, options);

        result.Succeeded.Should().Be(expectedValid, result.FailureMessage);
    }

    [Fact]
    public void Validate_RowLevelSecurityOff_AllowsMultiplexing()
    {
        var options = new NpgsqlPersistenceOptions { ConnectionString = Remote + ";Multiplexing=true" };

        new NpgsqlPersistenceOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
