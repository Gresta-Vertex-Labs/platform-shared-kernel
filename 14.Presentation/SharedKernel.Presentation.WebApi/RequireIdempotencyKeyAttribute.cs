using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Requires an <c>Idempotency-Key</c> request header. Works on MVC controllers and actions and on minimal-API
/// handlers (<c>[RequireIdempotencyKey]</c> on the lambda); the <c>RequireIdempotencyKey()</c> convention and an
/// <see cref="IdempotencyKey"/> parameter do the same.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint metadata only: <c>UseSharedKernelWebApi()</c> checks the header after authorization and before the
/// endpoint runs, identically for every kind of endpoint, so nothing else needs registering. A request without the
/// header is answered 400 <c>idempotency.key_required</c>; a key that is not 1 to 256 visible ASCII characters (after
/// removing one pair of surrounding double quotes) is answered 400 <c>idempotency.key_invalid</c>.
/// </para>
/// <para>
/// This checks the header only. Read the key with <c>HttpContext.GetIdempotencyKey()</c> (or declare an
/// <see cref="IdempotencyKey"/> parameter) and pass it to the command (<c>IIdempotentRequest.IdempotencyKey</c>),
/// whose pipeline reserves it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IIdempotencyKeyRequiredMetadata;
