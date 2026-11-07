namespace Kanabol.Api.Realtime;

/// <summary>The SignalR groups of the board hub (SPEC section 9).</summary>
public static class BoardGroups
{
    /// <summary>Whoever has the project's board open, and may see it.</summary>
    public static string Project(Guid projectId) => $"project:{projectId}";

    /// <summary>Every connection of one user, which joins it by itself.</summary>
    public static string User(Guid userId) => $"user:{userId}";
}
