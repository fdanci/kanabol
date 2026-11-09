using Kanabol.Application.Common;

namespace Kanabol.Api.Realtime;

/// <summary>What the board hub answers itself, before asking anyone (SPEC section 9).</summary>
public static class HubErrors
{
    public static readonly Error TooManyProjects =
        Error.Forbidden("Hub.TooManyProjects", "A connection hears at most 8 projects. Leave one first.");
}
