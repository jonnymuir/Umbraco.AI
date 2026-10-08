/**
 * Disclosure Context
 *
 * Global context that tells AI surfaces (chat, prompt previews) whether to show the
 * "Responses are AI-generated" notice, based on the global AI setting, and remembers a
 * per-browser dismissal when the setting allows it.
 *
 * Dismissals are per location (e.g. "chat", "prompt"): hiding the notice in one place never
 * hides it in another, so each AI surface discloses at least once.
 *
 * Auto-provided at the backoffice root via the globalContext manifest.
 *
 * @example
 * ```typescript
 * import { UAI_DISCLOSURE_CONTEXT } from '@umbraco-ai/core';
 *
 * this.consumeContext(UAI_DISCLOSURE_CONTEXT, (context) => {
 *   this.observe(context?.showNoticeFor("chat"), (show) => (this._showNotice = show ?? false));
 *   this.observe(context?.canDismiss, (canDismiss) => (this._canDismiss = canDismiss ?? false));
 * });
 *
 * // On the dismiss button:
 * this.#disclosureContext?.dismiss("chat");
 * ```
 */

import { UmbControllerBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { Observable } from "@umbraco-cms/backoffice/external/rxjs";
import { UmbBasicState, createObservablePart, mergeObservables } from "@umbraco-cms/backoffice/observable-api";
import { SettingsService } from "../api/sdk.gen.js";
import { coreClientReady } from "../client-ready.js";
import type { UaiDisclosureNoticeMode } from "./types.js";

const DISMISSED_COOKIE_PREFIX = "umbAIDisclosureNoticeDismissed_";
const DISMISSED_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

function isKnownMode(value: unknown): value is UaiDisclosureNoticeMode {
    return value === "Always" || value === "Dismissible" || value === "Off";
}

// Locations become part of a cookie name, so keep them to a safe, predictable shape.
const LOCATION_PATTERN = /^[a-z0-9-]+$/i;

function readDismissedLocations(): string[] {
    try {
        return document.cookie
            .split("; ")
            .filter((cookie) => cookie.startsWith(DISMISSED_COOKIE_PREFIX) && cookie.endsWith("=1"))
            .map((cookie) => cookie.slice(DISMISSED_COOKIE_PREFIX.length, -"=1".length))
            .filter((location) => LOCATION_PATTERN.test(location));
    } catch {
        return [];
    }
}

function writeDismissedCookie(location: string): void {
    try {
        const secure = window.location.protocol === "https:" ? "; Secure" : "";
        document.cookie = `${DISMISSED_COOKIE_PREFIX}${location}=1; Max-Age=${DISMISSED_MAX_AGE_SECONDS}; Path=/; SameSite=Strict${secure}`;
    } catch {
        // Cookies blocked: the dismissal still holds for this page load via state.
    }
}

/**
 * Global context providing the AI disclosure notice state.
 * Registered as a globalContext manifest - auto-instantiated at backoffice root.
 */
export class UaiDisclosureContext extends UmbControllerBase {
    /** Type guard marker for context resolution. */
    public readonly IS_DISCLOSURE_CONTEXT = true;

    // Undefined until loaded, so nothing flashes up and then disappears when the mode is Off.
    readonly #mode = new UmbBasicState<UaiDisclosureNoticeMode | undefined>(undefined);
    readonly #dismissedLocations = new UmbBasicState<ReadonlyArray<string>>(readDismissedLocations());

    /** The configured notice mode, or undefined while loading. */
    readonly mode = this.#mode.asObservable();

    /** Whether the notice can be dismissed by the user. */
    readonly canDismiss = createObservablePart(this.#mode.asObservable(), (mode) => mode === "Dismissible");

    /**
     * Whether the notice should be shown right now at the given location.
     * @param location A short key for the AI surface, e.g. "chat" or "prompt".
     */
    showNoticeFor(location: string): Observable<boolean> {
        return mergeObservables(
            [this.#mode.asObservable(), this.#dismissedLocations.asObservable()],
            ([mode, dismissed]) => mode === "Always" || (mode === "Dismissible" && !dismissed.includes(location)),
        );
    }

    constructor(host: UmbControllerHost) {
        super(host);
        this.provideContext(UAI_DISCLOSURE_CONTEXT, this);
        void this.#load();
    }

    async #load(): Promise<void> {
        await coreClientReady;
        try {
            const { data } = await SettingsService.getDisclosureSettings();
            // Anything unexpected falls back to Always: a failure must never hide the notice.
            this.#mode.setValue(isKnownMode(data?.noticeMode) ? data.noticeMode : "Always");
        } catch {
            this.#mode.setValue("Always");
        }
    }

    /**
     * Updates the mode without a reload, e.g. straight after the AI settings are saved.
     */
    setMode(mode: UaiDisclosureNoticeMode): void {
        this.#mode.setValue(isKnownMode(mode) ? mode : "Always");
    }

    /**
     * Hides the notice at the given location for this browser. Other locations are unaffected.
     * Only has an effect when the mode is Dismissible.
     * @param location A short key for the AI surface, e.g. "chat" or "prompt" (letters, digits, dashes).
     */
    dismiss(location: string): void {
        if (this.#mode.getValue() !== "Dismissible" || !LOCATION_PATTERN.test(location)) return;
        writeDismissedCookie(location);
        const dismissed = this.#dismissedLocations.getValue();
        if (!dismissed.includes(location)) {
            this.#dismissedLocations.setValue([...dismissed, location]);
        }
    }
}

export default UaiDisclosureContext;

export const UAI_DISCLOSURE_CONTEXT = new UmbContextToken<UaiDisclosureContext>("UaiDisclosureContext");
