using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>Errors shared by the test endpoints.</summary>
public static class TestErrors
{
    /// <summary>The code 06.Persistence gives every stale row version (<c>ConcurrencyVersion.ConflictErrorCode</c>).</summary>
    public const string ConcurrencyConflictCode = "persistence.concurrency_conflict";

    public static readonly Error OrderNotFound = Error.NotFound("order.not_found", "Order 42 was not found.");

    /// <summary>A conflict that is not about versions: stays 409 whatever the request's headers.</summary>
    public static readonly Error VersionConflict = Error.Conflict("order.version_conflict", "Order 42 was changed by someone else.");

    /// <summary>The persistence layer's stale-version conflict: 412 on a conditional request.</summary>
    public static readonly Error StaleVersion = Error.Conflict(ConcurrencyConflictCode, "Order 42 was changed by someone else.");

    public static readonly Error SearchUnreachable =
        Error.Unavailable("search.unreachable", "Search engine at http://search.internal:7700 did not answer.");
}

/// <summary>An API controller (<c>[ApiController]</c>) returning the same typed results as minimal APIs.</summary>
[ApiController]
[Route("mvc-api")]
public sealed class ApiTestController : ControllerBase
{
    [HttpGet("ok")]
    public Results<Ok<string>, ErrorHttpResult> Ok_() => Result<string>.Success("value").ToOk();

    [HttpGet("failure")]
    public Results<Ok<string>, ErrorHttpResult> Failure() => Result<string>.Failure(TestErrors.OrderNotFound).ToOk();

    [HttpDelete("done")]
    public Results<NoContent, ErrorHttpResult> Done() => Result.Success().ToNoContent();

    [HttpDelete("failure")]
    public Results<NoContent, ErrorHttpResult> DeleteFailure() => Result.Failure(TestErrors.OrderNotFound).ToNoContent();

    [HttpPost("created")]
    public Results<Created<string>, ErrorHttpResult> Created_() =>
        Result<string>.Success("7").ToCreated(id => $"/mvc-api/orders/{id}");

    [HttpGet("etag")]
    public Task<Results<OkWithETag<string>, ErrorHttpResult>> ETag() =>
        Task.FromResult(Result<string>.Success("value")).ToOkWithETag(_ => "5");

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
    public Results<NoContent, ErrorHttpResult> Versioned() =>
        HttpContext.GetIfMatch() == TestVersions.Current ? Result.Success().ToNoContent() : Result.Failure(TestErrors.StaleVersion).ToNoContent();

    [HttpPut("versioned-throw")]
    [RequireIfMatch]
    public IActionResult VersionedThrow() => throw new ConflictException(TestErrors.StaleVersion);

    [HttpPost("customers")]
    public Results<Ok<string>, ErrorHttpResult> CreateCustomer([FromBody] CustomerRequest request) =>
        Result<string>.Success(request.Name!).ToOk();
}

/// <summary>A controller without <c>[ApiController]</c>, for which MVC's problem writer writes nothing.</summary>
[Route("mvc-plain")]
public sealed class PlainTestController : Controller
{
    [HttpGet("failure")]
    public Results<NoContent, ErrorHttpResult> Failure() => Result.Failure(TestErrors.OrderNotFound).ToNoContent();
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

/// <summary>A body validated by data annotations, for the model-state response.</summary>
public sealed class CustomerRequest
{
    [Required]
    public string? Name { get; set; }

    [Range(18, 130)]
    public int Age { get; set; }
}
