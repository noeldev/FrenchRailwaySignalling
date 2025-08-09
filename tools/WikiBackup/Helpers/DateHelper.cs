namespace WikiBackup.Helpers;

/// <summary>
/// Helper class for consistent date formatting across the application
/// </summary>
public static class DateHelper
{
    private const string DefaultDateFormat = "yyyy-MM-dd HH:mm:ss UTC";

    /// <summary>
    /// Gets the current UTC time formatted consistently for backup purposes
    /// </summary>
    /// <returns>Current UTC time in DefaultDateFormat</returns>
    public static string GetFormattedUtcNow()
    {
        return DateTime.UtcNow.ToString(DefaultDateFormat);
    }

    /// <summary>
    /// Formats a timestamp string consistently
    /// </summary>
    /// <param name="timestamp">The timestamp string to format</param>
    /// <returns>Formatted timestamp or original string if parsing fails</returns>
    public static string FormatTimestamp(string timestamp)
    {
        return DateTime.TryParse(timestamp, out var parsedTimestamp)
            ? parsedTimestamp.ToString(DefaultDateFormat)
            : timestamp;
    }

    /// <summary>
    /// Formats a DateTime object consistently
    /// </summary>
    /// <param name="dateTime">The DateTime to format</param>
    /// <returns>Formatted DateTime string</returns>
    public static string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString(DefaultDateFormat);
    }
}