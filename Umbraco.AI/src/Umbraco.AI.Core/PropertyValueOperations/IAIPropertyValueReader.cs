using System.Text.Json.Nodes;

namespace Umbraco.AI.Core.PropertyValueOperations;

/// <summary>
/// Reads the value at a property value path without changing it, using the same descent rules as
/// <see cref="IAIPropertyValueDispatcher"/>. Used by the content value tools to check what was
/// actually stored after a save.
/// </summary>
internal interface IAIPropertyValueReader
{
    /// <summary>
    /// Walks <see cref="AIPropertyValueDispatchRequest.Path"/> from
    /// <see cref="AIPropertyValueDispatchRequest.RootValue"/> and returns the leaf value.
    /// <see cref="AIPropertyValueDispatchRequest.Operation"/> and its args are ignored.
    /// </summary>
    /// <param name="request">The request describing the path, root value and document.</param>
    /// <param name="value">The leaf value, or <c>null</c> when it is empty or the path could not be walked.</param>
    /// <returns><c>true</c> when the path could be walked to the leaf; otherwise <c>false</c>.</returns>
    bool TryReadValue(AIPropertyValueDispatchRequest request, out JsonNode? value);
}
