using SharedKernel.Domain.Monetary;
using SharedKernel.Testing.Domain;

namespace SharedKernel.Testing.SelfTests.Domain;

/// <summary>
/// Proves <see cref="MoneyFaker"/> — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class MoneyFakerTests
{
    [Fact]
    public void Generate_NoArguments_ProducesValidMoney()
    {
        var faker = new MoneyFaker();

        var money = faker.Generate();

        Assert.NotNull(money.Currency);
    }

    [Fact]
    public void Generate_ExplicitCurrencyAndAmount_UsesThem()
    {
        var faker = new MoneyFaker();

        var money = faker.Generate(Currency.Usd, 42.5m);

        Assert.Equal(Currency.Usd, money.Currency);
        Assert.Equal(42.5m, money.Amount);
    }

    [Fact]
    public void Generate_DefaultPool_CanProduceZeroDecimalCurrency()
    {
        var faker = new MoneyFaker();
        var jpy = Currency.Create("JPY").Value;

        var money = faker.Generate(jpy, 100m);

        Assert.Equal(0, money.Amount % 1);
        Assert.Equal(0, jpy.MinorUnitDigits);
    }

    [Fact]
    public void Generate_DefaultPool_CanProduceThreeDecimalCurrency()
    {
        var faker = new MoneyFaker();
        var bhd = Currency.Create("BHD").Value;

        var money = faker.Generate(bhd, 1.234m);

        Assert.Equal(3, bhd.MinorUnitDigits);
        Assert.Equal(1.234m, money.Amount);
    }

    [Fact]
    public void GenerateMany_ProducesRequestedCount()
    {
        var faker = new MoneyFaker();

        var moneys = faker.GenerateMany(5, Currency.Usd);

        Assert.Equal(5, moneys.Count);
        Assert.All(moneys, m => Assert.Equal(Currency.Usd, m.Currency));
    }

    [Fact]
    public void GenerateMany_ZeroCount_ReturnsEmpty()
    {
        var faker = new MoneyFaker();

        var moneys = faker.GenerateMany(0);

        Assert.Empty(moneys);
    }

    [Fact]
    public void GenerateMany_NegativeCount_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoneyFaker().GenerateMany(-1));

    [Fact]
    public void Generate_IsDeterministicAcrossInstances()
    {
        var first = new MoneyFaker().GenerateMany(20);
        var second = new MoneyFaker().GenerateMany(20);

        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Currency, second[i].Currency);
            Assert.Equal(first[i].Amount, second[i].Amount);
        }
    }
}
