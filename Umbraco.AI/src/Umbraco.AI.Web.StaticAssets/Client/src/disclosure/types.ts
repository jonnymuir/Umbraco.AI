/**
 * How the backoffice shows the notice that tells users a response is AI-generated.
 *
 * - `Always`: always shown.
 * - `Dismissible`: shown until the user dismisses it; the dismissal is remembered per browser.
 * - `Off`: never shown.
 */
export type UaiDisclosureNoticeMode = "Always" | "Dismissible" | "Off";
