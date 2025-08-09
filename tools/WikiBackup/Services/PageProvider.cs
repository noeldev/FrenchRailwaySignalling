using Microsoft.Extensions.Configuration;

namespace WikiBackup.Services;

/// <summary>
/// Provides access to predefined lists of wiki pages organized by category
/// </summary>
/// <remarks>
/// Initializes a new instance with page categories from configuration
/// </remarks>
/// <param name="configuration">Application configuration containing PageCategories section</param>
public class PageProvider(IConfiguration configuration)
{
    private readonly Dictionary<string, List<string>> _categories = configuration.GetSection("PageCategories")
            .Get<Dictionary<string, List<string>>>() ?? [];

    /// <summary>
    /// Gets the list of pages for a specific category
    /// </summary>
    /// <param name="category">Category name (case-insensitive)</param>
    /// <returns>List of page titles in the category</returns>
    /// <exception cref="ArgumentException">Thrown when category is not found</exception>
    public List<string> GetPagesByCategory(string category)
    {
        var key = category.ToLowerInvariant();
        
        if (key == "all")
        {
            return [.. _categories.Values.SelectMany(list => list).Distinct()];
        }

        if (_categories.TryGetValue(key, out var pages))
        {
            return pages;
        }

        throw new ArgumentException(
            $"Unknown category: {category}. Available categories: {string.Join(", ", GetAvailableCategories())}");
    }

    /// <summary>
    /// Gets the list of all available category names
    /// </summary>
    /// <returns>List of available category names</returns>
    public List<string> GetAvailableCategories()
    {
        var categories = _categories.Keys.ToList();
        categories.Add("all"); // Add the special "all" category
        return [.. categories.OrderBy(c => c)];
    }
}