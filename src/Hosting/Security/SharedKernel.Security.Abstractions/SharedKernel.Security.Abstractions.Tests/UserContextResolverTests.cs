using SharedKernel.Execution.Context;
using System.Security.Claims;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class UserContextResolverTests
{
    [Fact]
    public void Resolve_NullMappers_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => UserContextResolver.Resolve(new ClaimsPrincipal(), null!));
    }

    [Fact]
    public void Resolve_NullPrincipal_ReturnsAnonymous()
    {
        var mapper = new RecordingMapper("Bearer");

        var context = UserContextResolver.Resolve(null, [mapper]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Empty(mapper.Mapped);
    }

    [Fact]
    public void Resolve_PrincipalWithoutIdentities_ReturnsAnonymous()
    {
        var context = UserContextResolver.Resolve(new ClaimsPrincipal(), [new RecordingMapper("Bearer")]);

        Assert.Same(AnonymousUserContext.Instance, context);
    }

    [Fact]
    public void Resolve_UnauthenticatedIdentity_ReturnsAnonymousWithoutMapping()
    {
        var mapper = new RecordingMapper(string.Empty);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject-1")]));

        var context = UserContextResolver.Resolve(principal, [mapper]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Empty(mapper.Mapped);
    }

    [Fact]
    public void Resolve_IdentityReportsUnauthenticatedDespiteMatchingType_IsNotMapped()
    {
        var mapper = new RecordingMapper("Bearer");
        var principal = new ClaimsPrincipal(new UnauthenticatedIdentity("Bearer"));

        var context = UserContextResolver.Resolve(principal, [mapper]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Empty(mapper.Mapped);
    }

    [Fact]
    public void Resolve_NoMapperForScheme_ReturnsAnonymous()
    {
        var mapper = new RecordingMapper("Bearer");
        var principal = new ClaimsPrincipal(Identity("Cookies"));

        var context = UserContextResolver.Resolve(principal, [mapper]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Empty(mapper.Mapped);
    }

    [Fact]
    public void Resolve_NoMappersRegistered_ReturnsAnonymous()
    {
        var context = UserContextResolver.Resolve(new ClaimsPrincipal(Identity("Bearer")), []);

        Assert.Same(AnonymousUserContext.Instance, context);
    }

    [Fact]
    public void Resolve_MatchingMapper_ReturnsMappedContextForThatIdentity()
    {
        var identity = Identity("Bearer");
        var mapper = new RecordingMapper("Bearer");

        var context = UserContextResolver.Resolve(new ClaimsPrincipal(identity), [mapper]);

        Assert.Same(mapper.Result, context);
        Assert.Same(identity, Assert.Single(mapper.Mapped));
    }

    [Fact]
    public void Resolve_SeveralMappers_PicksMapperByAuthenticationTypeRegardlessOfOrder()
    {
        var apiKey = new RecordingMapper("ApiKey");
        var bearer = new RecordingMapper("Bearer");
        var mtls = new RecordingMapper("Certificate");

        var context = UserContextResolver.Resolve(new ClaimsPrincipal(Identity("Bearer")), [apiKey, mtls, bearer]);

        Assert.Same(bearer.Result, context);
        Assert.Empty(apiKey.Mapped);
        Assert.Empty(mtls.Mapped);
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("Bearer ")]
    public void Resolve_AuthenticationTypeDiffersInCaseOrText_ReturnsAnonymous(string mapperType)
    {
        var mapper = new RecordingMapper(mapperType);

        var context = UserContextResolver.Resolve(new ClaimsPrincipal(Identity("Bearer")), [mapper]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Empty(mapper.Mapped);
    }

    [Fact]
    public void Resolve_FirstIdentityUnauthenticated_UsesNextAuthenticatedIdentity()
    {
        var bearerIdentity = Identity("Bearer");
        var principal = new ClaimsPrincipal([new UnauthenticatedIdentity("Bearer"), bearerIdentity]);
        var mapper = new RecordingMapper("Bearer");

        var context = UserContextResolver.Resolve(principal, [mapper]);

        Assert.Same(mapper.Result, context);
        Assert.Same(bearerIdentity, Assert.Single(mapper.Mapped));
    }

    [Fact]
    public void Resolve_FirstAuthenticatedIdentityHasNoMapper_UsesNextIdentityWithMapper()
    {
        var apiKeyIdentity = Identity("ApiKey");
        var principal = new ClaimsPrincipal([new ClaimsIdentity(), Identity("Cookies"), apiKeyIdentity]);
        var mapper = new RecordingMapper("ApiKey");

        var context = UserContextResolver.Resolve(principal, [mapper]);

        Assert.Same(mapper.Result, context);
        Assert.Same(apiKeyIdentity, Assert.Single(mapper.Mapped));
    }

    [Fact]
    public void Resolve_TwoMappableIdentities_UsesFirstIdentityOnly()
    {
        var bearerIdentity = Identity("Bearer");
        var principal = new ClaimsPrincipal([bearerIdentity, Identity("ApiKey")]);
        var bearer = new RecordingMapper("Bearer");
        var apiKey = new RecordingMapper("ApiKey");

        var context = UserContextResolver.Resolve(principal, [apiKey, bearer]);

        Assert.Same(bearer.Result, context);
        Assert.Empty(apiKey.Mapped);
    }

    [Fact]
    public void Resolve_MapperReturnsAnonymous_ReturnsAnonymousWithoutTryingLaterIdentities()
    {
        var bearer = new RecordingMapper("Bearer", AnonymousUserContext.Instance);
        var apiKey = new RecordingMapper("ApiKey");
        var principal = new ClaimsPrincipal([Identity("Bearer"), Identity("ApiKey")]);

        var context = UserContextResolver.Resolve(principal, [bearer, apiKey]);

        Assert.Same(AnonymousUserContext.Instance, context);
        Assert.Single(bearer.Mapped);
        Assert.Empty(apiKey.Mapped);
    }

    [Fact]
    public void Resolve_DuplicateMappersForScheme_UsesFirstRegistered()
    {
        var first = new RecordingMapper("Bearer");
        var second = new RecordingMapper("Bearer");

        var context = UserContextResolver.Resolve(new ClaimsPrincipal(Identity("Bearer")), [first, second]);

        Assert.Same(first.Result, context);
        Assert.Empty(second.Mapped);
    }

    [Fact]
    public void Resolve_LazyMapperSequence_EnumeratesOnce()
    {
        var enumerations = 0;
        IEnumerable<IUserContextMapper> Mappers()
        {
            enumerations++;
            yield return new RecordingMapper("ApiKey");
        }

        var principal = new ClaimsPrincipal([Identity("Cookies"), Identity("Other"), Identity("ApiKey")]);

        UserContextResolver.Resolve(principal, Mappers());

        Assert.Equal(1, enumerations);
    }

    private static ClaimsIdentity Identity(string authenticationType) =>
        new([new Claim(SecurityClaimTypes.Subject, "subject-" + authenticationType)], authenticationType);

    private sealed class RecordingMapper(string authenticationType, IUserContext? result = null) : IUserContextMapper
    {
        public List<ClaimsIdentity> Mapped { get; } = [];

        public IUserContext Result { get; } = result ?? new UserContext(ActorKind.User, "mapped-" + authenticationType);

        public string AuthenticationType => authenticationType;

        public IUserContext Map(ClaimsIdentity identity)
        {
            Mapped.Add(identity);
            return Result;
        }
    }

    private sealed class UnauthenticatedIdentity(string authenticationType)
        : ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, "forged")], authenticationType)
    {
        public override bool IsAuthenticated => false;
    }
}
