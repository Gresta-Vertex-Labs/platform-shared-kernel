using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// Design D3: the four attributes are native <see cref="IAuthorizeData"/> with a <c>SharedKernel:</c> policy, validate
/// their arguments, keep the properties <c>SharedKernel.Presentation.Grpc</c> reads, and round-trip through the
/// policy provider.
/// </summary>
public sealed class AuthorizationAttributeTests
{
    public static TheoryData<IAuthorizeData> Attributes => new()
    {
        new RequirePermissionAttribute("orders.read", "orders:admin"),
        new RequireRoleAttribute("auditor"),
        new RequireFreshAuthenticationAttribute(300),
        new RequireAuthenticationMethodAttribute("mfa", "hwk"),
    };

    [Theory]
    [MemberData(nameof(Attributes))]
    public void Attribute_IsANativeAuthorizationPolicy(IAuthorizeData attribute)
    {
        attribute.Policy.Should().StartWith("SharedKernel:");
        attribute.Roles.Should().BeNull();
        attribute.AuthenticationSchemes.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Attributes))]
    public async Task Policy_RequiresAnAuthenticatedUser_AndTheAttributesRequirement(IAuthorizeData attribute)
    {
        var provider = new SharedKernelAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(attribute.Policy!);

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(requirement => requirement is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        policy.Requirements.Should().ContainSingle(requirement => requirement is SharedKernelRequirement);
    }

    [Fact]
    public async Task PolicyProvider_LeavesOtherNamesToTheDefaultProvider()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("service-policy", policy => policy.RequireClaim("scope", "orders"));
        var provider = new SharedKernelAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(options));

        (await provider.GetPolicyAsync("service-policy")).Should().NotBeNull();
        (await provider.GetPolicyAsync("unknown")).Should().BeNull();
        (await provider.GetPolicyAsync("SharedKernel:permission:")).Should().BeNull();
        (await provider.GetDefaultPolicyAsync()).Should().NotBeNull();
    }

    [Fact]
    public void Properties_ReadByTheGrpcPackage_AreKept()
    {
        new RequireRoleAttribute("a", "b").Roles.Should().Equal("a", "b");
        new RequirePermissionAttribute("p").Permissions.Should().Equal("p");
        new RequireFreshAuthenticationAttribute(90).MaxAge.Should().Be(TimeSpan.FromSeconds(90));
        new RequireAuthenticationMethodAttribute("mfa").Methods.Should().Equal("mfa");
    }

    [Fact]
    public void PermissionWithAColon_SurvivesThePolicyName()
    {
        var attribute = new RequirePermissionAttribute("orders:read");

        SharedKernelPolicyNames.TryCreateRequirement(((IAuthorizeData)attribute).Policy!, out var requirement).Should().BeTrue();
        requirement.Should().BeOfType<PermissionRequirement>().Which.Permissions.Should().Equal("orders:read");
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        FluentActions.Invoking(() => new RequirePermissionAttribute()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RequirePermissionAttribute(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RequireRoleAttribute("a|b")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RequireRoleAttribute(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new RequireAuthenticationMethodAttribute("mfa", null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RequireFreshAuthenticationAttribute(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PolicyOfAnAttribute_CannotBeChanged()
    {
        IAuthorizeData attribute = new RequireRoleAttribute("auditor");

        FluentActions.Invoking(() => attribute.Policy = "other").Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => attribute.Roles = "admin").Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TimeSpanConvention_NeedsAtLeastOneSecond()
    {
        var builder = new TestConventionBuilder();

        FluentActions.Invoking(() => builder.RequireFreshAuthentication(TimeSpan.FromMilliseconds(500)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class TestConventionBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention)
        {
        }
    }
}
