using Microsoft.Extensions.Configuration;
using System.Text;
using WikiBackup.Helpers;
using WikiBackup.Models;
using WikiBackup.Services;

namespace WikiBackup;

/// <summary>
/// Main program for backing up OSM Wiki pages
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("🚀 Starting OSM Wiki backup...");

        try
        {
            // Load configuration
            var configuration = LoadConfiguration();
            var settings = GetBackupSettings(configuration);
            if (settings == null) return 1;

            // Initialize backup context
            var backupDir = GetRepositoryBackupPath(settings.BackupDirectory);
            var category = GetCategoryFromArgs(args);
            
            using var context = new BackupContext(settings, backupDir, category);
            context.EnsureBackupDirectoryExists();

            Console.WriteLine($""""
                📂 Backup category: {category}
                📁 Target directory: {backupDir}
                """");

            // Get pages to backup
            var pagesToBackup = GetPagesToBackup(category, configuration);
            if (pagesToBackup == null) return 1;

            // Execute backup
            var backupResults = await ExecuteBackupAsync(pagesToBackup, context);
            await CreateIndexFileAsync(backupResults, context);
            
            DisplayBackupSummary(backupResults);
            
            return backupResults.SuccessCount == pagesToBackup.Count ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Fatal error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Loads application configuration from appsettings.json
    /// </summary>
    private static IConfiguration LoadConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    /// <summary>
    /// Extracts backup settings from configuration
    /// </summary>
    private static BackupSettings? GetBackupSettings(IConfiguration configuration)
    {
        var settings = configuration.GetSection("BackupSettings").Get<BackupSettings>();
        if (settings == null)
        {
            Console.WriteLine("💥 Fatal error: BackupSettings section not found in appsettings.json");
            return null;
        }
        
        // Normalize WikiBaseUrl once
        settings.NormalizeWikiBaseUrl();
        
        return settings;
    }

    /// <summary>
    /// Finds the repository root and returns the backup path
    /// </summary>
    private static string GetRepositoryBackupPath(string backupDirName)
    {
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            if (Directory.Exists(Path.Combine(currentDir.FullName, ".git")))
            {
                return Path.Combine(currentDir.FullName, backupDirName);
            }
            currentDir = currentDir.Parent;
        }

        Console.WriteLine("⚠️ Warning: Could not find repository root (.git folder), using current directory");
        return Path.Combine(Directory.GetCurrentDirectory(), backupDirName);
    }

    /// <summary>
    /// Gets the backup category from command line arguments
    /// </summary>
    private static string GetCategoryFromArgs(string[] args)
    {
        return args.Length > 0 ? args[0] : "main";
    }

    /// <summary>
    /// Gets the list of pages to backup based on category
    /// </summary>
    private static List<string>? GetPagesToBackup(string category, IConfiguration configuration)
    {
        try
        {
            var pageProvider = new PageProvider(configuration);
            var pages = pageProvider.GetPagesByCategory(category);
            Console.WriteLine($"📄 Pages to backup: {pages.Count}");
            return pages;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            ShowUsage();
            return null;
        }
    }

    /// <summary>
    /// Execute the backup process for all pages using the context
    /// </summary>
    private static async Task<BackupResults> ExecuteBackupAsync(List<string> pagesToBackup, BackupContext context)
    {
        // Process pages in parallel with controlled concurrency
        var semaphore = new SemaphoreSlim(context.Settings.MaxConcurrentRequests);
        var backupTasks = pagesToBackup.Select(pageTitle => 
            BackupPageWithSemaphoreAsync(pageTitle, semaphore, context));
        var pagesInfoArray = await Task.WhenAll(backupTasks);
        
        var pagesInfo = pagesInfoArray.ToDictionary(p => p.Title, p => p);
        var savedFiles = pagesInfo.Values.Where(p => p.Success && p.Filepath != null).Select(p => p.Filepath!).ToList();

        return new BackupResults
        {
            PagesInfo = pagesInfo,
            SavedFiles = savedFiles
        };
    }

    /// <summary>
    /// Wrapper to handle semaphore for controlled concurrency
    /// </summary>
    private static async Task<PageBackupInfo> BackupPageWithSemaphoreAsync(string pageTitle, SemaphoreSlim semaphore, BackupContext context)
    {
        await semaphore.WaitAsync();
        try
        {
            return await BackupPageAsync(pageTitle, context);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Handles the backup logic for a single page
    /// </summary>
    private static async Task<PageBackupInfo> BackupPageAsync(string pageTitle, BackupContext context)
    {
        var revision = await context.WikiClient.GetPageContentAsync(pageTitle);

        var pageInfo = new PageBackupInfo
        {
            Title = pageTitle,
            Timestamp = revision?.Timestamp,
            Comment = revision?.Comment,
            User = revision?.User,
            Success = false
        };

        if (revision != null)
        {
            var filepath = await WikiClient.SavePageToFileAsync(pageTitle, revision, context);
            if (filepath != null)
            {
                pageInfo.Success = true;
                pageInfo.Filepath = Path.GetRelativePath(context.BackupDirectory, filepath);
            }
        }
        return pageInfo;
    }

    /// <summary>
    /// Display backup completion summary
    /// </summary>
    private static void DisplayBackupSummary(BackupResults results)
    {
        Console.WriteLine($""""
            
            📊 Backup completed:
               ✅ {results.SuccessCount} pages saved successfully
               ❌ {results.FailedCount} pages failed
            """");
    }

    /// <summary>
    /// Creates an index file documenting all backed up pages
    /// </summary>
    private static async Task CreateIndexFileAsync(BackupResults results, BackupContext context)
    {
        try
        {
            var indexPath = Path.Combine(context.BackupDirectory, "README.md");
            var backupDate = DateHelper.GetFormattedUtcNow();

            var contentBuilder = new StringBuilder();

            contentBuilder.Append($"""
            # OSM Wiki Pages Backup

            **Backup generated:** {backupDate}<br>
            **Category:** {context.Category}<br>
            **Total pages:** {results.TotalCount}<br>
            **Successfully backed up:** {results.SuccessCount}

            """);

            // Successful backups
            var successfulPages = results.PagesInfo.Values.Where(p => p.Success).OrderBy(p => p.Title).ToList();
            if (successfulPages.Count != 0)
            {
                contentBuilder.AppendLine($"## Successfully Backed Up Pages ({successfulPages.Count})\n");

                var baseUrl = context.Settings.WikiBaseUrl;

                foreach (var pageInfo in successfulPages)
                {
                    var wikiUrl = $"{baseUrl}/{pageInfo.Title.Replace(' ', '_')}";

                    contentBuilder.Append($"""
                    ### {pageInfo.Title}

                    - **File:** [{pageInfo.Filepath}]({pageInfo.Filepath})
                    - **Wiki URL:** [{pageInfo.Title}]({wikiUrl})

                    """);

                    if (!string.IsNullOrEmpty(pageInfo.Timestamp) && DateTime.TryParse(pageInfo.Timestamp, out var timestamp))
                        contentBuilder.AppendLine($"- **Last edited:** {DateHelper.FormatDateTime(timestamp)}");

                    if (!string.IsNullOrEmpty(pageInfo.User))
                        contentBuilder.AppendLine($"- **Last editor:** {pageInfo.User}");

                    if (!string.IsNullOrEmpty(pageInfo.Comment))
                        contentBuilder.AppendLine($"- **Edit comment:** {pageInfo.Comment}");

                    contentBuilder.AppendLine();
                }
            }

            // Failed backups
            var failedPages = results.PagesInfo.Values.Where(p => !p.Success).OrderBy(p => p.Title).ToList();
            if (failedPages.Count != 0)
            {
                contentBuilder.AppendLine($"## Failed Backups ({failedPages.Count})\n");

                foreach (var pageInfo in failedPages)
                {
                    contentBuilder.AppendLine($"- ❌ **{pageInfo.Title}** - Backup failed");
                }
                contentBuilder.AppendLine();
            }

            contentBuilder.Append("""
            ## About

            These files contain the raw MediaWiki source code (wikitext) of the pages.
            This is exactly what you see when you click "Edit" on the wiki.

            The files include:
            - All MediaWiki markup (`{{templates}}`, `[[links]]`, etc.)
            - Original formatting and structure
            - No HTML rendering or styling
            - Pure source content as seen in the editor
            """);

            await File.WriteAllTextAsync(indexPath, contentBuilder.ToString(), System.Text.Encoding.UTF8);

            Console.WriteLine($"📋 Created index: {indexPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error creating index file: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows usage information
    /// </summary>
    private static void ShowUsage()
    {
        Console.WriteLine($""""
        
        Usage: WikiBackup [category]

        Available categories:
          main           - Only the main OpenRailwayMap/Tagging_in_France page (default)
          openrailwaymap - All OpenRailwayMap pages and subpages
          keys           - All Key: namespace pages
          tags           - All Tag: namespace pages
          templates      - All Template: namespace pages
          modules        - All Module: namespace pages (Lua scripts and their templates)
          french         - French railway signalling pages and localized content
          all            - All pages (complete backup)

        Examples:
          WikiBackup main           # Backup only the main page
          WikiBackup openrailwaymap # Backup OpenRailwayMap pages
          WikiBackup all            # Full backup of all pages
        """");
    }
}