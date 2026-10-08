import { describe, expect, it } from "vitest";
import type { MessageResponseModel } from "../api/types.gen.js";
import { toDisplayMessages, UAI_RESTORED_RESULT_UNAVAILABLE } from "./message-mapper.js";

let sequence = 0;

/** A stored row whose contentJson is a serialized M.E.AI ChatMessage, as the server persists it. */
function row(role: string, contents: unknown[], contentText: string | null = null): MessageResponseModel {
    sequence += 1;
    return {
        id: `m${sequence}`,
        sequence,
        role,
        contentJson: JSON.stringify({ role, contents }),
        contentText,
        dateCreated: "2026-09-30T12:50:48Z",
    };
}

const text = (value: string) => ({ $type: "text", text: value });
const call = (callId: string, name: string, args: unknown) => ({ $type: "functionCall", callId, name, arguments: args });
const result = (callId: string, value: unknown) => ({ $type: "functionResult", callId, result: value });
const approvalRequest = (callId: string, name: string, args: unknown) => ({
    $type: "toolApprovalRequest",
    toolCall: { $type: "functionCall", callId, name, arguments: args },
});
const approvalResponse = (callId: string, name: string, approved: boolean) => ({
    $type: "toolApprovalResponse",
    approved,
    toolCall: { $type: "functionCall", callId, name, arguments: {} },
});

describe("toDisplayMessages", () => {
    it("keeps a turn's tool calls together, with the answer after them, as a live run leaves them", () => {
        const messages = toDisplayMessages([
            row("user", [text("Rename the About page")], "Rename the About page"),
            row("assistant", [call("c1", "search_umbraco", { query: "About" })]),
            row("tool", [result("c1", { success: true })]),
            row("assistant", [call("c2", "update_umbraco_content", { name: "About us" })]),
            row("tool", [result("c2", { success: true })]),
            row("assistant", [text("Done.")], "Done."),
        ]);

        // Live, tool calls join the current message and text after them starts a new one.
        const assistant = messages.filter((m) => m.role === "assistant");
        expect(assistant).toHaveLength(2);
        expect(assistant[0].content).toBe("");
        expect(assistant[0].toolCalls?.map((c) => [c.name, c.status])).toEqual([
            ["search_umbraco", "completed"],
            ["update_umbraco_content", "completed"],
        ]);
        expect(assistant[1].content).toBe("Done.");
        expect(assistant[1].toolCalls).toBeUndefined();
    });

    it("gives custom tool views the same args and result the live stream would", () => {
        const args = { query: "About", type: "content" };
        const value = { success: true, results: [{ id: "704a66de", name: "About" }] };

        const [, assistant] = toDisplayMessages([
            row("user", [text("Find About")], "Find About"),
            row("assistant", [call("c1", "search_umbraco", args)]),
            row("tool", [result("c1", value)]),
        ]);

        // The tool renderer JSON-parses both strings before handing them to the view.
        const restored = assistant.toolCalls![0];
        expect(JSON.parse(restored.arguments)).toEqual(args);
        expect(JSON.parse(restored.result!)).toEqual(value);
    });

    it("pairs every restored tool call with a hidden tool message so none is re-synthesised", () => {
        const messages = toDisplayMessages([
            row("user", [text("Go")], "Go"),
            row("assistant", [call("c1", "search_umbraco", {}), call("c2", "get_umbraco_content", {})]),
            row("tool", [result("c1", "ok")]),
        ]);

        const callIds = messages.find((m) => m.role === "assistant")!.toolCalls!.map((c) => c.id);
        const toolMessageIds = messages.filter((m) => m.role === "tool").map((m) => m.toolCallId);
        expect(toolMessageIds).toEqual(callIds);
    });

    it("marks a call that never got a result as unavailable rather than leaving it pending", () => {
        const [, assistant] = toDisplayMessages([
            row("user", [text("Go")], "Go"),
            row("assistant", [call("c1", "search_umbraco", {})]),
        ]);

        expect(assistant.toolCalls![0]).toMatchObject({ status: "error", result: UAI_RESTORED_RESULT_UNAVAILABLE });
    });

    it("shows an approved call once, completed, and hides the approval answer", () => {
        const messages = toDisplayMessages([
            row("user", [text("Publish it")], "Publish it"),
            row("assistant", [approvalRequest("c1", "publish_umbraco_content", { key: "k" })]),
            row("user", [approvalResponse("c1", "publish_umbraco_content", true)]),
            row("assistant", [call("c1", "publish_umbraco_content", { key: "k" })]),
            row("tool", [result("c1", { success: true })]),
            row("assistant", [text("Published.")], "Published."),
        ]);

        expect(messages.filter((m) => m.role === "user")).toHaveLength(1);
        const [withCall, answer] = messages.filter((m) => m.role === "assistant");
        expect(withCall.toolCalls).toHaveLength(1);
        expect(withCall.toolCalls![0]).toMatchObject({ name: "publish_umbraco_content", status: "completed" });
        expect(answer.content).toBe("Published.");
    });

    it("shows a denied call as an error, matching the live chip", () => {
        const [, assistant] = toDisplayMessages([
            row("user", [text("Publish it")], "Publish it"),
            row("assistant", [approvalRequest("c1", "publish_umbraco_content", {})]),
            row("user", [approvalResponse("c1", "publish_umbraco_content", false)]),
            row("assistant", [call("c1", "publish_umbraco_content", {})]),
            row("tool", [result("c1", "Tool call invocation rejected.")]),
        ]);

        expect(assistant.toolCalls![0].status).toBe("error");
    });

    it("shows a stored provider error inline, as the live stream did", () => {
        const messages = toDisplayMessages([
            row("user", [text("Go")], "Go"),
            row("assistant", [call("c1", "search_umbraco", {})]),
            row("tool", [
                result("c1", "ok"),
                { $type: "error", errorCode: "rate_limit_exceeded", message: "Rate limit reached" },
            ]),
        ]);

        const [withCall, error] = messages.filter((m) => m.role === "assistant");
        expect(withCall.toolCalls![0].status).toBe("completed");
        expect(error.content).toContain("[Provider error rate_limit_exceeded: Rate limit reached]");
    });

    it("starts a new assistant message for each user turn", () => {
        const messages = toDisplayMessages([
            row("user", [text("One")], "One"),
            row("assistant", [text("First")], "First"),
            row("user", [text("Two")], "Two"),
            row("assistant", [text("Second")], "Second"),
        ]);

        expect(messages.map((m) => [m.role, m.content])).toEqual([
            ["user", "One"],
            ["assistant", "First"],
            ["user", "Two"],
            ["assistant", "Second"],
        ]);
    });

    it("falls back to plain text for rows that aren't a stored ChatMessage", () => {
        const messages = toDisplayMessages([
            { id: "u", sequence: 1, role: "user", contentJson: "not json", contentText: "Hello", dateCreated: "2026-01-01" },
            { id: "a", sequence: 2, role: "assistant", contentJson: "{}", contentText: "Hi there", dateCreated: "2026-01-01" },
            { id: "s", sequence: 3, role: "system", contentJson: "{}", contentText: "system prompt", dateCreated: "2026-01-01" },
        ]);

        expect(messages.map((m) => [m.role, m.content])).toEqual([
            ["user", "Hello"],
            ["assistant", "Hi there"],
        ]);
    });
});
