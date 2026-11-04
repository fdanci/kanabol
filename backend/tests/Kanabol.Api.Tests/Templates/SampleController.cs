using System.Diagnostics.CodeAnalysis;
using Kanabol.Api.Errors;
using Kanabol.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Tests.Templates;

/// <summary>Only exists in the tests, to reach the error paths until the first real endpoints do.</summary>
[ApiController]
[Route("api/test-samples")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only finds instance methods.")]
public sealed class SampleController : ControllerBase
{
    [HttpGet("missing")]
    public IActionResult Missing() => Result.Failure(Error.NotFound("Sample.NotFound", "No such thing.")).ToActionResult();

    [HttpPost("invalid")]
    public IActionResult Invalid() =>
        Result.Failure(Error.Validation(new Dictionary<string, string[]> { ["UserName"] = ["Required."] })).ToActionResult();

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret detail");
}
