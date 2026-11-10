using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Settings;

public static class SettingsErrors
{
    public static readonly Error ChangedMeanwhile =
        Error.Conflict("Settings.ChangedMeanwhile", "The work schedule changed at the same moment. Read it again.");
}
