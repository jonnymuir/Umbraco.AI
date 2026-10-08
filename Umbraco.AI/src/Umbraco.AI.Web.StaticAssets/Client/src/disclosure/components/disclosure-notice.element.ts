import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UAI_DISCLOSURE_CONTEXT } from "../disclosure.context.js";

/**
 * The "Responses are AI-generated" notice, shown next to AI output.
 *
 * Follows the global AI Disclosure Notice setting: always shown, dismissible (per location,
 * remembered per browser) or never shown. Renders nothing while hidden, so hosts can place
 * it unconditionally.
 *
 * Styling hooks for hosts:
 * - `--uai-disclosure-notice-background` (default: transparent)
 * - `--uai-disclosure-notice-padding` (default: 0)
 * - `--uai-disclosure-notice-border-radius` (default: 0)
 *
 * @example
 * ```html
 * <uai-disclosure-notice location="chat"></uai-disclosure-notice>
 * ```
 */
@customElement("uai-disclosure-notice")
export class UaiDisclosureNoticeElement extends UmbLitElement {
    /**
     * A short key for where the notice is shown, e.g. "chat" or "prompt" (letters, digits, dashes).
     * Dismissing the notice only hides it at this location.
     */
    @property({ type: String })
    location = "";

    /** Reflected so hosts can style around the notice, and so it takes no space while hidden. */
    @property({ type: Boolean, reflect: true })
    visible = false;

    @state()
    private _canDismiss = false;

    #disclosureContext?: typeof UAI_DISCLOSURE_CONTEXT.TYPE;

    constructor() {
        super();
        this.consumeContext(UAI_DISCLOSURE_CONTEXT, (context) => {
            this.#disclosureContext = context;
            this.#observeNotice();
            this.observe(context?.canDismiss, (canDismiss) => (this._canDismiss = canDismiss ?? false), "_canDismiss");
        });
    }

    override updated(changed: Map<PropertyKey, unknown>): void {
        super.updated(changed);
        if (changed.has("location")) {
            this.#observeNotice();
        }
    }

    #observeNotice(): void {
        this.observe(
            this.#disclosureContext?.showNoticeFor(this.location),
            (show) => (this.visible = show ?? false),
            "_visible",
        );
    }

    #onDismiss(): void {
        this.#disclosureContext?.dismiss(this.location);
    }

    override render() {
        if (!this.visible) return nothing;

        return html`
            <uui-icon name="icon-info"></uui-icon>
            <span>
                ${this.localize.termOrDefault("uaiDisclosure_notice", "Responses are AI-generated and may be inaccurate.")}
            </span>
            ${this._canDismiss
                ? html`<uui-button
                      compact
                      look="default"
                      label=${this.localize.termOrDefault("uaiDisclosure_dismiss", "Dismiss")}
                      @click=${this.#onDismiss}
                  >
                      <uui-icon name="icon-wrong"></uui-icon>
                  </uui-button>`
                : nothing}
        `;
    }

    static override styles = css`
        :host {
            display: none;
        }

        :host([visible]) {
            display: flex;
            align-items: center;
            gap: var(--uui-size-space-2);
            width: fit-content;
            max-width: 100%;
            box-sizing: border-box;
            padding: var(--uai-disclosure-notice-padding, 0);
            border-radius: var(--uai-disclosure-notice-border-radius, 0);
            background: var(--uai-disclosure-notice-background, transparent);
            font-size: var(--uui-type-small-size);
            color: var(--uui-color-text-alt);
        }

        uui-icon {
            flex-shrink: 0;
        }

        uui-button {
            --uui-button-height: 20px;
            --uui-button-padding-left-factor: 0.5;
            --uui-button-padding-right-factor: 0.5;
            margin: calc(var(--uui-size-space-1) * -1) 0;
            font-size: var(--uui-type-small-size);
        }
    `;
}

export default UaiDisclosureNoticeElement;

declare global {
    interface HTMLElementTagNameMap {
        "uai-disclosure-notice": UaiDisclosureNoticeElement;
    }
}
