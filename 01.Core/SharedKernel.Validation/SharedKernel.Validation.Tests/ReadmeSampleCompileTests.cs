using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Guards;
using SharedKernel.Validation.NationalId;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

/// <summary>
/// Compiles the exact code samples shown in the 01.Core README's "SharedKernel.Validation"
/// section and this package's own README, so a documentation drift is caught by the build
/// rather than trusted on sight.
/// </summary>
public sealed class ReadmeSampleCompileTests
{
    private sealed record AccountRequest(string Iban);

    private static Result<string> CreateAccount(AccountRequest request)
    {
        // Standalone Result call
        Result validationResult = IbanValidator.Validate(request.Iban);
        if (validationResult.IsFailure)
        {
            return Result<string>.Failure(validationResult.Error);
        }

        // Guard.Against.* extension — same validator, same error codes
        Error? error = Guard.Against.InvalidIban(request.Iban);
        if (error is not null)
        {
            return Result<string>.Failure(error);
        }

        return Result<string>.Success(request.Iban);
    }

    [Fact]
    public void ReadmeSample_StandaloneResultAndGuardExtension_AgreeOnValidIban()
    {
        Result<string> result = CreateAccount(new AccountRequest("DE89370400440532013000"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ReadmeSample_StandaloneResultAndGuardExtension_AgreeOnInvalidIban()
    {
        Result<string> result = CreateAccount(new AccountRequest("not-an-iban"));

        Assert.True(result.IsFailure);
    }

    private sealed class KycService(INationalIdValidatorRegistry registry)
    {
        public Error? ValidateNationalId(string idNumber, string countryCode) =>
            Guard.Against.InvalidNationalId(idNumber, countryCode, registry);
    }

    [Fact]
    public void ReadmeSample_KycService_ValidatesAgainstRegistry()
    {
        var registry = new NationalIdValidatorRegistry();
        var service = new KycService(registry);

        Assert.Null(service.ValidateNationalId("10000000146", "TR"));
        Assert.NotNull(service.ValidateNationalId("10000000146", "ZZ"));
    }

    [Fact]
    public void ReadmeSample_LeiAbaRoutingNumberAndSepaCreditorIdentifier_ValidateAsDocumented()
    {
        Assert.True(LeiValidator.IsValid("506700GE1G29325QX363"));
        Assert.Null(Guard.Against.InvalidLei("506700GE1G29325QX363"));

        Assert.True(AbaRoutingNumberValidator.IsValid("111000025"));
        Assert.Null(Guard.Against.InvalidAbaRoutingNumber("111000025"));

        Assert.True(SepaCreditorIdentifierValidator.IsValid("DE98ZZZ09999999999"));
        Assert.Null(Guard.Against.InvalidSepaCreditorIdentifier("DE98ZZZ09999999999"));
    }
}
