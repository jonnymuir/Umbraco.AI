using System.Text.Json;
using System.Text.Json.Nodes;

using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;

namespace Umbraco.AI.Core.Tools.Umbraco;

/// <summary>
/// Reads a content property's current value in the editor (Management API) format — the shape
/// <c>IContentEditingService</c> expects back on save — rather than
/// the stored (database) format <see cref="IProperty.GetValue"/> returns.
/// </summary>
/// <remarks>
/// The two formats differ for many editors: a block's nested values are stored as JSON strings
/// (e.g. a content picker's <c>"[{\"udi\":...}]"</c> or a dropdown's <c>"[\"primary\"]"</c>), and
/// resubmitting those as-is makes the value editors drop them. This mirrors what the backoffice does
/// when it loads a document for editing (<c>DocumentEditingPresentationFactory</c>): run the value
/// through the property editor's <see cref="IDataValueEditor.ToEditor"/>, then serialize it the way
/// an HTTP response would.
/// </remarks>
internal sealed class ContentEditorValueReader(PropertyEditorCollection propertyEditors, IJsonSerializer jsonSerializer)
{
    /// <summary>
    /// Gets the property's value in editor format, as a <see cref="JsonNode"/>.
    /// </summary>
    public JsonNode? GetEditorValueNode(IProperty property, string? culture, string? segment)
    {
        // Same fallback as the CMS's MissingPropertyEditor: an editor that isn't installed any more
        // can only hand back the stored value.
        if (!propertyEditors.TryGet(property.PropertyType.PropertyEditorAlias, out var editor))
        {
            return ContentPropertyValueOperationHelper.ToJsonNode(property.GetValue(culture, segment));
        }

        return editor.GetValueEditor().ToEditor(property, culture, segment) switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            var editorValue => JsonNode.Parse(jsonSerializer.Serialize(editorValue)),
        };
    }

    /// <summary>
    /// Gets the property's value in editor format, in the CLR shape a <c>PropertyValueModel.Value</c>
    /// arriving through the Management API would have.
    /// </summary>
    public object? GetEditorValue(IProperty property, string? culture, string? segment)
        => GetEditorValueNode(property, culture, segment) is { } node
            ? ContentPropertyValueOperationHelper.NormalizeIncomingValue(node.Deserialize<JsonElement>())
            : null;
}
