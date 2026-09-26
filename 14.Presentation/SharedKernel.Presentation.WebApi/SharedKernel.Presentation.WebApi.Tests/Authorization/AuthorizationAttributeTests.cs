using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
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
        new RequireEndpointPermissionAttribute("orders.read", "orders:admin"),
        new RequireRoleAttribute("auditor"),
        new RequireFreshAuthenticationAttribute(300),
        new RequireAuthenticationMethodAttribute("mfa", "hwk"),
        new RequireAuthenticationMethodAttribute("otp") { MaxAgeSeconds = 300 },
    };

    public static TheoryData<IAuthorizeData, string> EncodedPolicies => new()
    {
        { new RequireEndpointPermissionAttribute("orders.read", "orders:admin"), "SharedKernel:permission:orders.read|orders:admin" },
        { new RequireRoleAttribute("auditor"), "SharedKernel:role:auditor" },
        { new RequireFreshAuthenticationAttribute(300), "SharedKernel:fresh:300" },
        { new RequireAuthenticationMethodAttribute("mfa", "hwk"), "SharedKernel:amr:mfa|hwk" },
        { new RequireAuthenticationMethodAttribute("otp", "hwk") { MaxAgeSeconds = 300 }, "SharedKernel:amr-max-age:300|otp|hwk" },
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
    [InlineData(typeof(RequireEndpointPermissionAttribute))]
    [InlineData(typeof(RequireRoleAttribute))]
    [InlineData(typeof(RequireFreshAuthenticationAttribute))]
    [InlineData(typeof(RequireAuthenticationMethodAttribute))]
    public void PolicyAndRoles_AreReadOnly_SoTheInheritedSettersCannotBeNamedArguments(Type attributeType)
    {
        // A named attribute argument binds to the most derived member of that name. These read-only members hide
        // AuthorizeAttribute's setters, so [RequireEndpointPermission("x", Roles = "admin")] or Policy = "…" does not compile
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
        var attribute = new RequireEndpointPermissionAttribute("orders.read") { AuthenticationSchemes = "ApiKey" };
        var provider = new SharedKernelAuthorizationPolicyProvider(new DefaultAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions())));

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
        var provider = new SharedKernelAuthorizationPolicyProvider(new DefaultAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions())));

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
        var provider = new SharedKernelAuthorizationPolicyProvider(new DefaultAuthorizationPolicyProvider(Microsoft.Extensions.Options.Options.Create(options)));

        (await provider.GetPolicyAsync("service-policy")).Should().NotBeNull();
        (await provider.GetPolicyAsync("unknown")).Should().BeNull();
        (await provider.GetPolicyAsync("SharedKernel:permission:")).Should().BeNull();
        (await provider.GetDefaultPolicyAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task PolicyProvider_DecoratesTheProviderItWraps()
    {
        // R4: a service's own provider keeps answering its names, defaults and caching; the platform adds only its own.
        var inner = new DynamicPolicyProvider(allowsCaching: false);
        var provider = new SharedKernelAuthorizationPolicyProvider(inner);

        (await provider.GetPolicyAsync("dynamic:orders")).Should().BeSameAs(inner.DynamicPolicy);
        (await provider.GetDefaultPolicyAsync()).Should().BeSameAs(inner.DefaultPolicy);
        (await provider.GetFallbackPolicyAsync()).Should().BeNull();
        provider.AllowsCachingPolicies.Should().BeFalse("the decorated provider decides whether policies may be cached");
        (await provider.GetPolicyAsync(new RequireEndpointPermissionAttribute("orders.read").Policy))!
            .Requirements.Should().ContainSingle(requirement => requirement is PermissionRequirement);
    }

    [Fact]
    public void Properties_ReadByTheGrpcPackage_AreKept()
    {
        new RequireRoleAttribute("a", "b").Roles.Should().Equal("a", "b");
        new RequireEndpointPermissionAttribute("p").Permissions.Should().Equal("p");
        new RequireFreshAuthenticationAttribute(90).MaxAge.Should().Be(TimeSpan.FromSeconds(90));
        new RequireAuthenticationMethodAttribute("mfa").Methods.Should().Equal("mfa");
        new RequireAuthenticationMethodAttribute("mfa") { MaxAgeSeconds = 90 }.MaxAgeSeconds.Should().Be(90);
    }

    [Fact]
    public void X1_MethodAttributeWithoutMaxAge_KeepsItsPolicy_AndHasNoMaxAge()
    {
        var attribute = new RequireAuthenticationMethodAttribute("mfa", "hwk");

        attribute.MaxAgeSeconds.Should().Be(0);
        SharedKernelPolicyNames.TryCreateRequirement(attribute.Policy, out var requirement).Should().BeTrue();
        requirement.Should().BeOfType<AuthenticationMethodRequirement>().Which.MaxAge.Should().BeNull();
        requirement!.StepUpMaxAge.Should().BeNull();
    }

    [Fact]
    public void X1_MaxAge_RoundTripsThroughThePolicyName()
    {
        var attribute = new RequireAuthenticationMethodAttribute("otp", "hwk") { MaxAgeSeconds = 300 };

        SharedKernelPolicyNames.TryCreateRequirement(((IAuthorizeData)attribute).Policy!, out var requirement).Should().BeTrue();

        var method = requirement.Should().BeOfType<AuthenticationMethodRequirement>().Which;
        method.Methods.Should().Equal("otp", "hwk");
        method.MaxAge.Should().Be(TimeSpan.FromSeconds(300));
        method.StepUpMaxAge.Should().Be(TimeSpan.FromSeconds(300));
        method.IsStepUp.Should().BeTrue();
    }

    [Fact]
    public void X1_MethodThatLooksLikeANumber_IsNeverReadAsTheMaxAge()
    {
        SharedKernelPolicyNames.TryCreateRequirement(new RequireAuthenticationMethodAttribute("300").Policy, out var withoutAge).Should().BeTrue();
        SharedKernelPolicyNames.TryCreateRequirement(new RequireAuthenticationMethodAttribute("300") { MaxAgeSeconds = 60 }.Policy, out var withAge).Should().BeTrue();

        withoutAge.Should().BeOfType<AuthenticationMethodRequirement>().Which.Methods.Should().Equal("300");
        withoutAge.Should().BeOfType<AuthenticationMethodRequirement>().Which.MaxAge.Should().BeNull();
        withAge.Should().BeOfType<AuthenticationMethodRequirement>().Which.Methods.Should().Equal("300");
        withAge.Should().BeOfType<AuthenticationMethodRequirement>().Which.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void X1_MaxAgeSeconds_MustBePositive_AndARefusedValueLeavesThePolicy(int seconds)
    {
        var attribute = new RequireAuthenticationMethodAttribute("otp") { MaxAgeSeconds = 120 };

        FluentActions.Invoking(() => attribute.MaxAgeSeconds = seconds).Should().Throw<ArgumentOutOfRangeException>();

        attribute.MaxAgeSeconds.Should().Be(120);
        attribute.Policy.Should().Be("SharedKernel:amr-max-age:120|otp");
        ((AuthorizeAttribute)attribute).Policy.Should().Be("SharedKernel:amr-max-age:120|otp");
    }

    [Theory]
    [InlineData("SharedKernel:amr-max-age:300")]
    [InlineData("SharedKernel:amr-max-age:0|otp")]
    [InlineData("SharedKernel:amr-max-age:-5|otp")]
    [InlineData("SharedKernel:amr-max-age:+5|otp")]
    [InlineData("SharedKernel:amr-max-age:5m|otp")]
    [InlineData("SharedKernel:amr-max-age:|otp")]
    [InlineData("SharedKernel:amr-max-age:300| ")]
    [InlineData("SharedKernel:amr-max-age:99999999999|otp")]
    public void X1_MalformedMaxAgePolicyName_IsNotAPlatformPolicy(string policyName)
    {
        SharedKernelPolicyNames.TryCreateRequirement(policyName, out _).Should().BeFalse();
    }

    [Fact]
    public void X1_TimeSpanMethodConvention_NeedsAtLeastOneSecond_AndFitsAnInt()
    {
        var builder = new TestConventionBuilder();

        FluentActions.Invoking(() => builder.RequireAuthenticationMethod(TimeSpan.FromMilliseconds(500), "otp"))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => builder.RequireAuthenticationMethod(TimeSpan.FromSeconds(int.MaxValue + 1L), "otp"))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void X1_TimeSpanMethodConvention_AddsTheAttribute_WithWholeSeconds()
    {
        var builder = new CapturingConventionBuilder();

        builder.RequireAuthenticationMethod(TimeSpan.FromSeconds(90.9), "otp", "hwk");

        var attribute = builder.Metadata.OfType<RequireAuthenticationMethodAttribute>().Should().ContainSingle().Which;
        attribute.MaxAgeSeconds.Should().Be(90);
        attribute.Methods.Should().Equal("otp", "hwk");
        attribute.Policy.Should().Be("SharedKernel:amr-max-age:90|otp|hwk");
    }

    [Fact]
    public void PermissionWithAColon_SurvivesThePolicyName()
    {
        var attribute = new RequireEndpointPermissionAttribute("orders:read");

        SharedKernelPolicyNames.TryCreateRequirement(((IAuthorizeData)attribute).Policy!, out var requirement).Should().BeTrue();
        requirement.Should().BeOfType<PermissionRequirement>().Which.Permissions.Should().Equal("orders:read");
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        FluentActions.Invoking(() => new RequireEndpointPermissionAttribute()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RequireEndpointPermissionAttribute(" ")).Should().Throw<ArgumentException>();
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
        RequireEndpointPermissionAttribute permission => permission.Policy,
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

    // Applies each convention at once to an endpoint builder, so a test can read the metadata a convention adds.
    private sealed class CapturingConventionBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        private readonly Microsoft.AspNetCore.Routing.RouteEndpointBuilder _endpoint = new(
            _ => Task.CompletedTask,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/"),
            order: 0);

        public IList<object> Metadata => _endpoint.Metadata;

        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention) => convention(_endpoint);
    }
}
