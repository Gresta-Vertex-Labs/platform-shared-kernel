namespace SharedKernel.Security.Oidc.Options;

/// <summary>Whether DPoP is optional or mandatory.</summary>
public enum DpopMode
{
    /// <summary>DPoP-bound tokens must be presented with a proof; unbound bearer tokens are accepted.</summary>
    Allowed = 0,

    /// <summary>Every token must be DPoP-bound and presented with a proof.</summary>
    Required,
}
