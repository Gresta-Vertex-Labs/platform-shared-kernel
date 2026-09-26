using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Validation;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

/// <summary>
/// <c>AddFluentValidationRequestValidators</c>: the bridge into the request pipeline, and the validators of the given
/// assemblies.
/// </summary>
public sealed class FluentValidationServiceCollectionExtensionsTests
{
    public sealed record RenameCustomer(string Name);

    public sealed class RenameCustomerValidator : AbstractValidator<RenameCustomer>
    {
        public RenameCustomerValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    internal sealed record CloseAccount(string Reason);

    internal sealed class CloseAccountValidator : AbstractValidator<CloseAccount>
    {
        public CloseAccountValidator() => RuleFor(x => x.Reason).NotEmpty();
    }

    private static readonly System.Reflection.Assembly TestAssembly = typeof(FluentValidationServiceCollectionExtensionsTests).Assembly;

    [Fact]
    public void WithAssemblies_RegistersTheirPublicValidators_Scoped()
    {
        var services = new ServiceCollection();

        services.AddFluentValidationRequestValidators(TestAssembly);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IValidator<RenameCustomer>));
        Assert.Equal(typeof(RenameCustomerValidator), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void WithAssemblies_RegistersTheirInternalValidators()
    {
        var services = new ServiceCollection();

        services.AddFluentValidationRequestValidators(TestAssembly);

        Assert.Contains(services, d => d.ServiceType == typeof(IValidator<CloseAccount>)
            && d.ImplementationType == typeof(CloseAccountValidator));
    }

    [Fact]
    public async Task WithAssemblies_TheRequestValidatorRunsTheScannedValidator()
    {
        var services = new ServiceCollection();
        services.AddFluentValidationRequestValidators(TestAssembly);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var validator = Assert.Single(scope.ServiceProvider.GetServices<IRequestValidator<RenameCustomer>>());
        var errors = await validator.ValidateAsync(new RenameCustomer(""), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, Assert.Single(errors).Type);
    }

    [Fact]
    public void CalledTwiceOrWithTheSameAssemblyTwice_RegistersEachValidatorAndTheBridgeOnce()
    {
        var services = new ServiceCollection();

        services.AddFluentValidationRequestValidators(TestAssembly, TestAssembly);
        services.AddFluentValidationRequestValidators(TestAssembly);

        Assert.Single(services, d => d.ServiceType == typeof(IValidator<RenameCustomer>));
        Assert.Single(services, d => d.ServiceType == typeof(IRequestValidator<>));
    }

    [Fact]
    public void AValidatorRegisteredByHand_IsNotRegisteredAgain()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<RenameCustomer>, RenameCustomerValidator>();

        services.AddFluentValidationRequestValidators(TestAssembly);

        Assert.Single(services, d => d.ServiceType == typeof(IValidator<RenameCustomer>));
    }

    [Fact]
    public void WithoutAssemblies_RegistersTheBridgeOnly()
    {
        var services = new ServiceCollection();

        services.AddFluentValidationRequestValidators();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IRequestValidator<>), descriptor.ServiceType);
        Assert.Equal(typeof(FluentValidationRequestValidator<>), descriptor.ImplementationType);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFluentValidationRequestValidators());
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFluentValidationRequestValidators(null!));
    }
}
