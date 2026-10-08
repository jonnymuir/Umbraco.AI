namespace Umbraco.AI.Core.Settings;

/// <summary>
/// Controls how the backoffice shows the notice that tells users a response is AI-generated.
/// </summary>
public enum AIDisclosureNoticeMode
{
    /// <summary>
    /// The notice is always shown.
    /// </summary>
    Always = 0,

    /// <summary>
    /// The notice is shown until the user dismisses it. The dismissal is remembered per browser.
    /// </summary>
    Dismissible = 1,

    /// <summary>
    /// The notice is never shown.
    /// </summary>
    Off = 2,
}
