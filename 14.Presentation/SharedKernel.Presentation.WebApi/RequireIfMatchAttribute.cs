using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Requires an <c>If-Match</c> request header naming the version the request changes, for optimistic concurrency
/// over HTTP. Works on MVC controllers and actions and on minimal-API handlers (<c>[RequireIfMatch]</c> on the
/// lambda); the <c>RequireIfMatch()</c> convention and an <see cref="IfMatch{TVersion}"/> parameter do the same.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint metadata only: <c>UseSharedKernelWebApi()</c> checks the header after authorization and before the
/// endpoint runs, identically for every kind of endpoint, so nothing else needs registering. The rules
/// (RFC 9110 section 13.1.1):
/// </para>
/// <list type="bullet">
///   <item>Missing, or <c>*</c>: 428 Precondition Required, <c>precondition.required</c> — the request must name a version.</item>
///   <item>Malformed, or more than one entity tag: 400, <c>precondition.invalid</c>.</item>
///   <item>A weak entity tag (<c>W/"42"</c>): 412 Precondition Failed, <c>precondition.failed</c> — <c>If-Match</c>
///   compares strongly, so a weak tag never matches.</item>
/// </list>
/// <para>
/// Read the version with <c>HttpContext.GetIfMatch()</c> (or declare an <see cref="IfMatch{TVersion}"/> parameter)
/// and pass it to the update. When the update then fails because the version is no longer current — a
/// <c>persistence.concurrency_conflict</c>, returned or thrown — the answer is 412 with that code.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireIfMatchAttribute : Attribute, IIfMatchRequiredMetadata;
