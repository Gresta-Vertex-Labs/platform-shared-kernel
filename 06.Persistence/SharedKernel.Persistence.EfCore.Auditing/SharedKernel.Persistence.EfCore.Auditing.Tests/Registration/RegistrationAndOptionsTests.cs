using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Registration;

public sealed class RegistrationAndOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"{AuditLedgerOptions.SectionName}:{v.Key}", v.Value)))
            .Build();

    private static readonly (string, string?)[] ValidKeyring =
    [
        ("CurrentKeyId", "k1"),
        ("Keys:k1:Material", TestKeys.K1),
        ("Keys:k1:Order", "1"),
    ];

    private static ServiceProvider Build(IConfiguration configuration, Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestContext>(new TestRequestContext());
        services.AddSingleton<ICrossTenantScope>(new TestCrossTenantScope());
        services.AddSingleton<IAmbientDbTransaction>(new TestAmbientTransaction());
        services.AddSingleton<IDbConnectionFactory>(new TestConnectionFactory("Host=unused"));
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        before?.Invoke(services);
        services.AddSharedKernelAuditLedger(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Registration_ResolvesEveryPublicService()
    {
        using var provider = Build(Configuration(ValidKeyring));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().Should().BeOfType<EfAuditTrailWriter>();
        scope.ServiceProvider.GetRequiredService<IAuditQueryService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAuditLedgerMaintenance>().Should().NotBeNull();
        provider.GetRequiredService<IAuditSealingProbe>().Should().NotBeNull();
        provider.GetRequiredService<IAuditRecordAuthenticator>().Should().BeOfType<KeyringAuditRecordAuthenticator>();
        provider.GetRequiredService<IAuditCheckpointSink>().Should().BeOfType<TableAuditCheckpointSink>();
        provider.GetServices<IHostedService>().Should().HaveCount(2, "the self-check and the sealer");
    }

    [Fact]
    public void Registration_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelAuditLedger(Configuration(ValidKeyring));
        var count = services.Count;

        services.AddSharedKernelAuditLedger(Configuration(ValidKeyring));

        services.Count.Should().Be(count);
    }

    [Fact]
    public void PreRegisteredAuthenticatorAndSink_KeepWinning_AndNoKeyringIsRequired()
    {
        var authenticator = new KeyringAuditRecordAuthenticator(
            Microsoft.Extensions.Options.Options.Create(new AuditLedgerOptions { CurrentKeyId = "x", Keys = { ["x"] = new AuditKeyOptions { Material = TestKeys.K1, Order = 1 } } }),
            new HmacSha256Signer());

        using var provider = Build(Configuration(), s => s.AddSingleton<IAuditRecordAuthenticator>(authenticator));

        provider.GetRequiredService<IAuditRecordAuthenticator>().Should().BeSameAs(authenticator);
        provider.GetRequiredService<IOptions<AuditLedgerOptions>>().Value.UsesCustomAuthenticator.Should().BeTrue();
    }

    [Fact]
    public void MissingKeyring_FailsValidation()
    {
        using var provider = Build(Configuration());

        var act = () => provider.GetRequiredService<IOptions<AuditLedgerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Keys is empty*");
    }

    [Theory]
    [InlineData("CurrentKeyId", "k9", "*not one of the configured*")]
    [InlineData("Keys:k1:Material", "c2hvcnQ=", "*at least 32*")]
    [InlineData("Keys:k1:Material", "not base64!", "*not valid Base64*")]
    [InlineData("Keys:k2:Order", "2", "*must be the newest*")]
    [InlineData("Keys:k2:Order", "1", "*distinct order*")]
    [InlineData("Sealer:BatchSize", "0", "*BatchSize*")]
    [InlineData("Sealer:Interval", "00:00:00", "*Interval*")]
    public void InvalidOptions_AreRejectedAtStartup(string key, string value, string message)
    {
        var values = ValidKeyring.ToList();
        values.RemoveAll(v => v.Item1 == key);
        values.Add((key, value));
        if (key.StartsWith("Keys:k2", StringComparison.Ordinal))
            values.Add(("Keys:k2:Material", TestKeys.K2));

        using var provider = Build(Configuration([.. values]));
        var act = () => provider.GetRequiredService<IOptions<AuditLedgerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage(message);
    }

    [Fact]
    public async Task KeyringAuthenticator_ComputesAndVerifiesPerKey_AndRejectsUnknownKeys()
    {
        var authenticator = new KeyringAuditRecordAuthenticator(
            Microsoft.Extensions.Options.Options.Create(new AuditLedgerOptions
            {
                CurrentKeyId = "k2",
                Keys =
                {
                    ["k1"] = new AuditKeyOptions { Material = TestKeys.K1, Order = 1 },
                    ["k2"] = new AuditKeyOptions { Material = TestKeys.K2, Order = 2 },
                },
            }),
            new HmacSha256Signer());
        var message = "message"u8.ToArray();

        (await authenticator.GetCurrentKeyAsync()).Should().Be(new AuditKeyDescriptor("k2", 2));
        (await authenticator.FindKeyAsync("k1")).Should().Be(new AuditKeyDescriptor("k1", 1));
        (await authenticator.FindKeyAsync("k3")).Should().BeNull();

        var mac = await authenticator.ComputeMacAsync("k1", message);
        (await authenticator.VerifyMacAsync("k1", message, mac)).Should().BeTrue();
        (await authenticator.VerifyMacAsync("k2", message, mac)).Should().BeFalse();
        await authenticator.Invoking(a => a.ComputeMacAsync("k3", message).AsTask()).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void AcceptedCheckpointKeys_IncludeTheCurrentSigningKey()
    {
        var options = new AuditLedgerOptions { CheckpointSigningKeyId = "cp-2026", AcceptedCheckpointSigningKeyIds = { "cp-2025" } };

        options.EffectiveAcceptedCheckpointSigningKeyIds().Should().BeEquivalentTo("cp-2025", "cp-2026");
        new AuditLedgerOptions().EffectiveAcceptedCheckpointSigningKeyIds().Should().BeEmpty();
    }
}
