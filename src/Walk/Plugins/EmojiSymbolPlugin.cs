using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;
using WpfClipboard = System.Windows.Clipboard;

namespace Walk.Plugins;

public sealed class EmojiSymbolPlugin : IQueryPlugin
{
    private const int MaxResults = 24;
    private readonly TwemojiImageService? _twemojiImageService;

    public EmojiSymbolPlugin(TwemojiImageService? twemojiImageService = null)
    {
        _twemojiImageService = twemojiImageService;
    }

    public string Name => "Emoji";
    public int Priority => 86;

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (!TryParseQuery(query, out var searchTerm, out var filterKind))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = Search(searchTerm, filterKind)
            .Take(MaxResults)
            .Select(match => CreateResult(match, ct))
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static IEnumerable<EmojiSymbolMatch> Search(string searchTerm, EmojiSymbolKind? filterKind)
    {
        var trimmed = searchTerm.Trim();
        var filteredEntries = Entries.Where(entry => filterKind is null || entry.Kind == filterKind.Value);

        if (trimmed.Length == 0)
        {
            return filteredEntries
                .OrderByDescending(static entry => entry.Popularity)
                .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => new EmojiSymbolMatch(entry, entry.Popularity));
        }

        return filteredEntries
            .Select(entry => new EmojiSymbolMatch(entry, GetScore(trimmed, entry)))
            .Where(static match => match.Score > 0)
            .OrderByDescending(static match => match.Score)
            .ThenByDescending(static match => match.Entry.Popularity)
            .ThenBy(static match => match.Entry.Name, StringComparer.OrdinalIgnoreCase);
    }

    private SearchResult CreateResult(EmojiSymbolMatch match, CancellationToken ct)
    {
        var entry = match.Entry;
        var kindLabel = entry.Kind == EmojiSymbolKind.Emoji ? "Emoji" : "Symbol";

        var result = new SearchResult
        {
            Title = entry.Name,
            Subtitle = $"{kindLabel} - {entry.Category}",
            PluginName = "Emoji",
            Score = match.Score,
            IconGlyph = entry.Value,
            Actions =
            [
                new SearchAction
                {
                    Label = "Copy Character",
                    HintLabel = "Copy",
                    Execute = () => WpfClipboard.SetText(entry.Value),
                    KeyGesture = "Enter",
                },
                new SearchAction
                {
                    Label = "Copy Character",
                    HintLabel = "Copy",
                    Execute = () => WpfClipboard.SetText(entry.Value),
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false,
                },
                new SearchAction
                {
                    Label = "Copy Code Point",
                    HintLabel = "Code",
                    Execute = () => WpfClipboard.SetText(FormatCodePoints(entry.Value)),
                    KeyGesture = "Ctrl+U",
                    ClosesLauncher = false,
                },
            ],
        };

        if (entry.Kind == EmojiSymbolKind.Emoji)
            PopulateTwemojiIcon(result, entry.Value, ct);
        else
            result.Icon = CreateTextIcon(entry.Value);

        return result;
    }

    private void PopulateTwemojiIcon(SearchResult result, string emoji, CancellationToken ct)
    {
        if (_twemojiImageService is null)
        {
            result.Icon = CreateTextIcon(emoji);
            return;
        }

        if (_twemojiImageService.TryGetCachedIcon(emoji, out var cachedIcon) && cachedIcon is not null)
        {
            result.Icon = cachedIcon;
            result.Preview = cachedIcon;
            return;
        }

        result.Icon = CreateTextIcon(emoji);
        _ = PopulateTwemojiIconAsync(result, emoji, ct);
    }

    private async Task PopulateTwemojiIconAsync(SearchResult result, string emoji, CancellationToken ct)
    {
        if (_twemojiImageService is null)
            return;

        try
        {
            var icon = await _twemojiImageService.GetIconAsync(emoji, ct).ConfigureAwait(false);
            if (icon is null || ct.IsCancellationRequested)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                result.Icon = icon;
                result.Preview = icon;
                return;
            }

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        result.Icon = icon;
                        result.Preview = icon;
                    }
                },
                DispatcherPriority.Background,
                ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private static bool TryParseQuery(string query, out string searchTerm, out EmojiSymbolKind? filterKind)
    {
        var trimmed = query.Trim();
        searchTerm = "";
        filterKind = null;

        if (trimmed.Equals(":", StringComparison.Ordinal))
        {
            filterKind = EmojiSymbolKind.Emoji;
            return true;
        }

        if (trimmed.StartsWith(":", StringComparison.Ordinal))
        {
            filterKind = EmojiSymbolKind.Emoji;
            searchTerm = trimmed[1..].Trim();
            return true;
        }

        foreach (var prefix in new[] { "emoji", "em" })
        {
            if (TryMatchPrefix(trimmed, prefix, out searchTerm))
            {
                filterKind = EmojiSymbolKind.Emoji;
                return true;
            }
        }

        foreach (var prefix in new[] { "symbol", "symbols", "sym" })
        {
            if (TryMatchPrefix(trimmed, prefix, out searchTerm))
            {
                filterKind = EmojiSymbolKind.Symbol;
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchPrefix(string query, string prefix, out string remainder)
    {
        remainder = "";
        if (query.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!query.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            return false;

        remainder = query[prefix.Length..].Trim();
        return true;
    }

    private static double GetScore(string query, EmojiSymbolEntry entry)
    {
        if (query.Equals(entry.Value, StringComparison.Ordinal))
            return 1.0;

        var score = 0.0;
        var nameMatch = FuzzyMatcher.Match(query, entry.Name);
        if (nameMatch.IsMatch)
            score = Math.Max(score, nameMatch.Score);

        var categoryMatch = FuzzyMatcher.Match(query, entry.Category);
        if (categoryMatch.IsMatch)
            score = Math.Max(score, categoryMatch.Score * 0.75);

        foreach (var keyword in entry.Keywords)
        {
            if (keyword.Equals(query, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 0.96);
            else if (keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 0.9);
            else
            {
                var keywordMatch = FuzzyMatcher.Match(query, keyword);
                if (keywordMatch.IsMatch)
                    score = Math.Max(score, keywordMatch.Score * 0.92);
            }
        }

        return score <= 0 ? 0 : Math.Min(1.0, score + (entry.Popularity * 0.04));
    }

    private static string FormatCodePoints(string value)
    {
        return string.Join(
            " ",
            value.EnumerateRunes().Select(static rune => $"U+{rune.Value:X}"));
    }

    private static ImageSource CreateTextIcon(string value)
    {
        const int size = 64;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var formattedText = new FormattedText(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
                38,
                System.Windows.Media.Brushes.White,
                1.0)
            {
                TextAlignment = TextAlignment.Center,
            };

            context.DrawText(
                formattedText,
                new System.Windows.Point(size / 2d, (size - formattedText.Height) / 2d));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record EmojiSymbolMatch(EmojiSymbolEntry Entry, double Score);

    private sealed record EmojiSymbolEntry(
        string Value,
        string Name,
        EmojiSymbolKind Kind,
        string Category,
        double Popularity,
        params string[] Keywords);

    private enum EmojiSymbolKind
    {
        Emoji,
        Symbol,
    }

    private static readonly IReadOnlyList<EmojiSymbolEntry> Entries =
    [
        new("😀", "Grinning Face", EmojiSymbolKind.Emoji, "Smileys", 1.00, "happy", "smile", "grin", "face"),
        new("😃", "Smiling Face With Big Eyes", EmojiSymbolKind.Emoji, "Smileys", 0.94, "happy", "smile", "joy"),
        new("😄", "Smiling Face With Open Mouth", EmojiSymbolKind.Emoji, "Smileys", 0.94, "happy", "smile", "laugh"),
        new("😁", "Beaming Face", EmojiSymbolKind.Emoji, "Smileys", 0.91, "happy", "smile", "grin"),
        new("😆", "Laughing Face", EmojiSymbolKind.Emoji, "Smileys", 0.89, "laugh", "lol", "happy"),
        new("😂", "Face With Tears Of Joy", EmojiSymbolKind.Emoji, "Smileys", 1.00, "laugh", "cry", "lol", "tears"),
        new("🤣", "Rolling On The Floor Laughing", EmojiSymbolKind.Emoji, "Smileys", 0.98, "laugh", "rofl", "lol"),
        new("😊", "Smiling Face With Smiling Eyes", EmojiSymbolKind.Emoji, "Smileys", 0.96, "happy", "smile", "blush"),
        new("🙂", "Slightly Smiling Face", EmojiSymbolKind.Emoji, "Smileys", 0.91, "smile", "happy"),
        new("😉", "Winking Face", EmojiSymbolKind.Emoji, "Smileys", 0.88, "wink", "joke"),
        new("😍", "Smiling Face With Heart Eyes", EmojiSymbolKind.Emoji, "Smileys", 0.94, "love", "heart", "crush"),
        new("😘", "Face Blowing A Kiss", EmojiSymbolKind.Emoji, "Smileys", 0.85, "kiss", "love"),
        new("😎", "Smiling Face With Sunglasses", EmojiSymbolKind.Emoji, "Smileys", 0.87, "cool", "sunglasses"),
        new("🤔", "Thinking Face", EmojiSymbolKind.Emoji, "Smileys", 0.96, "think", "hmm", "question"),
        new("🤨", "Face With Raised Eyebrow", EmojiSymbolKind.Emoji, "Smileys", 0.79, "skeptical", "doubt"),
        new("😐", "Neutral Face", EmojiSymbolKind.Emoji, "Smileys", 0.76, "neutral", "meh"),
        new("😬", "Grimacing Face", EmojiSymbolKind.Emoji, "Smileys", 0.77, "grimace", "awkward"),
        new("🙄", "Face With Rolling Eyes", EmojiSymbolKind.Emoji, "Smileys", 0.85, "eyeroll", "annoyed"),
        new("😮", "Face With Open Mouth", EmojiSymbolKind.Emoji, "Smileys", 0.78, "surprise", "wow"),
        new("😢", "Crying Face", EmojiSymbolKind.Emoji, "Smileys", 0.80, "sad", "cry", "tear"),
        new("😭", "Loudly Crying Face", EmojiSymbolKind.Emoji, "Smileys", 0.91, "sad", "cry", "tears"),
        new("😡", "Pouting Face", EmojiSymbolKind.Emoji, "Smileys", 0.73, "angry", "mad"),
        new("🤯", "Exploding Head", EmojiSymbolKind.Emoji, "Smileys", 0.83, "mind blown", "wow", "shock"),
        new("🥳", "Partying Face", EmojiSymbolKind.Emoji, "Smileys", 0.90, "party", "celebrate", "birthday"),
        new("😴", "Sleeping Face", EmojiSymbolKind.Emoji, "Smileys", 0.74, "sleep", "tired"),
        new("👍", "Thumbs Up", EmojiSymbolKind.Emoji, "Hands", 1.00, "thumb", "yes", "approve", "like"),
        new("👎", "Thumbs Down", EmojiSymbolKind.Emoji, "Hands", 0.78, "thumb", "no", "disapprove", "dislike"),
        new("👏", "Clapping Hands", EmojiSymbolKind.Emoji, "Hands", 0.90, "clap", "applause", "congrats"),
        new("🙌", "Raising Hands", EmojiSymbolKind.Emoji, "Hands", 0.86, "raise", "celebrate", "hooray"),
        new("🙏", "Folded Hands", EmojiSymbolKind.Emoji, "Hands", 0.93, "please", "thanks", "pray"),
        new("🤝", "Handshake", EmojiSymbolKind.Emoji, "Hands", 0.81, "deal", "agreement", "shake"),
        new("👋", "Waving Hand", EmojiSymbolKind.Emoji, "Hands", 0.83, "wave", "hello", "bye"),
        new("👌", "OK Hand", EmojiSymbolKind.Emoji, "Hands", 0.82, "ok", "perfect"),
        new("✌️", "Victory Hand", EmojiSymbolKind.Emoji, "Hands", 0.79, "peace", "victory"),
        new("🤞", "Crossed Fingers", EmojiSymbolKind.Emoji, "Hands", 0.76, "luck", "hope"),
        new("💪", "Flexed Biceps", EmojiSymbolKind.Emoji, "Body", 0.84, "strong", "muscle", "strength"),
        new("👀", "Eyes", EmojiSymbolKind.Emoji, "Body", 0.88, "look", "watch", "seen"),
        new("🧠", "Brain", EmojiSymbolKind.Emoji, "Body", 0.75, "mind", "smart", "think"),
        new("❤️", "Red Heart", EmojiSymbolKind.Emoji, "Hearts", 1.00, "heart", "love", "red"),
        new("🧡", "Orange Heart", EmojiSymbolKind.Emoji, "Hearts", 0.78, "heart", "love", "orange"),
        new("💛", "Yellow Heart", EmojiSymbolKind.Emoji, "Hearts", 0.79, "heart", "love", "yellow"),
        new("💚", "Green Heart", EmojiSymbolKind.Emoji, "Hearts", 0.80, "heart", "love", "green"),
        new("💙", "Blue Heart", EmojiSymbolKind.Emoji, "Hearts", 0.81, "heart", "love", "blue"),
        new("💜", "Purple Heart", EmojiSymbolKind.Emoji, "Hearts", 0.80, "heart", "love", "purple"),
        new("🖤", "Black Heart", EmojiSymbolKind.Emoji, "Hearts", 0.78, "heart", "love", "black"),
        new("🤍", "White Heart", EmojiSymbolKind.Emoji, "Hearts", 0.77, "heart", "love", "white"),
        new("💔", "Broken Heart", EmojiSymbolKind.Emoji, "Hearts", 0.70, "heart", "sad", "break"),
        new("🔥", "Fire", EmojiSymbolKind.Emoji, "Objects", 0.96, "hot", "flame", "lit"),
        new("✨", "Sparkles", EmojiSymbolKind.Emoji, "Objects", 0.95, "sparkle", "magic", "shine"),
        new("⭐", "Star", EmojiSymbolKind.Emoji, "Objects", 0.88, "star", "favorite"),
        new("💯", "Hundred Points", EmojiSymbolKind.Emoji, "Objects", 0.89, "hundred", "perfect", "100"),
        new("✅", "Check Mark Button", EmojiSymbolKind.Emoji, "Symbols", 0.94, "check", "done", "yes", "complete"),
        new("❌", "Cross Mark", EmojiSymbolKind.Emoji, "Symbols", 0.86, "cross", "x", "no", "close"),
        new("⚠️", "Warning", EmojiSymbolKind.Emoji, "Symbols", 0.88, "warn", "warning", "alert"),
        new("🚀", "Rocket", EmojiSymbolKind.Emoji, "Travel", 0.94, "rocket", "ship", "launch"),
        new("🎉", "Party Popper", EmojiSymbolKind.Emoji, "Activities", 0.96, "party", "celebrate", "confetti"),
        new("🎯", "Bullseye", EmojiSymbolKind.Emoji, "Activities", 0.82, "target", "goal"),
        new("🏆", "Trophy", EmojiSymbolKind.Emoji, "Activities", 0.82, "win", "award", "trophy"),
        new("💡", "Light Bulb", EmojiSymbolKind.Emoji, "Objects", 0.86, "idea", "light", "bulb"),
        new("📌", "Pushpin", EmojiSymbolKind.Emoji, "Objects", 0.81, "pin", "pushpin"),
        new("📎", "Paperclip", EmojiSymbolKind.Emoji, "Objects", 0.73, "attachment", "paperclip"),
        new("📅", "Calendar", EmojiSymbolKind.Emoji, "Objects", 0.73, "date", "calendar"),
        new("📣", "Megaphone", EmojiSymbolKind.Emoji, "Objects", 0.78, "announce", "megaphone"),
        new("🔒", "Locked", EmojiSymbolKind.Emoji, "Objects", 0.78, "lock", "secure"),
        new("🔓", "Unlocked", EmojiSymbolKind.Emoji, "Objects", 0.68, "unlock", "open"),
        new("🔑", "Key", EmojiSymbolKind.Emoji, "Objects", 0.72, "key", "password"),
        new("📷", "Camera", EmojiSymbolKind.Emoji, "Objects", 0.70, "photo", "camera"),
        new("🖼️", "Framed Picture", EmojiSymbolKind.Emoji, "Objects", 0.70, "image", "picture", "photo"),
        new("📄", "Page Facing Up", EmojiSymbolKind.Emoji, "Objects", 0.72, "file", "document", "page"),
        new("📁", "File Folder", EmojiSymbolKind.Emoji, "Objects", 0.72, "folder", "directory"),
        new("💻", "Laptop", EmojiSymbolKind.Emoji, "Objects", 0.78, "computer", "laptop", "code"),
        new("⌨️", "Keyboard", EmojiSymbolKind.Emoji, "Objects", 0.71, "keyboard", "type"),
        new("🐛", "Bug", EmojiSymbolKind.Emoji, "Animals", 0.82, "bug", "issue", "debug"),
        new("⚙️", "Gear", EmojiSymbolKind.Emoji, "Objects", 0.78, "settings", "gear", "config"),
        new("🛠️", "Hammer And Wrench", EmojiSymbolKind.Emoji, "Objects", 0.74, "tool", "fix", "build"),
        new("☕", "Hot Beverage", EmojiSymbolKind.Emoji, "Food", 0.80, "coffee", "tea"),

        new("✓", "Check Mark", EmojiSymbolKind.Symbol, "Marks", 1.00, "check", "tick", "done", "yes"),
        new("✔", "Heavy Check Mark", EmojiSymbolKind.Symbol, "Marks", 0.96, "check", "tick", "done", "yes"),
        new("✕", "Multiplication X", EmojiSymbolKind.Symbol, "Marks", 0.85, "x", "close", "cancel"),
        new("✖", "Heavy Multiplication X", EmojiSymbolKind.Symbol, "Marks", 0.86, "x", "close", "cancel"),
        new("✗", "Ballot X", EmojiSymbolKind.Symbol, "Marks", 0.84, "x", "no", "fail"),
        new("•", "Bullet", EmojiSymbolKind.Symbol, "Punctuation", 0.92, "bullet", "dot"),
        new("·", "Middle Dot", EmojiSymbolKind.Symbol, "Punctuation", 0.78, "dot", "middle"),
        new("…", "Ellipsis", EmojiSymbolKind.Symbol, "Punctuation", 0.86, "ellipsis", "dots"),
        new("—", "Em Dash", EmojiSymbolKind.Symbol, "Punctuation", 0.82, "dash", "em dash"),
        new("–", "En Dash", EmojiSymbolKind.Symbol, "Punctuation", 0.75, "dash", "en dash"),
        new("©", "Copyright Sign", EmojiSymbolKind.Symbol, "Legal", 0.82, "copyright", "legal"),
        new("®", "Registered Sign", EmojiSymbolKind.Symbol, "Legal", 0.78, "registered", "trademark"),
        new("™", "Trademark Sign", EmojiSymbolKind.Symbol, "Legal", 0.80, "trademark", "tm"),
        new("°", "Degree Sign", EmojiSymbolKind.Symbol, "Units", 0.82, "degree", "temperature"),
        new("№", "Numero Sign", EmojiSymbolKind.Symbol, "Text", 0.64, "number", "numero"),
        new("§", "Section Sign", EmojiSymbolKind.Symbol, "Legal", 0.70, "section", "legal"),
        new("¶", "Pilcrow Sign", EmojiSymbolKind.Symbol, "Text", 0.65, "paragraph", "pilcrow"),
        new("→", "Right Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.95, "arrow", "right", "next"),
        new("←", "Left Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.90, "arrow", "left", "back"),
        new("↑", "Up Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.88, "arrow", "up"),
        new("↓", "Down Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.88, "arrow", "down"),
        new("↔", "Left Right Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.76, "arrow", "horizontal"),
        new("↕", "Up Down Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.70, "arrow", "vertical"),
        new("⇒", "Right Double Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.74, "arrow", "right", "double"),
        new("⇐", "Left Double Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.70, "arrow", "left", "double"),
        new("↗", "Up Right Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.68, "arrow", "up right"),
        new("↘", "Down Right Arrow", EmojiSymbolKind.Symbol, "Arrows", 0.66, "arrow", "down right"),
        new("↩", "Leftwards Arrow With Hook", EmojiSymbolKind.Symbol, "Arrows", 0.65, "return", "back"),
        new("↪", "Rightwards Arrow With Hook", EmojiSymbolKind.Symbol, "Arrows", 0.65, "return", "forward"),
        new("⇧", "Upwards White Arrow", EmojiSymbolKind.Symbol, "Keyboard", 0.72, "shift", "keyboard"),
        new("⌘", "Command Key", EmojiSymbolKind.Symbol, "Keyboard", 0.78, "command", "cmd", "keyboard"),
        new("⌥", "Option Key", EmojiSymbolKind.Symbol, "Keyboard", 0.70, "option", "alt", "keyboard"),
        new("⌃", "Control Key", EmojiSymbolKind.Symbol, "Keyboard", 0.68, "control", "ctrl", "keyboard"),
        new("⌫", "Erase To The Left", EmojiSymbolKind.Symbol, "Keyboard", 0.68, "backspace", "delete"),
        new("⏎", "Return Symbol", EmojiSymbolKind.Symbol, "Keyboard", 0.76, "enter", "return"),
        new("␣", "Open Box", EmojiSymbolKind.Symbol, "Keyboard", 0.60, "space", "spacebar"),
        new("±", "Plus Minus", EmojiSymbolKind.Symbol, "Math", 0.82, "plus minus", "math"),
        new("×", "Multiplication Sign", EmojiSymbolKind.Symbol, "Math", 0.78, "multiply", "times"),
        new("÷", "Division Sign", EmojiSymbolKind.Symbol, "Math", 0.78, "divide", "division"),
        new("≈", "Almost Equal", EmojiSymbolKind.Symbol, "Math", 0.76, "approx", "approximately"),
        new("≠", "Not Equal", EmojiSymbolKind.Symbol, "Math", 0.80, "not equal", "math"),
        new("≤", "Less Than Or Equal", EmojiSymbolKind.Symbol, "Math", 0.76, "less", "equal"),
        new("≥", "Greater Than Or Equal", EmojiSymbolKind.Symbol, "Math", 0.76, "greater", "equal"),
        new("∞", "Infinity", EmojiSymbolKind.Symbol, "Math", 0.82, "infinity", "forever"),
        new("√", "Square Root", EmojiSymbolKind.Symbol, "Math", 0.72, "root", "sqrt"),
        new("∑", "N-Ary Summation", EmojiSymbolKind.Symbol, "Math", 0.68, "sum", "sigma"),
        new("∆", "Increment", EmojiSymbolKind.Symbol, "Math", 0.62, "delta", "change"),
        new("π", "Greek Small Letter Pi", EmojiSymbolKind.Symbol, "Greek", 0.80, "pi", "math"),
        new("µ", "Micro Sign", EmojiSymbolKind.Symbol, "Units", 0.66, "micro", "mu"),
        new("Ω", "Greek Capital Letter Omega", EmojiSymbolKind.Symbol, "Greek", 0.68, "omega", "ohm"),
        new("€", "Euro Sign", EmojiSymbolKind.Symbol, "Currency", 0.80, "euro", "currency"),
        new("£", "Pound Sign", EmojiSymbolKind.Symbol, "Currency", 0.78, "pound", "currency"),
        new("¥", "Yen Sign", EmojiSymbolKind.Symbol, "Currency", 0.74, "yen", "currency"),
        new("₹", "Indian Rupee Sign", EmojiSymbolKind.Symbol, "Currency", 0.78, "rupee", "inr", "currency"),
        new("¢", "Cent Sign", EmojiSymbolKind.Symbol, "Currency", 0.64, "cent", "currency"),
        new("♥", "Heart Suit", EmojiSymbolKind.Symbol, "Shapes", 0.78, "heart", "love"),
        new("★", "Black Star", EmojiSymbolKind.Symbol, "Shapes", 0.82, "star", "favorite"),
        new("☆", "White Star", EmojiSymbolKind.Symbol, "Shapes", 0.74, "star", "favorite"),
        new("◆", "Black Diamond", EmojiSymbolKind.Symbol, "Shapes", 0.66, "diamond", "shape"),
        new("◇", "White Diamond", EmojiSymbolKind.Symbol, "Shapes", 0.62, "diamond", "shape"),
        new("■", "Black Square", EmojiSymbolKind.Symbol, "Shapes", 0.68, "square", "shape"),
        new("□", "White Square", EmojiSymbolKind.Symbol, "Shapes", 0.62, "square", "shape"),
        new("●", "Black Circle", EmojiSymbolKind.Symbol, "Shapes", 0.70, "circle", "dot"),
        new("○", "White Circle", EmojiSymbolKind.Symbol, "Shapes", 0.64, "circle", "shape"),
    ];
}
