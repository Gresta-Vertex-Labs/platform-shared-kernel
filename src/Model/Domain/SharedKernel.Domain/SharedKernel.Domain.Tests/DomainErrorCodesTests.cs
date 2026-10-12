using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>Pins the error codes and error types the package owns.</summary>
public sealed partial class DomainErrorCodesTests
{
    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*(\\.[a-z][a-z0-9]*(_[a-z0-9]+)*){1,2}$")]
    private static partial Regex CodeFormat();

    private static IEnumerable<FieldInfo> AllConstants() =>
        typeof(DomainErrorCodes).GetNestedTypes(BindingFlags.Public)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string));

    [Fact]
    public void EveryConstant_IsPinnedByLiteral()
    {
        var pinned = new Dictionary<string, string>
        {
            [nameof(DomainErrorCodes.Currency) + "." + nameof(DomainErrorCodes.Currency.InvalidFormat)] =
                "currency.code.invalid_format",
            [nameof(DomainErrorCodes.Currency) + "." + nameof(DomainErrorCodes.Currency.Unknown)] =
                "currency.code.unknown",
            [nameof(DomainErrorCodes.Money) + "." + nameof(DomainErrorCodes.Money.CurrencyMismatch)] =
                "money.currency_mismatch",
            [nameof(DomainErrorCodes.Money) + "." + nameof(DomainErrorCodes.Money.StoredPrecisionExceeded)] =
                "money.stored_precision_exceeded",
        };

        var actual = AllConstants().ToDictionary(
            f => f.DeclaringType!.Name + "." + f.Name,
            f => (string)f.GetRawConstantValue()!);

        actual.Should().BeEquivalentTo(pinned);
    }

    [Fact]
    public void EveryConstant_FollowsTheCodeFormat()
    {
        foreach (var field in AllConstants())
            CodeFormat().IsMatch((string)field.GetRawConstantValue()!).Should().BeTrue($"{field.Name} is a C1 code");
    }

    [Fact]
    public void CurrencyMismatchRule_AliasesTheConstant() =>
        CurrencyMismatchRule.ErrorCode.Should().Be(DomainErrorCodes.Money.CurrencyMismatch);

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    public void CurrencyCreate_InvalidFormat_IsValidation(string input)
    {
        var result = Currency.Create(input);

        result.Errors.Should().ContainSingle().Which.Should().Match<Error>(e =>
            e.Code == DomainErrorCodes.Currency.InvalidFormat && e.Type == ErrorType.Validation);
    }

    [Fact]
    public void CurrencyCreate_UnknownCode_IsValidation()
    {
        var result = Currency.Create("ZZZ");

        result.Errors.Should().ContainSingle().Which.Should().Match<Error>(e =>
            e.Code == DomainErrorCodes.Currency.Unknown && e.Type == ErrorType.Validation);
    }

    [Fact]
    public void MoneyAcrossCurrencies_IsBusinessRule()
    {
        var usd = Money.Create(1m, Currency.Usd).Value;
        var eur = Money.Create(1m, Currency.Eur).Value;

        var ex = FluentActions.Invoking(() => usd + eur).Should().Throw<BusinessRuleViolationException>().Which;

        ex.Error.Code.Should().Be(DomainErrorCodes.Money.CurrencyMismatch);
        ex.Error.Type.Should().Be(ErrorType.BusinessRule);
        ex.Error.Message.Should().Be("Expected an amount in USD but got EUR.");
    }

    [Fact]
    public void StoredAmountWithExcessDecimals_IsAnUnexpectedDomainException()
    {
        // Before: thrown as BusinessRuleViolationException with ErrorType.BusinessRule (HTTP 422).
        var ctor = typeof(Money).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(decimal), typeof(Currency)]);
        ctor.Should().NotBeNull();

        var act = () => ctor!.Invoke([10.005m, Currency.Usd]);

        var ex = act.Should().Throw<TargetInvocationException>().Which.InnerException.Should()
            .BeOfType<DomainException>().Which;
        ex.Should().NotBeOfType<BusinessRuleViolationException>();
        ex.Error.Code.Should().Be(DomainErrorCodes.Money.StoredPrecisionExceeded);
        ex.Error.Type.Should().Be(ErrorType.Unexpected);
        ex.Error.Message.Should().EndWith(".");
    }

    [Fact]
    public void DomainAssembly_NeverSpellsThePrimitivesRuleViolatedCode()
    {
        // The assembly carries no rule-violated literal (the const would be inlined into the user-string heap),
        // whatever value Primitives gives it today.
        var forbidden = new[] { ErrorCodes.Domain.RuleViolated, "domain.rule.violated", "domain.rule_violated" };

        using var stream = File.OpenRead(typeof(DomainErrorCodes).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var literals = new List<string>();
        var handle = reader.GetNextHandle(MetadataTokens.UserStringHandle(0));
        while (!handle.IsNil)
        {
            literals.Add(reader.GetUserString(handle));
            handle = reader.GetNextHandle(handle);
        }

        literals.Should().NotBeEmpty();
        literals.Should().NotContain(forbidden);
    }
}
