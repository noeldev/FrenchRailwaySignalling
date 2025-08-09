using System.Diagnostics.Metrics;
using System.Text.Json;
using WikiBackup.Helpers;
using WikiBackup.Models;

namespace WikiBackup;

/// <summary>
/// Handles communication with the MediaWiki API
/// </summary>
public class WikiClient : IDisposable
{
    // Known MediaWiki namespaces that should be treated as directory separators
    private static readonly HashSet<string> KnownNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "Template", "Module", "Tag", "Key", "Category", "File", "Help", "User"
    };

    // ISO 3166-1 alpha-2 country codes that might be used as namespaces
    private static readonly HashSet<string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FR" // Add more country codes as needed
    };

    private readonly HttpClient _httpClient;
    private readonly BackupSettings _settings;

    public WikiClient(BackupSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _settings.UserAgent);
    }

    /// <summary>
    /// Fetches the latest revision of a wiki page
    /// </summary>
    public async Task<PageRevision?> GetPageContentAsync(string pageTitle)
    {
        try
        {
            Console.WriteLine($"Fetching: {pageTitle}");

            var queryParams = new Dictionary<string, string>
            {
                ["action"] = "query",
                ["format"] = "json",
                ["titles"] = pageTitle,
                ["prop"] = "revisions",
                ["rvprop"] = "content|timestamp|comment|user",
                ["rvslots"] = "main"
            };

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var requestUrl = $"{_settings.ApiUrl}?{queryString}";

            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            var pages = apiResponse.GetProperty("query").GetProperty("pages");

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Name == "-1")
                {
                    Console.WriteLine($"❌ Page not found: {pageTitle}");
                    return null;
                }

                if (!page.Value.TryGetProperty("revisions", out var revisions))
                {
                    Console.WriteLine($"❌ No revisions found for: {pageTitle}");
                    return null;
                }

                var pageRevision = PageRevision.Create(revisions[0]);

                Console.WriteLine($"✅ Found page: {pageTitle} (last edited: {pageRevision.Timestamp} by {pageRevision.User})");
                return pageRevision;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error fetching {pageTitle}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves a wiki page to a file with metadata header in organized directory structure
    /// </summary>
    public static async Task<string?> SavePageToFileAsync(string pageTitle, PageRevision revision, BackupContext context)
    {
        try
        {
            // Create organized path based on page namespace and structure
            var organizedPath = CreateOrganizedPath(pageTitle, context.BackupDirectory);
            var directory = Path.GetDirectoryName(organizedPath)!;
            Directory.CreateDirectory(directory);

            // Create metadata header
            var header = CreateMetadataHeader(pageTitle, revision, context.Settings.WikiBaseUrl);

            // Write file with UTF-8 encoding
            await File.WriteAllTextAsync(organizedPath, header + revision.Content, System.Text.Encoding.UTF8);

            Console.WriteLine($"💾 Saved: {organizedPath}");
            return organizedPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error saving {pageTitle}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates an organized directory structure and filename based on the page title
    /// Uses known namespaces to determine directory structure
    /// Examples:
    /// - "Template:FR:RailwaySignalState/doc" -> "Template/FR/RailwaySignalState/doc.wiki"
    /// - "FR:Key:railway:signal:distant:states" -> "FR/Key/railway-signal-distant-states.wiki"
    /// - "FR:OpenRailwayMap/Tagging_in_France" -> "FR/OpenRailwayMap/Tagging_in_France.wiki"
    /// </summary>
    private static string CreateOrganizedPath(string pageTitle, string backupDir)
    {
        var parts = pageTitle.Split(':');
        var pathComponents = new List<string> { backupDir };
        
        // Process each colon-separated part
        int namespaceCount = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];

            // Check if it's a known namespace or country code
            if (KnownNamespaces.Contains(part) ||
                CountryCodes.Contains(part))
            {
                pathComponents.Add(parts[i]);
                namespaceCount++;
            }
            else
            {
                break; // Stop at first non-namespace part
            }
        }
        
        // Everything after namespaces becomes the content path
        var contentPath = string.Join(":", parts.Skip(namespaceCount));
        
        // Handle slashes in content path
        var contentParts = contentPath.Split('/');
        var directories = contentParts.Take(contentParts.Length - 1).Select(CreateSafeName);
        var filename = $"{CreateSafeName(contentParts.Last())}.wiki";
        
        pathComponents.AddRange(directories);
        pathComponents.Add(filename);
        
        return Path.Combine([.. pathComponents]);
    }

    /// <summary>
    /// Creates a safe name from a wiki page component for filesystem use
    /// </summary>
    private static string CreateSafeName(string name)
    {
        var safe = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            safe[i] = c switch
            {
                ':' => '-', // Replace colon with dash for readability
                '*' or '?' or '<' or '>' or '|' or '"' => '_',
                _ => c
            };
        }
        return new string(safe);
    }


    /// <summary>
    /// Creates the metadata header for saved files
    /// </summary>
    private static string CreateMetadataHeader(string pageTitle, PageRevision revision, string wikiBaseUrl)
    {
        var wikiUrl = $"{wikiBaseUrl}/{pageTitle.Replace(' ', '_')}";
        var formattedTimestamp = DateHelper.FormatTimestamp(revision.Timestamp);
        var backupDate = DateHelper.GetFormattedUtcNow();

        return $@"{{{{!--
Wiki page: {pageTitle}
Last edited: {formattedTimestamp}
Last editor: {revision.User}
Last edit comment: {revision.Comment}
Backup date: {backupDate}
Source URL: {wikiUrl}
--}}}}

";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}