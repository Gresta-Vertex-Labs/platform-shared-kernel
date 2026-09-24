namespace SharedKernel.Presentation.WebApi;

/// <summary>Settings for the correlation id every request carries, read from and written to <c>X-Correlation-Id</c>.</summary>
/// <remarks>
/// A caller-supplied value is used only when it is at most <see cref="MaxLength"/> characters, contains no control
/// character and matches <see cref="AllowedCharacterPattern"/>. Otherwise the request gets the current trace id
/// (or a new identifier when there is no trace), so one id ties logs and traces together and an unvalidated,
/// caller-controlled string never reaches logs, baggage or the response.
/// </remarks>
public sealed class WebApiCorrelationIdOptions
{
    /// <summary>The default value of <see cref="AllowedCharacterPattern"/>: letters, digits, <c>-</c>, <c>_</c>, <c>:</c> and <c>.</c>.</summary>
    public const string DefaultAllowedCharacterPattern = @"^[A-Za-z0-9\-_:.]+$";

    /// <summary>
    /// Gets or sets a value indicating whether correlation ids are resolved and written. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the longest caller-supplied correlation id accepted. Defaults to 128 characters.</summary>
    public int MaxLength { get; set; } = 128;

    /// <summary>
    /// Gets or sets the regular expression a caller-supplied correlation id must match. Defaults to
    /// <see cref="DefaultAllowedCharacterPattern"/>, which keeps GUIDs, ULIDs and trace ids unchanged.
    /// </summary>
    public string AllowedCharacterPattern { get; set; } = DefaultAllowedCharacterPattern;
}
