namespace SharedKernel.Storage;

/// <summary>
/// A presigned HTML-form upload returned by <see cref="IFileStorage.CreateUploadFormAsync"/>: the browser
/// <c>POST</c>s a <c>multipart/form-data</c> body straight to the provider.
/// </summary>
/// <remarks>
/// <para>
/// Build the form in this order: every entry of <see cref="Fields"/> as a form field; when
/// <see cref="PresignedPostOptions.ContentType"/> was a prefix such as <c>image/</c>, a <c>Content-Type</c> field
/// with the file's actual type (for an exact type it is already in <see cref="Fields"/>); then the file itself as
/// the last field, named <c>file</c>. The provider rejects a file outside the size range or content type, or a
/// changed field, before storing anything. A successful S3 upload answers <c>204 No Content</c>.
/// </para>
/// <para>
/// Send the file part with a plain <c>filename="..."</c> parameter, as browsers do. Huawei Cloud OBS rejects the
/// <c>filename*=</c> form that .NET's <c>MultipartFormDataContent.Add(content, name, fileName)</c> produces.
/// </para>
/// </remarks>
public sealed record PresignedPost
{
    /// <summary>Gets the form action URL (the bucket endpoint).</summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Gets the form fields to submit before the file, with exactly these names and values: the key, the signed
    /// policy and signature, and any content type, metadata, encryption and storage-class fields the policy pins.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    /// <summary>Gets when the policy expires; submissions after it are rejected by the provider.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
