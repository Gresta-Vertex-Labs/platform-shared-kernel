using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Accepts an optional <c>If-Match</c> request header: a request without it is unconditional, and one with it names the
/// version it changes. Works on MVC controllers and actions and on minimal-API handlers (<c>[AcceptIfMatch]</c> on the
/// lambda); the <c>AcceptIfMatch()</c> convention and a nullable <see cref="IfMatch{TVersion}"/> parameter do the same.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint metadata only: <c>UseSharedKernelWebApi()</c> checks the header after authorization and before the endpoint
/// runs, identically for every kind of endpoint, so nothing else needs registering. A header the request sends is held
/// to the rules of <see cref="RequireIfMatchAttribute"/> (RFC 9110 section 13.1.1):
/// </para>
/// <list type="bullet">
///   <item>Missing: the request goes on, unconditional.</item>
///   <item>Malformed, more than one entity tag, or <c>*</c>: 400, <c>precondition.invalid</c>. <c>*</c> asks for any
///   current version, which an endpoint that compares one version does not check; read as missing, it would let a write
///   meant to replace a resource create it.</item>
///   <item>A weak entity tag (<c>W/"42"</c>): 412 Precondition Failed, <c>precondition.failed</c> — <c>If-Match</c>
///   compares strongly, so a weak tag never matches.</item>
/// </list>
/// <para>
/// A header the endpoint cannot use is refused, never ignored: ignoring it would turn the client's conditional request
/// into an unconditional one. Read the version with <c>HttpContext.GetIfMatch()</c>, which is <see langword="null"/>
/// only when the request sent no <c>If-Match</c>, or declare a nullable <see cref="IfMatch{TVersion}"/> parameter. On
/// an endpoint that also requires the header (<see cref="RequireIfMatchAttribute"/>), the requirement wins.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AcceptIfMatchAttribute : Attribute, IIfMatchAcceptedMetadata;
