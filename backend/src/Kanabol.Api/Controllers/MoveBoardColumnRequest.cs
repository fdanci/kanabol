namespace Kanabol.Api.Controllers;

/// <summary>From 0 on the left; past the end, the column goes last.</summary>
public sealed record MoveBoardColumnRequest(int Position);
