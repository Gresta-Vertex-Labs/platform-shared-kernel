using Microsoft.Extensions.Options;
using SharedKernel.Communication.GraphQL.Options;

namespace SharedKernel.Communication.GraphQL.Tests;

public sealed class GraphQLOptionsTests
{
    private readonly GraphQLOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new GraphQLOptions();

        options.EnableFiltering.Should().BeTrue();
        options.EnableSorting.Should().BeTrue();
        options.EnablePaging.Should().BeTrue();
        options.MaxPageSize.Should().Be(100);
        options.AllowIntrospection.Should().BeTrue();
    }

    [Fact]
    public void Validator_Passes_WhenMaxPageSizeIsExactly500()
    {
        var options = new GraphQLOptions { MaxPageSize = 500 };
        var result = _validator.Validate(null, options);
        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validator_Fails_WhenMaxPageSizeExceeds500()
    {
        var options = new GraphQLOptions { MaxPageSize = 501 };
        var result = _validator.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("MaxPageSize"));
    }

    [Fact]
    public void Validator_Fails_WhenMaxPageSizeIsZero()
    {
        var options = new GraphQLOptions { MaxPageSize = 0 };
        var result = _validator.Validate(null, options);
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validator_Passes_WhenMaxPageSizeIs1()
    {
        var options = new GraphQLOptions { MaxPageSize = 1 };
        var result = _validator.Validate(null, options);
        result.Should().Be(ValidateOptionsResult.Success);
    }
}
