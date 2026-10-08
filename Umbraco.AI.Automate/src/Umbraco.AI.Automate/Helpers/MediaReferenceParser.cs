using System.Text.Json;
using Umbraco.Cms.Core;
using CmsConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.AI.Automate.Helpers;

/// <summary>
/// Parses a free-form media reference setting into a list of media keys.
/// </summary>
/// <remarks>
/// Accepts media keys, <c>umb://media/…</c> UDIs, and media picker JSON (single object or array),
/// either on their own or as a comma/semicolon/newline separated list. File paths are deliberately
/// not accepted so that every reference maps to a media node the caller can authorize.
/// </remarks>
internal static class MediaReferenceParser
{
    private static readonly char[] Separators = [',', ';', '\n', '\r'];

    /// <summary>
    /// Parses <paramref name="value"/> into distinct media keys, in the order they appear.
    /// </summary>
    /// <param name="value">The raw setting value.</param>
    /// <param name="mediaKeys">The parsed media keys.</param>
    /// <param name="invalidReference">The first entry that couldn't be parsed, when parsing fails.</param>
    /// <returns><c>true</c> when every entry is a valid media reference; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out IReadOnlyList<Guid> mediaKeys, out string? invalidReference)
    {
        var keys = new List<Guid>();
        mediaKeys = keys;
        invalidReference = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return TryParseJson(trimmed, keys, out invalidReference);
        }

        foreach (var entry in trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseKey(entry, out var key))
            {
                invalidReference = entry;
                return false;
            }

            AddDistinct(keys, key);
        }

        return true;
    }

    private static bool TryParseJson(string json, List<Guid> keys, out string? invalidReference)
    {
        invalidReference = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            invalidReference = json;
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : [root];

            foreach (var item in items)
            {
                if (!TryParseJsonItem(item, out var key))
                {
                    invalidReference = item.GetRawText();
                    return false;
                }

                AddDistinct(keys, key);
            }
        }

        return true;
    }

    private static bool TryParseJsonItem(JsonElement item, out Guid key)
    {
        key = Guid.Empty;

        if (item.ValueKind == JsonValueKind.String)
        {
            return TryParseKey(item.GetString(), out key);
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Media picker v3: {"mediaKey": "guid"}. Legacy: {"key": "guid"} or {"udi": "umb://media/guid"}.
        foreach (var propertyName in new[] { "mediaKey", "key", "udi" })
        {
            if (item.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && TryParseKey(property.GetString(), out key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseKey(string? value, out Guid key)
    {
        key = Guid.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Guid.TryParse(value, out key))
        {
            return true;
        }

        if (value.StartsWith("umb://", StringComparison.OrdinalIgnoreCase)
            && UdiParser.TryParse(value, out GuidUdi? udi)
            && udi is not null
            && string.Equals(udi.EntityType, CmsConstants.UdiEntityType.Media, StringComparison.OrdinalIgnoreCase))
        {
            key = udi.Guid;
            return true;
        }

        return false;
    }

    private static void AddDistinct(List<Guid> keys, Guid key)
    {
        if (!keys.Contains(key))
        {
            keys.Add(key);
        }
    }
}
