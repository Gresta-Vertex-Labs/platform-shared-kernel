
namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Accepts an optional <c>Idempotency-Key</c> request header: a client may send a key so that a retry is recognized, or
/// send none. Works on MVC controllers and actions and on minimal-API handlers (<c>[AcceptIdempotencyKey]</c> on the
/// lambda); the <c>AcceptIdempotencyKey()</c> convention and a nullable <see cref="IdempotencyKey"/> parameter do the
/// same.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint metadata only: <c>UseSharedKernelWebApi()</c> checks the header after authorization and before the endpoint
/// runs, identically for every kind of endpoint, so nothing else needs registering. A request without the header goes
/// on; a key that is not 1 to 256 visible ASCII characters (after removing one pair of surrounding double quotes) is
/// answered 400 <c>idempotency.key_invalid</c>. It is never read as missing: that would run a retry twice.
/// </para>
/// <para>
/// Read the key with <c>HttpContext.GetIdempotencyKey()</c>, which is <see langword="null"/> only when the request sent
/// none, or declare a nullable <see cref="IdempotencyKey"/> parameter. On an endpoint that also requires the header
/// (<see cref="RequireIdempotencyKeyAttribute"/>), the requirement wins.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AcceptIdempotencyKeyAttribute : Attribute, IIdempotencyKeyAcceptedMetadata;
