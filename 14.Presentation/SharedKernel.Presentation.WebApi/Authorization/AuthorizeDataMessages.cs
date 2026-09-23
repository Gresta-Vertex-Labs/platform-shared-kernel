namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>Messages shared by the authorization attributes.</summary>
internal static class AuthorizeDataMessages
{
    public const string Fixed =
        "The policy of a SharedKernel authorization attribute is set by its constructor. Use [Authorize] for a named policy or schemes.";
}
