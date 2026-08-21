namespace SharedKernel.Presentation.WebApi.Uploads;

/// <summary>
/// Configures the platform-default file/multipart upload size and content-type validation applied
/// by <see cref="UploadValidationEndpointFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS A BOUNDARY-SHAPE CHECK ONLY.</b> VIRUS/MALWARE SCANNING AND ANTIVIRUS-ENGINE
/// INTEGRATION ARE EXPLICITLY OUT OF SCOPE AND MUST NEVER BE IMPLIED AS COVERED BY THIS
/// CAPABILITY — IT VALIDATES DECLARED SIZE, DECLARED CONTENT-TYPE, AND (OPTIONALLY) A LEADING
/// MAGIC-BYTE SIGNATURE ONLY.
/// </para>
/// <para>
/// No third-party MIME-detection library is used — <see cref="AllowedMagicBytes"/> is a small,
/// locally-maintained, extensible signature table, keeping this package dependency-light.
/// </para>
/// </remarks>
public sealed class UploadValidationOptions
{
    /// <summary>
    /// Gets or sets the platform-default maximum accepted upload size, in bytes. Defaults to
    /// <c>10_485_760</c> (10 MB). Overridable per endpoint via
    /// <see cref="RequireValidatedUploadAttribute"/>'s constructor argument.
    /// </summary>
    public long MaxSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets the platform-default set of accepted <c>Content-Type</c> values. Empty means no
    /// content-type restriction is applied by default. Overridable per endpoint via
    /// <see cref="RequireValidatedUploadAttribute"/>'s constructor argument.
    /// </summary>
    public ICollection<string> AllowedContentTypes { get; } = new List<string>();

    /// <summary>
    /// Gets an optional mapping from content-type to its expected leading magic-byte signature, for
    /// a deeper check beyond the declared <c>Content-Type</c> header alone. A content type with no
    /// entry here skips the magic-byte check entirely.
    /// </summary>
    public IDictionary<string, byte[]> AllowedMagicBytes { get; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
}
