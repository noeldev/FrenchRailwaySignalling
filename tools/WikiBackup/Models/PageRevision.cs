using System.Text.Json;

namespace WikiBackup.Models;

/// <summary>
/// Represents a revision of a wiki page with metadata
/// </summary>
/// <param name="Content">The raw wikitext content of the page</param>
/// <param name="Timestamp">ISO timestamp of when this revision was created</param>
/// <param name="Comment">Edit summary/comment for this revision</param>
/// <param name="User">Username of the person who made this edit</param>
public record PageRevision(string Content, string Timestamp, string Comment, string User)
{
    private const string DefaultComment = "No edit comment";
    private const string DefaultUser = "Unknown user";

    /// <summary>
    /// Creates a PageRevision from a MediaWiki API JSON revision element
    /// </summary>
    /// <param name="revision">JSON element containing revision data</param>
    /// <returns>New PageRevision instance</returns>
    public static PageRevision Create(JsonElement revision)
    {
        var content = revision.GetProperty("slots").GetProperty("main").GetProperty("*").GetString() ?? "";
        var timestamp = revision.GetProperty("timestamp").GetString() ?? "";
        var comment = GetJsonStringOrDefault(revision, "comment", DefaultComment);
        var user = GetJsonStringOrDefault(revision, "user", DefaultUser);
        
        return new PageRevision(content, timestamp, comment, user);
    }
    
    /// <summary>
    /// Helper method to safely extract string property from JSON element
    /// </summary>
    private static string GetJsonStringOrDefault(JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var prop) 
            ? prop.GetString() ?? defaultValue 
            : defaultValue;
    }
}