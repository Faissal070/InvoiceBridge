using System.Globalization;
using OrderSync.Models;

namespace OrderSync.Components;

/// <summary>
/// Dutch formatting shared by the pages.
/// </summary>
public static class Display
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    // Times are shown in Dutch time, also when the server runs in another time zone.
    private static readonly TimeZoneInfo DutchTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string Amount(decimal? amount) => amount?.ToString("C", Dutch) ?? "–";

    public static string Date(DateOnly? date) => date?.ToString("d MMMM yyyy", Dutch) ?? "–";

    public static string DateAndTime(DateTimeOffset? moment) =>
        moment is { } m ? ToDutchTime(m).ToString("d MMM yyyy 'om' HH:mm", Dutch) : "–";

    /// <summary>"zojuist", "5 min geleden", or a date for older moments.</summary>
    public static string Relative(DateTimeOffset? moment, DateTimeOffset now)
    {
        if (moment is not { } m)
        {
            return "–";
        }

        var ago = now - m;
        return ago switch
        {
            { TotalMinutes: < 1 } => "zojuist",
            { TotalMinutes: < 60 } => $"{(int)ago.TotalMinutes} min geleden",
            { TotalHours: < 24 } when ToDutchTime(m).Date == ToDutchTime(now).Date => $"vandaag {ToDutchTime(m):HH:mm}",
            _ => ToDutchTime(m).ToString("d MMM HH:mm", Dutch)
        };
    }

    /// <summary>"2 uur en 15 minuten", for the time-saved estimate.</summary>
    public static string Duration(int minutes) => minutes switch
    {
        < 60 => $"{minutes} minuten",
        _ when minutes % 60 == 0 => $"{minutes / 60} uur",
        _ => $"{minutes / 60} uur en {minutes % 60} min"
    };

    public static string Label(SyncStatus status) => status switch
    {
        SyncStatus.Pending => "Wachtend",
        SyncStatus.Processing => "Bezig",
        SyncStatus.Synced => "Gefactureerd",
        SyncStatus.Failed => "Mislukt",
        _ => status.ToString()
    };

    public static string Tone(SyncStatus status) => status switch
    {
        SyncStatus.Synced => "success",
        SyncStatus.Processing => "info",
        SyncStatus.Failed => "danger",
        _ => "warning"
    };

    private static DateTimeOffset ToDutchTime(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, DutchTime);
}
