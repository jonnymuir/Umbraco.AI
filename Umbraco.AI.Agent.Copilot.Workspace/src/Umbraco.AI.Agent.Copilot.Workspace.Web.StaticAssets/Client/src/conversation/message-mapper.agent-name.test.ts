// S8 - Workspace remembers which agent answered (AC4 mapping logic, AC5 legacy rows)
//
// Assumed seam: `toDisplayMessages(messages, agentNames)` takes an optional
// `ReadonlyMap<string, string>` of agent id -> name (built from the agent list Workspace already loads
// for its picker), and each stored row carries `agentId` (string | null) once the client is regenerated.
import { describe, expect, it } from "vitest";
import type { MessageResponseModel } from "../api/types.gen.js";
import { toDisplayMessages } from "./message-mapper.js";

const AGENT_A = "aaaaaaaa-0000-0000-0000-000000000001";

let sequence = 0;

function row(role: string, value: string, agentId: string | null = null): MessageResponseModel {
    sequence += 1;
    return {
        id: `m${sequence}`,
        sequence,
        role,
        contentJson: JSON.stringify({ role, contents: [{ $type: "text", text: value }] }),
        contentText: value,
        dateCreated: "2026-10-01T12:00:00Z",
        agentId,
    } as MessageResponseModel;
}

/** A stored row whose contentJson is a serialized M.E.AI ChatMessage, as the server persists it. */
function contentsRow(role: string, contents: unknown[], agentId: string | null = null): MessageResponseModel {
    sequence += 1;
    return {
        id: `m${sequence}`,
        sequence,
        role,
        contentJson: JSON.stringify({ role, contents }),
        contentText: null,
        dateCreated: "2026-10-01T12:00:00Z",
        agentId,
    } as MessageResponseModel;
}

const text = (value: string) => ({ $type: "text", text: value });
const call = (callId: string, name: string, args: unknown) => ({ $type: "functionCall", callId, name, arguments: args });
const result = (callId: string, value: unknown) => ({ $type: "functionResult", callId, result: value });

const agentNames = new Map<string, string>([[AGENT_A, "Agent A"]]);

function assistantOf(messages: MessageResponseModel[]) {
    return toDisplayMessages(messages, agentNames).find((m) => m.role === "assistant");
}

describe("toDisplayMessages agent names", () => {
    it("shows the agent's name on a reply with a known agent id", () => {
        const assistant = assistantOf([row("user", "Hi"), row("assistant", "Hello", AGENT_A)]);
        expect(assistant?.agentName).toBe("Agent A");
    });

    it("shows no name on a legacy reply with no agent id", () => {
        const assistant = assistantOf([row("user", "Hi"), row("assistant", "Hello", null)]);
        expect(assistant?.agentName).toBeUndefined();
    });

    it("shows no name when the agent id is unknown to the client", () => {
        const assistant = assistantOf([row("user", "Hi"), row("assistant", "Hello", "cccccccc-0000-0000-0000-000000000003")]);
        expect(assistant?.agentName).toBeUndefined();
    });

    // AC4 — a turn's tool calls join one message segment and text after them starts a new one (see
    // `message-mapper.test.ts`); the agent name must survive onto that later, post-tool-call segment too.
    it("keeps the agent's name on the assistant text that follows a tool-call segment", () => {
        const messages = toDisplayMessages(
            [
                row("user", "Rename the About page"),
                contentsRow("assistant", [call("c1", "search_umbraco", { query: "About" })], AGENT_A),
                contentsRow("tool", [result("c1", { success: true })]),
                contentsRow("assistant", [text("Done.")], AGENT_A),
            ],
            agentNames,
        );

        const textReply = messages.find((m) => m.role === "assistant" && m.content === "Done.");
        expect(textReply?.agentName).toBe("Agent A");
    });
});
