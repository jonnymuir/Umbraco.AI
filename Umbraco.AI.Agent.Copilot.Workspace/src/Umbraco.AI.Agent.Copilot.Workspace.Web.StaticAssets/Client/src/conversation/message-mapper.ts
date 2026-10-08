import { safeParseJson, type UaiChatMessage, type UaiToolCallInfo } from "@umbraco-ai/agent-ui";
import type { MessageResponseModel } from "../api/types.gen.js";

/**
 * Shown for a restored tool call that never got a result (e.g. the run was stopped mid-call). Same
 * wording as the run controller's orphaned-tool-call repair (`#sanitizeOrphanedToolCalls`).
 */
export const UAI_RESTORED_RESULT_UNAVAILABLE = "Result unavailable (conversation was restored).";

/**
 * The parts of a persisted M.E.AI `ChatMessage` content item (`contentJson` is the serialized
 * `ChatMessage`, discriminated by `$type`) that the display needs.
 */
interface StoredContent {
    $type?: string;
    text?: string;
    // functionCall
    callId?: string;
    name?: string;
    arguments?: unknown;
    // functionResult
    result?: unknown;
    // toolApprovalRequest / toolApprovalResponse
    toolCall?: { callId?: string; name?: string; arguments?: unknown };
    approved?: boolean;
    // error
    message?: string;
    errorCode?: string;
}

interface Segment {
    message: UaiChatMessage;
    calls: UaiToolCallInfo[];
}

/**
 * Maps persisted messages into the chat UI's `UaiChatMessage` shape for **display only**. This seeds
 * the thread when a conversation opens; it does NOT feed the model — on each turn the server supplies
 * the authoritative history from its durable store (the client transmits only the new turn).
 *
 * The result mirrors what a live run leaves behind, so a reopened conversation looks the same as it
 * did while it ran and tool renderers (including custom tool views) get the same inputs:
 * - assistant messages split the way the live run controller splits them: tool calls keep joining the
 *   current message (so a turn's chips sit together), and text that arrives after tool calls starts
 *   a new message. Stored rows are split per model call, so they're regrouped here rather than shown
 *   one row per message;
 * - each tool call's `arguments` / `result` are the JSON serialisation of the stored values, which is
 *   exactly what the live AG-UI stream sends (`AGUIEventEmitter` serialises the same objects);
 * - a hidden tool-role message after the assistant message for every tool call. The run controller
 *   repairs any tool call without one by appending a synthesized result, which would both show a
 *   bogus "unavailable" chip and shift the strategy's persisted-message count — so every call gets one.
 *
 * Rows that can't be read as a stored `ChatMessage` fall back to their plain text.
 *
 * `agentNames` resolves a known assistant message's `agentId` to the agent's display name (built by
 * the caller from the agent list it already loads for the picker). An unset, null, or unresolvable
 * agent id leaves the display message's `agentName` unset — no name is shown.
 */
export function toDisplayMessages(
    messages: readonly MessageResponseModel[],
    agentNames: ReadonlyMap<string, string> = new Map(),
): UaiChatMessage[] {
    const display: UaiChatMessage[] = [];
    const deniedCallIds = new Set<string>();
    // Every call in the current user turn, by id — an approved call is stored twice (the approval
    // request, then the call itself) and a result can arrive in a later row than its call.
    let turnCalls = new Map<string, UaiToolCallInfo>();
    let segment: Segment | undefined;

    const flushSegment = () => {
        if (!segment) return;
        const { message, calls } = segment;
        segment = undefined;

        for (const call of calls) {
            if (call.result === undefined) {
                call.result = UAI_RESTORED_RESULT_UNAVAILABLE;
                call.status = "error";
            }
        }

        if (!message.content.trim() && calls.length === 0) return;

        if (calls.length > 0) message.toolCalls = calls;
        display.push(message);
        for (const call of calls) {
            display.push({
                id: `${message.id}:${call.id}`,
                role: "tool",
                content: call.result!,
                toolCallId: call.id,
                timestamp: message.timestamp,
            });
        }
    };

    const segmentFor = (id: string, timestamp: Date, agentName: string | undefined): Segment =>
        (segment ??= { message: { id, role: "assistant", content: "", timestamp, agentName }, calls: [] });

    // Text after tool calls starts a new message, as it does live.
    const textSegmentFor = (id: string, timestamp: Date, agentName: string | undefined): Segment => {
        if (segment && segment.calls.length > 0) flushSegment();
        return segmentFor(id, timestamp, agentName);
    };

    for (const stored of messages) {
        const { role } = stored;
        if (role !== "user" && role !== "assistant" && role !== "tool") continue;

        const timestamp = new Date(stored.dateCreated);
        const contents = readContents(stored);
        // Unknown or null ids, and ids not in the caller's agent list, leave this unset — no name is shown.
        const agentName = role === "assistant" && stored.agentId ? agentNames.get(stored.agentId) : undefined;

        if (role === "user") {
            for (const content of contents) {
                if (content.$type === "toolApprovalResponse" && content.approved === false && content.toolCall?.callId) {
                    deniedCallIds.add(content.toolCall.callId);
                }
            }

            // An approval answer is stored as a user row with no text; it belongs to the turn it answers.
            const text = contents
                .filter((c) => c.$type === "text" && c.text)
                .map((c) => c.text)
                .join("");
            if (!text.trim()) continue;

            flushSegment();
            turnCalls = new Map();
            display.push({ id: stored.id, role: "user", content: text, timestamp });
            continue;
        }

        for (const content of contents) {
            switch (content.$type) {
                case "text":
                    if (role === "assistant" && content.text) {
                        textSegmentFor(stored.id, timestamp, agentName).message.content += content.text;
                    }
                    break;
                case "functionCall":
                case "toolApprovalRequest": {
                    const call = newCall(turnCalls, content.$type === "functionCall" ? content : content.toolCall);
                    if (call) segmentFor(stored.id, timestamp, agentName).calls.push(call);
                    break;
                }
                case "functionResult": {
                    const call = content.callId ? turnCalls.get(content.callId) : undefined;
                    if (call) {
                        call.result = JSON.stringify(content.result ?? null);
                        // A denied call keeps its error state, as it does live.
                        call.status = deniedCallIds.has(call.id) ? "error" : "completed";
                    }
                    break;
                }
                case "error":
                    textSegmentFor(stored.id, timestamp, agentName).message.content += formatProviderError(content);
                    break;
            }
        }
    }

    flushSegment();
    return display;
}

/** The row's stored contents, or its plain text as a single text item when it isn't a stored ChatMessage. */
function readContents(stored: MessageResponseModel): StoredContent[] {
    const contents = safeParseJson<{ contents?: unknown } | null>(stored.contentJson)?.contents;
    if (Array.isArray(contents)) return contents as StoredContent[];
    return stored.contentText ? [{ $type: "text", text: stored.contentText }] : [];
}

/** A display entry for a stored call, or undefined when the call is incomplete or already shown this turn. */
function newCall(
    turnCalls: Map<string, UaiToolCallInfo>,
    stored: { callId?: string; name?: string; arguments?: unknown } | undefined,
): UaiToolCallInfo | undefined {
    // An approved call is stored twice (the approval request, then the call itself) — show it once.
    if (!stored?.callId || !stored.name || turnCalls.has(stored.callId)) return undefined;
    const call: UaiToolCallInfo = {
        id: stored.callId,
        name: stored.name,
        arguments: JSON.stringify(stored.arguments ?? {}),
        status: "pending",
    };
    turnCalls.set(stored.callId, call);
    return call;
}

/** Matches the inline text the live stream emits for provider errors (AGUIStreamingService.FormatProviderErrorForChat). */
function formatProviderError(content: StoredContent): string {
    const code = content.errorCode ? ` ${content.errorCode}` : "";
    return `\n\n[Provider error${code}: ${content.message || "(no message)"}]\n\n`;
}
