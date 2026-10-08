using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Umbraco.AI.Core.Settings;
using Umbraco.AI.Web.Api.Management.Settings.Models;

namespace Umbraco.AI.Web.Api.Management.Settings.Controllers;

/// <summary>
/// Controller to get the AI disclosure settings.
/// </summary>
/// <remarks>
/// Unlike <see cref="GetSettingsController"/>, this does not require AI section access: every
/// backoffice user who sees AI output (chat, prompts) needs to know whether to show the notice.
/// It only exposes the disclosure setting, never the rest of the AI settings.
/// </remarks>
[ApiVersion("1.0")]
public class GetDisclosureSettingsController : SettingsControllerBase
{
    private readonly IAISettingsService _settingsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetDisclosureSettingsController"/> class.
    /// </summary>
    public GetDisclosureSettingsController(IAISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Get the AI disclosure settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AI disclosure settings.</returns>
    [HttpGet("disclosure")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(DisclosureSettingsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDisclosureSettings(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        return Ok(new DisclosureSettingsResponseModel { NoticeMode = settings.DisclosureNoticeMode.ToString() });
    }
}
