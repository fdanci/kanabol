using System.ComponentModel.DataAnnotations;

namespace Kanabol.Application.Features.Settings;

/// <summary>
/// The time zone the work schedule's hours are in (SPEC section 4.4, "Setările generale"): Schedule:TimeZone,
/// Europe/Bucharest unless set, Schedule__TimeZone=Europe/Vienna in compose. A zone the system doesn't know stops
/// the API at start, so the image has to carry the time zone data.
/// </summary>
public sealed class ScheduleOptions
{
    public const string SectionName = "Schedule";

    public const string DefaultTimeZone = "Europe/Bucharest";

    [Required]
    public string TimeZone { get; set; } = DefaultTimeZone;

    // A method, not a property: the options' validation reads every property, before the zone is known to exist.
    public TimeZoneInfo Zone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public static bool IsAKnownZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
