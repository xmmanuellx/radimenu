using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RadiMenu.Models;

namespace RadiMenu.Services
{
    public class IconifyService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string API_BASE_URL = "https://api.iconify.design";
        private readonly string _cacheDirectory;

        public IconifyService()
        {
            // Set up cache directory in the app's running folder or local app data
            // For this project, we'll use a local Data folder.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _cacheDirectory = Path.Combine(baseDir, "Data", "Icons", "Cache");
            
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }

        /// <summary>
        /// Searches for icons using the Iconify API.
        /// </summary>
        public async Task<IconifySearchResult?> SearchIconsAsync(string query, int limit = 64)
        {
            try
            {
                string url = $"{API_BASE_URL}/search?query={Uri.EscapeDataString(query)}&limit={limit}";
                string json = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<IconifySearchResult>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching icons: {ex.Message}");
                return new IconifySearchResult(); // Return empty result on failure
            }
        }

        /// <summary>
        /// Gets the icon data (SVG body, etc.) either from local cache or the API.
        /// </summary>
        /// <param name="iconName">Full icon name (e.g., "mdi:account" or "mdi-account")</param>
        public async Task<IconifyIconData?> GetIconDataAsync(string iconName)
        {
            // specific cleanup for icon names, ensuring "prefix:name" format is handled or "prefix-name"
            // The API expects connection via route: /{prefix}/{name}.json
            
            var (prefix, name) = ParseIconName(iconName);
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(name)) return null;

            // 1. Check Cache
            string cachePath = Path.Combine(_cacheDirectory, prefix, $"{name}.json");
            if (File.Exists(cachePath))
            {
                try
                {
                    string cachedJson = await File.ReadAllTextAsync(cachePath);
                    var data = JsonSerializer.Deserialize<IconifyIconData>(cachedJson);
                    if (data != null)
                    {
                        data.Prefix = prefix;
                        data.Name = name;
                        return data;
                    }
                }
                catch
                {
                    // Ignore cache errors, try fetching
                }
            }

            // 2. Fetch from API
            try
            {
                // Correct Endpoint: https://api.iconify.design/prefix.json?icons=name
                string url = $"{API_BASE_URL}/{prefix}.json?icons={name}";
                string json = await _httpClient.GetStringAsync(url);
                
                var collection = JsonSerializer.Deserialize<IconifyCollectionResponse>(json);
                if (collection != null && collection.Icons.ContainsKey(name))
                {
                    var data = collection.Icons[name];
                    data.Prefix = prefix;
                    data.Name = name;

                    // 3. Save to Cache
                    // We save just the specific icon data to keep cache simple and flat for our usage
                    string prefixDir = Path.Combine(_cacheDirectory, prefix);
                    if (!Directory.Exists(prefixDir)) Directory.CreateDirectory(prefixDir);
                    
                    // Re-serialize just the icon object for simple cache reading later
                    string flatJson = JsonSerializer.Serialize(data);
                    await File.WriteAllTextAsync(cachePath, flatJson);

                    return data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching icon {iconName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Extracts the Path Data (d attribute) from the SVG body.
        /// </summary>
        public string? ExtractPathData(string svgBody)
        {
            if (string.IsNullOrEmpty(svgBody)) return null;

            // Regex for d="..."
            var match = Regex.Match(svgBody, "d=\"([^\"]+)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            
            // Simple fallback if quotes are single
            match = Regex.Match(svgBody, "d='([^']+)'");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        private (string prefix, string name) ParseIconName(string fullIconName)
        {
            // Expecting "prefix:name" (our internal convention) or "prefix-name" (Iconify search result convention)
            // Note: "mdi-light:home" -> prefix "mdi-light", name "home"
            // The API search returns results like "mdi:home" or "mdi-light:home" generally, 
            // but sometimes just "prefix:name".
            
            if (fullIconName.Contains(":"))
            {
                var parts = fullIconName.Split(':');
                if (parts.Length == 2) return (parts[0], parts[1]);
            }
            
            // Fallback for hyphenated "prefix-name" (less reliable as names can have hyphens)
            // But Iconify usually enforces "prefix:name" in search results.
            // If we receive "mdi-account", we might have to guess. 
            // For now, let's assume the input IS correctly formatted as "prefix:name" from our system.
            
            return (string.Empty, string.Empty);
        }
    }
}
