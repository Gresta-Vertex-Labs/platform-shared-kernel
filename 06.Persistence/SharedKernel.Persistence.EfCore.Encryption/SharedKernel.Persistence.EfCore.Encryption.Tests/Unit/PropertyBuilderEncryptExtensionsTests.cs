using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class PropertyBuilderEncryptExtensionsTests
{
    private static EntityTypeBuilder<EncCustomer> NewBuilder() =>
        new ModelBuilder().Entity<EncCustomer>();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Encrypt_NullOrWhitespacePurpose_Throws(string? purpose)
    {
        var act = () => NewBuilder().Property(x => x.Email).Encrypt(purpose!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Customer.Email")] // uppercase not allowed
    [InlineData("customer email")] // space not allowed
    [InlineData(".customer")] // leading dot not allowed
    [InlineData("customer.")] // trailing dot not allowed
    [InlineData("customer..email")] // doubled dot (empty segment) not allowed
    public void Encrypt_MalformedPurpose_Throws(string purpose)
    {
        var act = () => NewBuilder().Property(x => x.Email).Encrypt(purpose);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encrypt_TooLongPurpose_Throws()
    {
        var purpose = "a." + new string('b', PropertyBuilderEncryptExtensions.MaxPurposeLength);
        var act = () => NewBuilder().Property(x => x.Email).Encrypt(purpose);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encrypt_WellFormedPurpose_Succeeds()
    {
        var act = () => NewBuilder().Property(x => x.Email).Encrypt("customer.email");
        act.Should().NotThrow();
    }

    [Fact]
    public void Encrypt_PurposeWithUnderscore_Succeeds()
    {
        // This class's own XML doc example, "payment.card_number", needs underscores to be valid.
        var act = () => NewBuilder().Property(x => x.Email).Encrypt("payment.card_number");
        act.Should().NotThrow();
    }
}
