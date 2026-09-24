namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Endpoint metadata that says whether an <c>If-Match</c> entity tag can be a version of the endpoint's resource at
/// all. Added by an <see cref="IfMatch{TVersion}"/> parameter, nullable or not, so a tag that does not parse as
/// <c>TVersion</c> — and therefore can never match the current version — is answered 412 before the handler runs,
/// whether the endpoint requires the header or accepts it.
/// </summary>
internal interface IEntityTagValidator
{
    /// <summary>Returns <see langword="true"/> when <paramref name="opaqueTag"/> (without quotes) can be a version.</summary>
    bool IsValid(string opaqueTag);
}
