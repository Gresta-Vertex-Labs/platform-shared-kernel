using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// Design D3: the four attributes are <see cref="AuthorizeAttribute"/>s — so SignalR, which authorizes a hub method
/// only through <see cref="AuthorizeAttribute"/>, enforces them like every other framework — with a
/// <c>SharedKernel:</c> policy the constructor fixes. They validate their arguments, keep the properties
/// <c>SharedKernel.Presentation.Grpc</c> reads, and round-trip through the policy provider.
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

    public static TheoryData<IAuthorizeData, string> EncodedPolicies => new()
    {
        { new RequirePermissionAttribute("orders.read", "orders:admin"), "SharedKernel:permission:orders.read|orders:admin" },
        { new RequireRoleAttribute("auditor"), "SharedKernel:role:auditor" },
        { new RequireFreshAuthenticationAttribute(300), "SharedKernel:fresh:300" },
        { new RequireAuthenticationMethodAttribute("mfa", "hwk"), "SharedKernel:amr:mfa|hwk" },
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
    public void Attribute_IsAnAuthorizeAttribute(IAuthorizeData attribute)
    {
        // SignalR's hub dispatcher collects GetCustomAttributes<AuthorizeAttribute>() for a hub method; an IAuthorizeData
        // that is not an AuthorizeAttribute would be skipped there and the method would run for anyone connected.
        attribute.Should().BeAssignableTo<AuthorizeAttribute>();
    }

    [Theory]
    [MemberData(nameof(EncodedPolicies))]
    public void PolicyThroughIAuthorizeData_IsTheEncodedName_AndEveryViewAgrees(IAuthorizeData attribute, string encoded)
    {
        attribute.Policy.Should().Be(encoded);
        ((AuthorizeAttribute)attribute).Policy.Should().Be(encoded, "readers of AuthorizeAttribute.Policy see the same policy");
        DeclaredPolicy(attribute).Should().Be(encoded);
    }

    [Theory]
    [MemberData(nameof(Attributes))]
    public void InheritedRoles_IsNull(IAuthorizeData attribute)
    {
        // A non-null AuthorizeAttribute.Roles would add a claim-based role check that ignores IUserContext.
        ((AuthorizeAttribute)attribute).Roles.Should().BeNull();
        attribute.Roles.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(RequirePermissionAttribute))]
    [InlineData(typeof(RequireRoleAttribute))]
    [InlineData(typeof(RequireFreshAuthenticationAttribute))]
    [InlineData(typeof(RequireAuthenticationMethodAttribute))]
    public void PolicyAndRoles_AreReadOnly_SoTheInheritedSettersCannotBeNamedArguments(Type attributeType)
    {
        // A named attribute argument binds to the most derived member of that name. These read-only members hide
        // AuthorizeAttribute's setters, so [RequirePermission("x", Roles = "admin")] or Policy = "…" does not compile
        // (CS0617), while AuthenticationSchemes stays settable.
        foreach (var name in new[] { nameof(AuthorizeAttribute.Policy), nameof(AuthorizeAttribute.Roles) })
        {
            var property = attributeType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            property.Should().NotBeNull($"{attributeType.Name} declares its own {name}");
            property!.CanWrite.Should().BeFalse($"{attributeType.Name}.{name} must not be settable");
        }

        attributeType.GetProperty(nameof(AuthorizeAttribute.AuthenticationSchemes))!.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void RolesOfARoleAttribute_AreItsOwnList()
    {
        var attribute = new RequireRoleAttribute("auditor", "admin");

        attribute.Roles.Should().Equal("auditor", "admin");
        ((AuthorizeAttribute)attribute).Roles.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticationSchemes_CanBeSet_AndJoinThePolicy_WithoutReplacingTheRequirement()
    {
        var attribute = new RequirePermissionAttribute("orders.read") { AuthenticationSchemes = "ApiKey" };
        var provider = new SharedKernelAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions()));

        var policy = await AuthorizationPolicy.CombineAsync(provider, [attribute]);

        ((IAuthorizeData)attribute).AuthenticationSchemes.Should().Be("ApiKey");
        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Equal("ApiKey");
        policy.Requirements.Should().ContainSingle(requirement => requirement is PermissionRequirement);
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

    [Theory]
    [MemberData(nameof(Attributes))]
    public void PolicyOfAnAttribute_CannotBeChanged(IAuthorizeData attribute)
    {
        var policy = attribute.Policy;

        FluentActions.Invoking(() => attribute.Policy = "other").Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => attribute.Roles = "admin").Should().Throw<NotSupportedException>();
        attribute.Policy.Should().Be(policy);
        attribute.Roles.Should().BeNull();
    }

    [Fact]
    public void TimeSpanConvention_NeedsAtLeastOneSecond()
    {
        var builder = new TestConventionBuilder();

        FluentActions.Invoking(() => builder.RequireFreshAuthentication(TimeSpan.FromMilliseconds(500)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    // The Policy each attribute declares itself, which hides AuthorizeAttribute.Policy.
    private static string DeclaredPolicy(IAuthorizeData attribute) => attribute switch
    {
        RequirePermissionAttribute permission => permission.Policy,
        RequireRoleAttribute role => role.Policy,
        RequireFreshAuthenticationAttribute fresh => fresh.Policy,
        RequireAuthenticationMethodAttribute method => method.Policy,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Not a SharedKernel authorization attribute."),
    };

    private sealed class TestConventionBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention)
        {
        }
    }
}
