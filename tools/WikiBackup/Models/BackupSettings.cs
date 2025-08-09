namespace WikiBackup.Models;

/// <summary>
/// Configuration settings for the wiki backup application
/// </summary>
public class BackupSettings
{
    /// <summary>
    /// MediaWiki API endpoint URL
    /// </summary>
    public string ApiUrl { get; set; } = "https://wiki.openstreetmap.org/w/api.php";
    
    /// <summary>
    /// Base URL of the wiki (for generating links)
    /// </summary>
    public string WikiBaseUrl { get; set; } = "https://wiki.openstreetmap.org/wiki";
    
    /// <summary>
    /// User agent string for HTTP requests
    /// </summary>
    public string UserAgent { get; set; } = "WikiBackup/1.0 (French Railway Signalling Project)";
    
    /// <summary>
    /// Maximum number of concurrent HTTP requests
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 5;
    
    /// <summary>
    /// Base directory for storing backup files
    /// </summary>
    public string BackupDirectory { get; set; } = "wiki/backup";

    /// <summary>
    /// Normalizes the WikiBaseUrl by removing trailing slash in-place
    /// Should be called once during initialization
    /// </summary>
    public void NormalizeWikiBaseUrl()
    {
        WikiBaseUrl = WikiBaseUrl.TrimEnd('/');
    }
}