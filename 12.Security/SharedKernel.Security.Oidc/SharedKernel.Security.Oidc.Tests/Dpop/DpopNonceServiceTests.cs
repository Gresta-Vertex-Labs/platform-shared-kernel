using Microsoft.AspNetCore.DataProtection;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Dpop;

public sealed class DpopNonceServiceTests
{
    private readonly FakeClock _clock = new();
    private readonly OidcAuthenticationOptions _options = new();
    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    [Fact]
    public void IsValid_FreshNonce_IsTrue()
    {
        DpopNonceService service = CreateService();

        Assert.True(service.IsValid(service.Create()));
    }

    [Fact]
    public void Create_TwoNonces_AreDifferent()
    {
        DpopNonceService service = CreateService();

        Assert.NotEqual(service.Create(), service.Create());
    }

    [Fact]
    public void IsValid_AtExpiry_IsTrue()
    {
        DpopNonceService service = CreateService();
        string nonce = service.Create();

        _clock.Advance(_options.Dpop.NonceLifetime);

        Assert.True(service.IsValid(nonce));
    }

    [Fact]
    public void IsValid_AfterExpiry_IsFalse()
    {
        DpopNonceService service = CreateService();
        string nonce = service.Create();

        _clock.Advance(_options.Dpop.NonceLifetime + TimeSpan.FromSeconds(1));

        Assert.False(service.IsValid(nonce));
    }

    [Fact]
    public void IsValid_ConfiguredLifetime_IsUsed()
    {
        _options.Dpop.NonceLifetime = TimeSpan.FromMinutes(1);
        DpopNonceService service = CreateService();
        string nonce = service.Create();

        _clock.Advance(TimeSpan.FromSeconds(61));

        Assert.False(service.IsValid(nonce));
    }

    [Fact]
    public void IsValid_NonceFromAnotherKeyRing_IsFalse()
    {
        string foreign = new DpopNonceService(new EphemeralDataProtectionProvider(), _clock, Monitor()).Create();

        Assert.False(CreateService().IsValid(foreign));
    }

    [Fact]
    public void IsValid_PayloadProtectedForAnotherPurpose_IsFalse()
    {
        byte[] payload = new byte[24];
        string other = System.Buffers.Text.Base64Url.EncodeToString(_dataProtection.CreateProtector("another-purpose").Protect(payload));

        Assert.False(CreateService().IsValid(other));
    }

    [Fact]
    public void IsValid_WrongPayloadLengthUnderSamePurpose_IsFalse()
    {
        byte[] payload = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload, long.MaxValue);
        string nonce = System.Buffers.Text.Base64Url.EncodeToString(
            _dataProtection.CreateProtector("SharedKernel.Security.Oidc.DpopNonce.v1").Protect(payload));

        Assert.False(CreateService().IsValid(nonce));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void IsValid_InvalidInput_IsFalse(string? nonce)
    {
        Assert.False(CreateService().IsValid(nonce));
    }

    [Fact]
    public void IsValid_TooLong_IsFalse()
    {
        Assert.False(CreateService().IsValid(new string('A', 513)));
    }

    [Fact]
    public void IsValid_TamperedNonce_IsFalse()
    {
        DpopNonceService service = CreateService();
        char[] nonce = service.Create().ToCharArray();
        nonce[^5] = nonce[^5] == 'A' ? 'B' : 'A';

        Assert.False(service.IsValid(new string(nonce)));
    }

    private DpopNonceService CreateService() => new(_dataProtection, _clock, Monitor());

    private StaticOptionsMonitor<OidcAuthenticationOptions> Monitor() => new(_options);
}
