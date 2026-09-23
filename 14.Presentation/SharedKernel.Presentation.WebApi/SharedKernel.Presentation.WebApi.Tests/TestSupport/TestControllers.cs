using Microsoft.AspNetCore.Mvc;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>Errors shared by the test endpoints.</summary>
public static class TestErrors
{
    public static readonly Error OrderNotFound = Error.NotFound("order.not_found", "Order 42 was not found.");

    public static readonly Error VersionConflict = Error.Conflict("order.version_conflict", "Order 42 was changed by someone else.");

    public static readonly Error SearchUnreachable =
        Error.Unavailable("search.unreachable", "Search engine at http://search.internal:7700 did not answer.");
}

/// <summary>An API controller (<c>[ApiController]</c>) exercising the MVC mapping.</summary>
[ApiController]
[Route("mvc-api")]
public sealed class ApiTestController : ControllerBase
{
    [HttpGet("ok")]
    public ActionResult<string> Ok_() => Result<string>.Success("value").ToActionResult();

    [HttpGet("failure")]
    public ActionResult<string> Failure() => Result<string>.Failure(TestErrors.OrderNotFound).ToActionResult();

    [HttpDelete("done")]
    public IActionResult Done() => Result.Success().ToActionResult();

    [HttpDelete("failure")]
    public IActionResult DeleteFailure() => Result.Failure(TestErrors.OrderNotFound).ToActionResult();

    [HttpPost("created")]
    public IActionResult Created_() =>
        Result<string>.Success("7").ToActionResult(id => CreatedAtAction(nameof(Ok_), new { id }, id));

    [HttpGet("not-found")]
    public IActionResult FrameworkNotFound() => NotFound();

    [HttpGet("throw")]
    public IActionResult Throw() => throw new NotFoundException(TestErrors.OrderNotFound);

    [HttpGet("auth/perm")]
    [RequirePermission("orders.read", "orders.admin")]
    public string Permission() => "ok";

    [HttpGet("auth/perm-and-role")]
    [RequirePermission("orders.read")]
    [RequireRole("auditor")]
    public string PermissionAndRole() => "ok";

    [HttpGet("auth/fresh")]
    [RequireFreshAuthentication(300)]
    public string Fresh() => "ok";

    [HttpGet("auth/mfa")]
    [RequireAuthenticationMethod("mfa", "hwk")]
    public string Mfa() => "ok";

    [HttpPost("idempotent")]
    [RequireIdempotencyKey]
    public string Idempotent() => HttpContext.GetIdempotencyKey() ?? "none";

    [HttpPut("versioned")]
    [RequireIfMatch]
    public IActionResult Versioned() =>
        HttpContext.GetIfMatch() == "1"
            ? Result.Success().ToActionResult()
            : Result.Failure(TestErrors.VersionConflict).ToActionResult();

    [HttpPut("versioned-throw")]
    [RequireIfMatch]
    public IActionResult VersionedThrow() => throw new ConflictException(TestErrors.VersionConflict);
}

/// <summary>A controller without <c>[ApiController]</c>, for which MVC's problem writer writes nothing.</summary>
[Route("mvc-plain")]
public sealed class PlainTestController : Controller
{
    [HttpGet("failure")]
    public IActionResult Failure() => Result.Failure(TestErrors.OrderNotFound).ToActionResult();
}

/// <summary>Class-level and action-level requirements, which must both hold.</summary>
[ApiController]
[Route("mvc-admin")]
[RequireRole("admin")]
public sealed class AdminTestController : ControllerBase
{
    [HttpGet("report")]
    [RequirePermission("reports.read")]
    public string Report() => "ok";
}
