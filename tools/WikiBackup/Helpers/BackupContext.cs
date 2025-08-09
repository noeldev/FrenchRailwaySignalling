using WikiBackup.Models;

namespace WikiBackup.Helpers;

/// <summary>
/// Context object that encapsulates all backup operation dependencies and settings
/// Reduces parameter passing and centralizes configuration
/// </summary>
/// <param name="settings">Backup configuration settings</param>
/// <param name="backupDirectory">Target directory for backup files</param>
/// <param name="category">Backup category name</param>
public class BackupContext(BackupSettings settings, string backupDirectory, string category) : IDisposable
{
    public BackupSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));
    public string BackupDirectory { get; } = backupDirectory ?? throw new ArgumentNullException(nameof(backupDirectory));
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));
    public WikiClient WikiClient { get; } = new(settings);

    private bool _disposed = false;

    /// <summary>
    /// Creates the backup directory if it doesn't exist
    /// </summary>
    public void EnsureBackupDirectoryExists()
    {
        Directory.CreateDirectory(BackupDirectory);
    }

    /// <summary>
    /// Disposes managed resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method following the dispose pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            WikiClient?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Results of a backup operation with detailed information
/// </summary>
public record BackupResults
{
    public Dictionary<string, PageBackupInfo> PagesInfo { get; init; } = new();
    public List<string> SavedFiles { get; init; } = [];
    
    public int SuccessCount => SavedFiles.Count;
    public int FailedCount => PagesInfo.Count - SuccessCount;
    public int TotalCount => PagesInfo.Count;
}

/// <summary>
/// Information about a single page backup operation
/// </summary>
public record PageBackupInfo
{
    public string Title { get; init; } = "";
    public string? Timestamp { get; init; }
    public string? Comment { get; init; }
    public string? User { get; init; }
    public bool Success { get; set; }
    public string? Filepath { get; set; }
}