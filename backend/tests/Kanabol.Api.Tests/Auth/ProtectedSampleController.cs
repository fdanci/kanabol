using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Tests.Auth;

/// <summary>Only exists in the tests. It has no attributes, so it shows what any new endpoint gets by default.</summary>
[ApiController]
[Route("api/test-protected")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only finds instance methods.")]
public sealed class ProtectedSampleController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
