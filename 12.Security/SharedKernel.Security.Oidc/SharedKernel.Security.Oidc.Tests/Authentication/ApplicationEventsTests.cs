using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Revocation;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Authentication;

public sealed class ApplicationEventsTests
{
    [Fact]
    public async Task OptionsEvents_OnTokenValidatedDoingNothing_StillRejectsRevokedToken()
    {
        var calls = new EventCalls();
        var check = new RecordingRevocationCheck { IsRevoked = _ => true };
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<ITokenRevocationCheck>(check);
            options.Oidc = oidc => oidc.AddTokenRevocation<RecordingRevocationCheck>();
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = _ =>
                    {
                        calls.TokenValidated++;
                        return Task.CompletedTask;
                    },
                });
        });

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls.TokenValidated);
    }

    [Fact]
    public async Task OptionsEvents_OnTokenValidatedCallingSuccess_StillRejectsBoundTokenAsBearer()
    {
        var calls = new EventCalls();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<IDpopReplayCache>(new InMemoryDpopReplayCache());
            options.Oidc = oidc => oidc.AddDpop<InMemoryDpopReplayCache>();
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        calls.TokenValidated++;
                        context.Success();
                        return Task.CompletedTask;
                    },
                });
        });

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToDpopKey(HandmadeProof.Thumbprint(key)).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls.TokenValidated);
    }

    [Fact]
    public async Task EventsType_CustomEventsCallingSuccess_StillRejectsCertificateBoundTokenWithoutCertificate()
    {
        var calls = new EventCalls();
        using X509Certificate2 certificate = new MtlsTestCertificateBuilder().Build().Certificate;
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services =>
            {
                services.AddSingleton(calls);
                services.AddScoped<SucceedingEvents>();
                services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt => jwt.EventsType = typeof(SucceedingEvents));
            });

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(certificate).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls.TokenValidated);
    }

    [Fact]
    public async Task EventsType_CustomEvents_StillRejectsRevokedToken()
    {
        var calls = new EventCalls();
        var check = new RecordingRevocationCheck { IsRevoked = _ => true };
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<ITokenRevocationCheck>(check);
            options.Oidc = oidc => oidc.AddTokenRevocation<RecordingRevocationCheck>();
            options.AfterOidc = services =>
            {
                services.AddSingleton(calls);
                services.AddScoped<SucceedingEvents>();
                services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt => jwt.EventsType = typeof(SucceedingEvents));
            };
        });

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls.TokenValidated);
        Assert.Single(check.Requests);
    }

    [Fact]
    public async Task OptionsEvents_OnTokenValidatedDroppingConfirmationClaim_StillRejectsBoundTokenAsBearer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<IDpopReplayCache>(new InMemoryDpopReplayCache());
            options.Oidc = oidc => oidc.AddDpop<InMemoryDpopReplayCache>();
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    // A typical claims-trimming transformation that happens to leave out cnf.
                    OnTokenValidated = context =>
                    {
                        string[] kept = ["sub", "roles", "scope"];
                        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                            context.Principal!.Claims.Where(claim => kept.Contains(claim.Type)),
                            OidcAuthenticationDefaults.AuthenticationScheme));
                        return Task.CompletedTask;
                    },
                });
        });

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToDpopKey(HandmadeProof.Thumbprint(key)).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OptionsEvents_OnTokenValidatedDroppingConfirmationClaim_StillRejectsCertificateBoundTokenWithoutCertificate()
    {
        using X509Certificate2 certificate = new MtlsTestCertificateBuilder().Build().Certificate;
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                            context.Principal!.Claims.Where(claim => claim.Type != "cnf"),
                            OidcAuthenticationDefaults.AuthenticationScheme));
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(certificate).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OptionsEvents_MessageReceivedShortCircuitsWithSuccess_IsRejected()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "forged")], OidcAuthenticationDefaults.AuthenticationScheme));
                        context.Success();
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OptionsEvents_MessageReceivedChallengeAndFailure_StillRun()
    {
        var calls = new EventCalls();
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = _ =>
                    {
                        calls.MessageReceived++;
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = _ =>
                    {
                        calls.AuthenticationFailed++;
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        calls.Challenge++;
                        context.Response.Headers["X-App-Challenge"] = "seen";
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().WithAudience("api://other").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls.MessageReceived);
        Assert.Equal(1, calls.AuthenticationFailed);
        Assert.Equal(1, calls.Challenge);
        Assert.Equal("seen", Assert.Single(response.Headers.GetValues("X-App-Challenge")));
    }

    [Fact]
    public async Task OptionsEvents_MessageReceivedSuppliesToken_IsStillValidated()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Query["access_token"];
                        return Task.CompletedTask;
                    },
                }));
        string valid = TestTokens.Create().Build();
        string invalid = TestTokens.Create().WithIssuer("https://attacker.example.test").Build();

        using HttpResponseMessage accepted = await host.SendAsync(token: null, path: $"/resource?access_token={valid}");
        using HttpResponseMessage rejected = await host.SendAsync(token: null, path: $"/resource?access_token={invalid}");

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task Configure_AppWeakensValidationBeforePostConfigure_IsPinnedBack()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.Configure<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme, jwt =>
            {
                jwt.MapInboundClaims = true;
                jwt.TokenValidationParameters.ValidateAudience = false;
                jwt.TokenValidationParameters.ValidateIssuer = false;
                jwt.TokenValidationParameters.ValidateLifetime = false;
                jwt.TokenValidationParameters.RequireSignedTokens = false;
            }));

        using HttpResponseMessage wrongAudience = await host.SendAsync(TestTokens.Create().WithAudience("api://other").Build());
        using HttpResponseMessage wrongIssuer = await host.SendAsync(TestTokens.Create().WithIssuer("https://attacker.example.test").Build());
        using HttpResponseMessage expired = await host.SendAsync(TestTokens.Create().WithLifetime(DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1)).Build());
        using HttpResponseMessage unsigned = await host.SendAsync(TestTokens.Unsigned(TestTokens.StandardPayload()));
        UserResponse user = await host.GetUserAsync(TestTokens.Create().WithClaim("email", "ada@example.test").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal("user-1", user.SubjectId);
        Assert.Equal("ada@example.test", user.Email);
        Assert.False(user.HasMappedNameIdentifier);
    }

    private sealed class EventCalls
    {
        public int MessageReceived { get; set; }

        public int TokenValidated { get; set; }

        public int AuthenticationFailed { get; set; }

        public int Challenge { get; set; }
    }

    private sealed class SucceedingEvents(EventCalls calls) : JwtBearerEvents
    {
        public override Task TokenValidated(TokenValidatedContext context)
        {
            calls.TokenValidated++;
            context.Success();
            return Task.CompletedTask;
        }
    }
}
