using System.IO;
using System.Text.Json;

namespace Walk.Services;

public sealed class CacheService
{
    private readonly string _cacheDir;

    public CacheService(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<T?> GetOrSetAsync<T>(string fileName, TimeSpan ttl, Func<Task<T>> factory)
        where T : class
    {
        var filePath = Path.Combine(_cacheDir, fileName);

        if (File.Exists(filePath))
        {
            var cacheEntry = await ReadCacheEntry<T>(filePath);
            if (cacheEntry is not null && DateTime.UtcNow - cacheEntry.FetchedAt < ttl)
                return cacheEntry.Data;
        }

        try
        {
            var data = await factory();
            await WriteCacheEntry(filePath, data);
            return data;
        }
        catch
        {
            var stale = await ReadCacheEntry<T>(filePath);
            return stale?.Data;
        }
    }

    private static async Task<CacheEntry<T>?> ReadCacheEntry<T>(string path) where T : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<CacheEntry<T>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheEntry<T>(string path, T data) where T : class
    {
        var entry = new CacheEntry<T> { Data = data, FetchedAt = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private sealed class CacheEntry<T> where T : class
    {
        public T? Data { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}
