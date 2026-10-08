namespace Umbraco.AI.Core.Tools;

/// <summary>
/// Attribute to mark AI tool implementations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AIToolAttribute : Attribute
{
    /// <summary>
    /// Gets the unique identifier of the AI tool.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display name of the AI tool.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the scope identifier for permission and grouping purposes.
    /// </summary>
    /// <remarks>
    /// Examples: "content-read", "content-write", "media-read", "search"
    /// Defaults to "general" if not specified.
    /// </remarks>
    public string ScopeId { get; set; } = "general";

    /// <summary>
    /// Gets or sets whether the tool performs destructive operations.
    /// </summary>
    public bool IsDestructive { get; set; }

    private bool? _requiresApproval;

    /// <summary>
    /// Gets or sets whether a call to the tool must be approved by a human before it runs on an
    /// interactive surface. Defaults to <see cref="IsDestructive"/> when not set explicitly.
    /// </summary>
    /// <remarks>
    /// Set this to <c>false</c> on a destructive tool whose effect an editor can undo themselves (e.g.
    /// saving a draft, which version history can roll back). The tool stays destructive, so it is still
    /// withheld from contextual surfaces and denied on non-interactive runs; it just no longer
    /// interrupts an interactive run for approval. Anything the public sees change (publish, unpublish)
    /// or that removes content should keep requiring approval.
    /// <para>
    /// Setting this to <c>true</c> on a tool that isn't <see cref="IsDestructive"/> has no effect: only
    /// destructive tools are ever gated for approval, so a tool that should be approved must also be
    /// marked destructive.
    /// </para>
    /// </remarks>
    public bool RequiresApproval
    {
        get => _requiresApproval ?? IsDestructive;
        set => _requiresApproval = value;
    }

    /// <summary>
    /// Gets or sets tags for additional categorization.
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolAttribute"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the tool.</param>
    /// <param name="name">The display name of the tool.</param>
    public AIToolAttribute(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
