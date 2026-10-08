using Umbraco.Cms.Core.Models;
using Umbraco.Extensions;

namespace Umbraco.AI.Core.PropertyValueOperations;

/// <summary>
/// Identifies a culture/segment combination for variant-aware property values.
/// </summary>
/// <param name="Culture">The culture code (e.g. "en-US"), or <c>null</c> for invariant content.</param>
/// <param name="Segment">The segment alias, or <c>null</c> for non-segmented content.</param>
public sealed record AIVariantId(string? Culture, string? Segment)
{
    /// <summary>
    /// Gets a variant identifier representing invariant, non-segmented content.
    /// </summary>
    public static AIVariantId Invariant { get; } = new(null, null);

    /// <summary>
    /// Gets a value indicating whether this identifier represents invariant, non-segmented content.
    /// </summary>
    public bool IsInvariant => Culture is null && Segment is null;

    /// <summary>
    /// Narrows a variant to the dimensions a property type (or element type) actually varies by.
    /// Mirrors the CMS rule for block values: a value carries a culture only when its own property
    /// type varies by culture, and a segment only when it varies by segment.
    /// </summary>
    internal static AIVariantId ForVariations(AIVariantId? variant, ContentVariation variations)
        => new(
            variations.VariesByCulture() ? variant?.Culture : null,
            variations.VariesBySegment() ? variant?.Segment : null);
}
