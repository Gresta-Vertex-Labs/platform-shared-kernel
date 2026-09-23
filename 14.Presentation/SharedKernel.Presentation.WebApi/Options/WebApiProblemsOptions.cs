namespace SharedKernel.Presentation.WebApi.Options;

/// <summary>Settings for RFC 9457 <c>application/problem+json</c> error responses.</summary>
public sealed class WebApiProblemsOptions
{
    /// <summary>
    /// Gets or sets an absolute base URI for the <c>type</c> member. When set, every problem's <c>type</c> is this URI
    /// followed by its <c>errorCode</c>, such as <c>https://errors.example.com/not_found.default</c>, so a service
    /// can document each code at a stable address. When <see langword="null"/> (the default), <c>type</c> is the
    /// RFC section of the status code.
    /// </summary>
    public Uri? TypeBaseUri { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an unhandled exception's type, message and stack trace are returned
    /// in the <c>exception</c> member. <see langword="null"/> (the default) returns them in the Development
    /// environment only. Never enable this in production: stack traces reveal internals.
    /// </summary>
    public bool? IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> sent with a 503 Service Unavailable, rounded up to whole seconds.
    /// <see langword="null"/> (the default) sends none.
    /// </summary>
    public TimeSpan? UnavailableRetryAfter { get; set; }
}
