using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Walk.Helpers;
using Walk.Models;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace Walk.Services;

public sealed class ClipboardHistoryService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int DefaultMaxEntries = 200;
    private const int MaxStoredTextLength = 100_000;
    private const int MaxTitleLength = 96;
    private const int MaxPreviewLength = 240;

    private readonly object _gate = new();
    private readonly string _historyPath;
    private readonly string _imageDirectory;
    private readonly int _maxEntries;
    private List<ClipboardHistoryEntry>? _entries;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _isListening;
    private bool _suppressNextClipboardUpdate;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ClipboardHistoryService(string dataDir, int maxEntries = DefaultMaxEntries)
    {
        Directory.CreateDirectory(dataDir);
        _historyPath = Path.Combine(dataDir, "clipboard-history.json");
        _imageDirectory = Path.Combine(dataDir, "ClipboardImages");
        _maxEntries = Math.Max(1, maxEntries);
    }

    public void StartListening(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        if (_source is not null && _windowHandle == windowHandle)
            return;

        StopListening();

        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WndProc);
        _isListening = AddClipboardFormatListener(windowHandle);

        CaptureCurrentClipboard();
    }

    public void StopListening()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        if (_isListening && _windowHandle != IntPtr.Zero)
            RemoveClipboardFormatListener(_windowHandle);

        _windowHandle = IntPtr.Zero;
        _isListening = false;
        _suppressNextClipboardUpdate = false;
    }

    public IReadOnlyList<ClipboardHistoryEntry> GetEntries()
    {
        EnsureLoaded();

        lock (_gate)
        {
            return _entries!
                .Select(CloneEntry)
                .ToList();
        }
    }

    public IReadOnlyList<ClipboardHistoryEntry> Search(string query, int maxResults = 20)
    {
        EnsureLoaded();

        var trimmed = query.Trim();
        var limit = Math.Max(1, maxResults);

        lock (_gate)
        {
            if (trimmed.Length == 0)
            {
                return _entries!
                    .OrderByDescending(static entry => entry.IsPinned)
                    .ThenByDescending(static entry => entry.LastUsedUtc)
                    .ThenByDescending(static entry => entry.LastCopiedUtc)
                    .Take(limit)
                    .Select(CloneEntry)
                    .ToList();
            }

            return _entries!
                .Select(entry => (Entry: entry, Score: GetMatchScore(trimmed, entry)))
                .Where(match => match.Score > 0)
                .OrderByDescending(static match => match.Entry.IsPinned)
                .ThenByDescending(static match => match.Score)
                .ThenByDescending(static match => match.Entry.LastUsedUtc)
                .ThenByDescending(static match => match.Entry.LastCopiedUtc)
                .Take(limit)
                .Select(match => CloneEntry(match.Entry))
                .ToList();
        }
    }

    public ClipboardHistoryEntry? RecordText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = NormalizeTextForStorage(text);
        if (normalized.Length == 0)
            return null;

        var entry = new ClipboardHistoryEntry
        {
            Kind = ClipboardHistoryKind.Text,
            Text = normalized,
            Title = CreateTextTitle(normalized),
        };

        return RecordEntry(entry);
    }

    public ClipboardHistoryEntry? RecordFiles(IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            return null;

        var entry = new ClipboardHistoryEntry
        {
            Kind = ClipboardHistoryKind.Files,
            FilePaths = paths,
            Title = CreateFilesTitle(paths),
        };

        return RecordEntry(entry);
    }

    public ClipboardHistoryEntry? RecordImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            return null;

        byte[] pngBytes;
        try
        {
            pngBytes = EncodePng(image);
        }
        catch
        {
            return null;
        }

        if (pngBytes.Length == 0)
            return null;

        var hash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();
        Directory.CreateDirectory(_imageDirectory);
        var imagePath = Path.Combine(_imageDirectory, $"{hash}.png");

        try
        {
            if (!File.Exists(imagePath))
                File.WriteAllBytes(imagePath, pngBytes);
        }
        catch
        {
            return null;
        }

        var entry = new ClipboardHistoryEntry
        {
            Kind = ClipboardHistoryKind.Image,
            ImagePath = imagePath,
            ImageHash = hash,
            ImageWidth = image.PixelWidth,
            ImageHeight = image.PixelHeight,
            Title = CreateImageTitle(image.PixelWidth, image.PixelHeight),
        };

        return RecordEntry(entry);
    }

    public void CopyEntryToClipboard(string entryId)
    {
        var entry = GetEntry(entryId);
        if (entry is null)
            return;

        try
        {
            _suppressNextClipboardUpdate = true;

            if (entry.Kind == ClipboardHistoryKind.Files && entry.FilePaths.Count > 0)
            {
                var collection = new StringCollection();
                collection.AddRange(entry.FilePaths.ToArray());
                WpfClipboard.SetFileDropList(collection);
            }
            else if (!string.IsNullOrEmpty(entry.Text))
            {
                WpfClipboard.SetText(entry.Text, WpfTextDataFormat.UnicodeText);
            }
            else if (entry.Kind == ClipboardHistoryKind.Image && TryLoadBitmap(entry.ImagePath, out var bitmap))
            {
                WpfClipboard.SetImage(bitmap);
            }

            RecordUse(entry.Id);
        }
        catch
        {
            _suppressNextClipboardUpdate = false;
        }
    }

    public void TogglePinned(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            if (entry is null)
                return;

            entry.IsPinned = !entry.IsPinned;
            SaveUnsafe();
        }
    }

    public void DeleteEntry(string entryId)
    {
        EnsureLoaded();
        string? deletedImagePath = null;

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            if (entry is null)
                return;

            deletedImagePath = entry.ImagePath;
            _entries!.Remove(entry);
            SaveUnsafe();
            DeleteImageIfUnusedUnsafe(deletedImagePath);
        }
    }

    public void Dispose()
    {
        StopListening();
    }

    private ClipboardHistoryEntry? RecordEntry(ClipboardHistoryEntry entry)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var existing = _entries!.FirstOrDefault(candidate => AreSameContent(candidate, entry));

            if (existing is not null)
            {
                existing.Title = entry.Title;
                existing.Text = entry.Text;
                existing.FilePaths = entry.FilePaths;
                existing.ImagePath = entry.ImagePath;
                existing.ImageHash = entry.ImageHash;
                existing.ImageWidth = entry.ImageWidth;
                existing.ImageHeight = entry.ImageHeight;
                existing.CopyCount++;
                existing.LastCopiedUtc = now;
                existing.LastUsedUtc = now;
                SaveUnsafe();
                return CloneEntry(existing);
            }

            entry.Id = Guid.NewGuid().ToString("N");
            entry.CopyCount = 1;
            entry.UseCount = 0;
            entry.CreatedUtc = now;
            entry.LastCopiedUtc = now;
            entry.LastUsedUtc = now;
            _entries!.Add(entry);
            TrimEntriesUnsafe();
            SaveUnsafe();
            return CloneEntry(entry);
        }
    }

    private ClipboardHistoryEntry? GetEntry(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            return entry is null ? null : CloneEntry(entry);
        }
    }

    private void RecordUse(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            if (entry is null)
                return;

            entry.UseCount++;
            entry.LastUsedUtc = DateTime.UtcNow;
            SaveUnsafe();
        }
    }

    private void CaptureCurrentClipboard()
    {
        if (_suppressNextClipboardUpdate)
        {
            _suppressNextClipboardUpdate = false;
            return;
        }

        try
        {
            if (WpfClipboard.ContainsFileDropList())
            {
                var files = WpfClipboard.GetFileDropList().Cast<string>();
                RecordFiles(files);
                return;
            }

            if (WpfClipboard.ContainsImage())
            {
                var image = WpfClipboard.GetImage();
                if (image is not null)
                    RecordImage(image);
                return;
            }

            if (WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText))
                RecordText(WpfClipboard.GetText(WpfTextDataFormat.UnicodeText));
        }
        catch
        {
            // Clipboard access can fail when another process owns it briefly.
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            CaptureCurrentClipboard();

        return IntPtr.Zero;
    }

    private void EnsureLoaded()
    {
        if (_entries is not null)
            return;

        lock (_gate)
        {
            if (_entries is not null)
                return;

            if (!File.Exists(_historyPath))
            {
                _entries = [];
                return;
            }

            try
            {
                var json = File.ReadAllText(_historyPath);
                _entries = JsonSerializer.Deserialize<List<ClipboardHistoryEntry>>(json) ?? [];
            }
            catch
            {
                _entries = [];
            }
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch
        {
            // Clipboard history should never block the active copy operation.
        }
    }

    private void TrimEntriesUnsafe()
    {
        var overflow = _entries!.Count - _maxEntries;
        if (overflow <= 0)
            return;

        var removable = _entries
            .Where(static entry => !entry.IsPinned)
            .OrderBy(static entry => entry.LastUsedUtc)
            .ThenBy(static entry => entry.LastCopiedUtc)
            .Take(overflow)
            .ToList();

        foreach (var entry in removable)
        {
            _entries.Remove(entry);
            DeleteImageIfUnusedUnsafe(entry.ImagePath);
        }
    }

    private static bool AreSameContent(ClipboardHistoryEntry left, ClipboardHistoryEntry right)
    {
        if (left.Kind != right.Kind)
            return false;

        if (left.Kind == ClipboardHistoryKind.Text)
            return string.Equals(left.Text, right.Text, StringComparison.Ordinal);

        if (left.Kind == ClipboardHistoryKind.Image)
            return !string.IsNullOrWhiteSpace(left.ImageHash) &&
                   string.Equals(left.ImageHash, right.ImageHash, StringComparison.OrdinalIgnoreCase);

        return left.FilePaths.SequenceEqual(right.FilePaths, StringComparer.OrdinalIgnoreCase);
    }

    private static double GetMatchScore(string query, ClipboardHistoryEntry entry)
    {
        var titleMatch = FuzzyMatcher.Match(query, entry.Title);
        var score = titleMatch.IsMatch ? titleMatch.Score : 0.0;

        if (entry.Kind == ClipboardHistoryKind.Text && !string.IsNullOrEmpty(entry.Text))
        {
            if (entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 0.82);

            var preview = Truncate(entry.Text, MaxPreviewLength);
            var previewMatch = FuzzyMatcher.Match(query, preview);
            if (previewMatch.IsMatch)
                score = Math.Max(score, previewMatch.Score * 0.9);
        }

        foreach (var path in entry.FilePaths)
        {
            if (path.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 0.84);

            var fileName = Path.GetFileName(path);
            var fileMatch = FuzzyMatcher.Match(query, fileName);
            if (fileMatch.IsMatch)
                score = Math.Max(score, fileMatch.Score);
        }

        if (entry.Kind == ClipboardHistoryKind.Image)
        {
            if (query.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                query.Equals("screenshot", StringComparison.OrdinalIgnoreCase) ||
                query.Equals("picture", StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 0.88);
            }

            var dimensions = $"{entry.ImageWidth}x{entry.ImageHeight}";
            if (dimensions.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 0.82);
        }

        return score;
    }

    private static ClipboardHistoryEntry CloneEntry(ClipboardHistoryEntry entry)
    {
        return new ClipboardHistoryEntry
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Title = entry.Title,
            Text = entry.Text,
            FilePaths = entry.FilePaths.ToList(),
            ImagePath = entry.ImagePath,
            ImageHash = entry.ImageHash,
            ImageWidth = entry.ImageWidth,
            ImageHeight = entry.ImageHeight,
            IsPinned = entry.IsPinned,
            CopyCount = entry.CopyCount,
            UseCount = entry.UseCount,
            CreatedUtc = entry.CreatedUtc,
            LastCopiedUtc = entry.LastCopiedUtc,
            LastUsedUtc = entry.LastUsedUtc,
        };
    }

    private static string NormalizeTextForStorage(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return normalized.Length <= MaxStoredTextLength
            ? normalized
            : normalized[..MaxStoredTextLength];
    }

    private static string CreateTextTitle(string text)
    {
        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? text;

        var collapsed = CollapseWhitespace(firstLine);
        return Truncate(string.IsNullOrWhiteSpace(collapsed) ? "Text" : collapsed, MaxTitleLength);
    }

    private static string CreateFilesTitle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
        {
            var name = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? paths[0] : name;
        }

        return $"{paths.Count} files";
    }

    private static string CreateImageTitle(int width, int height)
    {
        return width > 0 && height > 0
            ? $"Image {width} x {height}"
            : "Image";
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                    builder.Append(' ');

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "...";
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool TryLoadBitmap(string? imagePath, out BitmapSource bitmap)
    {
        bitmap = null!;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return false;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(imagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            bitmap = image;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DeleteImageIfUnusedUnsafe(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (_entries!.Any(entry => string.Equals(entry.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase)))
            return;

        TryDeleteFile(imagePath);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
