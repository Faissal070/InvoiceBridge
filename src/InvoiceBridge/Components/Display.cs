using System.Globalization;
using InvoiceBridge.Models;

namespace InvoiceBridge.Components;

/// <summary>
/// Formatting helpers shared by the pages. Amounts use Dutch/euro style regardless of UI language;
/// time-relative strings switch based on CultureInfo.CurrentUICulture.
/// </summary>
public static class Display
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    // Times are shown in Dutch time, also when the server runs in another time zone.
    private static readonly TimeZoneInfo DutchTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public static string Amount(decimal? amount) => amount?.ToString("C", Dutch) ?? "–";

    public static string Date(DateOnly? date) => date?.ToString("d MMMM yyyy", Dutch) ?? "–";

    public static string DateAndTime(DateTimeOffset? moment) =>
        moment is { } m
            ? ToDutchTime(m).ToString(IsEnglish ? "d MMM yyyy 'at' HH:mm" : "d MMMM yyyy 'om' HH:mm", Dutch)
            : "–";

    /// <summary>"just now" / "zojuist", "5 min ago" / "5 min geleden", or a date for older moments.</summary>
    public static string Relative(DateTimeOffset? moment, DateTimeOffset now)
    {
        if (moment is not { } m)
        {
            return "–";
        }

        var ago = now - m;
        return IsEnglish
            ? ago switch
            {
                { TotalMinutes: < 1 } => "just now",
                { TotalMinutes: < 60 } => $"{(int)ago.TotalMinutes} min ago",
                { TotalHours: < 24 } when ToDutchTime(m).Date == ToDutchTime(now).Date => $"today {ToDutchTime(m):HH:mm}",
                _ => ToDutchTime(m).ToString("d MMM HH:mm", Dutch)
            }
            : ago switch
            {
                { TotalMinutes: < 1 } => "zojuist",
                { TotalMinutes: < 60 } => $"{(int)ago.TotalMinutes} min geleden",
                { TotalHours: < 24 } when ToDutchTime(m).Date == ToDutchTime(now).Date => $"vandaag {ToDutchTime(m):HH:mm}",
                _ => ToDutchTime(m).ToString("d MMM HH:mm", Dutch)
            };
    }

    /// <summary>"2 hours 15 min" / "2 uur en 15 min", for the time-saved estimate.</summary>
    public static string Duration(int minutes) => IsEnglish
        ? minutes switch
        {
            < 60 => $"{minutes} minutes",
            _ when minutes % 60 == 0 => $"{minutes / 60} {(minutes / 60 == 1 ? "hour" : "hours")}",
            _ => $"{minutes / 60} h {minutes % 60} min"
        }
        : minutes switch
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
        SyncStatus.Simulated => "Gesimuleerd",
        _ => status.ToString()
    };

    public static string Tone(SyncStatus status) => status switch
    {
        SyncStatus.Synced => "success",
        SyncStatus.Processing => "info",
        SyncStatus.Failed => "danger",
        SyncStatus.Simulated => "secondary",
        _ => "warning"
    };

    private static DateTimeOffset ToDutchTime(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, DutchTime);
}
