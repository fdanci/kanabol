namespace Kanabol.Api.Realtime;

/// <summary>One message of the board hub for a group, waiting in BoardOutbox.</summary>
public sealed record BoardMessage(string Group, string EventName, object Payload);
