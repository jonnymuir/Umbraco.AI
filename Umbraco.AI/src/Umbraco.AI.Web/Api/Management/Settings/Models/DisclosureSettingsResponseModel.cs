using System.ComponentModel.DataAnnotations;

namespace Umbraco.AI.Web.Api.Management.Settings.Models;

/// <summary>
/// Response model for the AI disclosure settings that every backoffice user needs to read.
/// </summary>
public class DisclosureSettingsResponseModel
{
    /// <summary>
    /// How the AI-generated disclosure notice is shown (Always, Dismissible, Off).
    /// </summary>
    [Required]
    public string NoticeMode { get; set; } = "Always";
}
