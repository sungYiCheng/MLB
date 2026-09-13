using System.Globalization;

namespace MlbAi.Application.Services;

public static class MlbDisplayFormatter
{
    public static string FormatRecord(int wins, int losses)
    {
        return $"{wins}-{losses}";
    }

    public static string? FormatGameType(string? gameType)
    {
        return gameType switch
        {
            "R" => "Regular Season",
            "S" => "Spring Training",
            "F" => "Wild Card",
            "D" => "Division Series",
            "L" => "League Championship",
            "W" => "World Series",
            _ => gameType
        };
    }

    public static string? FormatDoubleHeader(string? doubleHeader)
    {
        return doubleHeader switch
        {
            "N" => "No",
            "Y" => "Yes",
            "S" => "Split",
            _ => doubleHeader
        };
    }

    public static string? FormatTitleCase(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    public static string Pluralize(int count)
    {
        return count == 1 ? string.Empty : "s";
    }
}
