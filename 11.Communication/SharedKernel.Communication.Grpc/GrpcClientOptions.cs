using System.ComponentModel.DataAnnotations;
using Grpc.Core;

namespace SharedKernel.Communication;

/// <summary>
/// One gRPC client: its address, deadline, retry policy, keepalive and credentials. Bound from
/// <c>SharedKernel:Communication:Clients:{name}</c> and validated at startup; change it in code with
/// <c>client.Configure(o =&gt; …)</c>.
/// </summary>
/// <example>
/// <code>
/// "Clients": {
///   "inventory-grpc": {
///     "Address": "http://_grpc.inventory",
///     "Deadline": "00:00:05",
///     "Retry": { "MaxAttempts": 4, "RetryableStatusCodes": [ "Unavailable", "ResourceExhausted" ] }
///   }
/// }
/// </code>
/// </example>
public sealed class GrpcClientOptions : CommunicationClientOptions
{
    /// <summary>
    /// Gets or sets the service's address: <c>http://inventory</c>, or <c>http://_grpc.inventory</c> for the service's
    /// endpoint named <c>grpc</c>, resolved through service discovery. Required; <c>http</c> or <c>https</c>.
    /// </summary>
    public Uri? Address { get; set; }

    /// <summary>
    /// Gets or sets the deadline of a call that sets none of its own, retries included. The service sees it and can stop
    /// working on a call nobody waits for any more. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the retry policy, carried out by the gRPC client itself.</summary>
    public GrpcRetryOptions Retry { get; set; } = new();

    /// <summary>Gets or sets the HTTP/2 keepalive pings, which find a dead connection before a call waits on it.</summary>
    public GrpcKeepAliveOptions KeepAlive { get; set; } = new();

    /// <summary>Gets or sets the largest message the client accepts, in bytes. gRPC's 4 MB when <see langword="null"/>.</summary>
    public int? MaxReceiveMessageSize { get; set; }

    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidateAddress(Address, nameof(Address)) is { } address)
        {
            yield return address;
        }
        else if (Address!.Scheme is not ("http" or "https"))
        {
            yield return new ValidationResult($"Address '{Address}' must be an http or https address.", [nameof(Address)]);
        }

        if (Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromHours(1))
        {
            yield return new ValidationResult("Deadline must be positive and at most 1 hour.", [nameof(Deadline)]);
        }

        if (Retry.MaxAttempts is < 1 or > 5)
        {
            yield return new ValidationResult("Retry:MaxAttempts must be between 1 (no retries) and 5, gRPC's limit.", [nameof(Retry)]);
        }

        if (Retry.InitialBackoff <= TimeSpan.Zero || Retry.MaxBackoff < Retry.InitialBackoff || Retry.BackoffMultiplier <= 0)
        {
            yield return new ValidationResult(
                "Retry:InitialBackoff must be positive, Retry:MaxBackoff at least InitialBackoff, and Retry:BackoffMultiplier positive.",
                [nameof(Retry)]);
        }

        if (Retry.RetryableStatusCodes is { } codes && codes.Any(c => c is StatusCode.OK || !Enum.IsDefined(c)))
        {
            yield return new ValidationResult("Retry:RetryableStatusCodes must be defined, non-OK status codes.", [nameof(Retry)]);
        }

        if (KeepAlive.PingTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult("KeepAlive:PingTimeout must be positive.", [nameof(KeepAlive)]);
        }

        if (MaxReceiveMessageSize is <= 0)
        {
            yield return new ValidationResult("MaxReceiveMessageSize must be positive.", [nameof(MaxReceiveMessageSize)]);
        }

        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }
    }
}

/// <summary>
/// The gRPC retry policy (gRFC A6), carried out by the client: a call is retried only while the service has not
/// answered it — no response headers yet — so a retry does not repeat work the service reported doing.
/// </summary>
public sealed class GrpcRetryOptions
{
    /// <summary>Gets or sets the attempts, the first included; <c>1</c> turns retries off. Defaults to 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the upper bound of the first retry's randomized delay. Defaults to 500 ms.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the largest delay between attempts. Defaults to 5 seconds.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the factor each next delay grows by. Defaults to 1.5.</summary>
    public double BackoffMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets the status codes that are retried. <c>Unavailable</c> alone when <see langword="null"/>: the one
    /// status that says the call did not run.
    /// </summary>
    public IList<StatusCode>? RetryableStatusCodes { get; set; }
}

/// <summary>HTTP/2 keepalive pings on the client's connections.</summary>
public sealed class GrpcKeepAliveOptions
{
    /// <summary>
    /// Gets or sets how long a connection may be idle before it is pinged; <see cref="Timeout.InfiniteTimeSpan"/> turns
    /// pings off. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan PingDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how long a ping may go unanswered before the connection is dropped. Defaults to 30 seconds.</summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
