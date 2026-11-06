namespace Kanabol.Api.Controllers;

/// <summary>The column goes at the right end of the board.</summary>
public sealed record AddBoardColumnRequest(string Name, bool IsDone);
