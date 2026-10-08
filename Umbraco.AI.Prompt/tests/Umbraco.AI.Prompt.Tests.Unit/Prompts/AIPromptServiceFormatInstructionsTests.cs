using Microsoft.Extensions.AI;
using Shouldly;
using Umbraco.AI.Prompt.Core.Prompts;
using Xunit;

namespace Umbraco.AI.Prompt.Tests.Unit.Prompts;

public class AIPromptServiceFormatInstructionsTests
{
    [Fact]
    public void ReplaceFormatInstructions_WithoutContext_KeepsUserPrompt()
    {
        var format = new ChatMessage(ChatRole.System, "old format");
        List<ChatMessage> messages = [new(ChatRole.User, "Write a title"), format];

        AIPromptService.ReplaceFormatInstructions(messages, format, "new format");

        messages.Select(m => (m.Role, m.Text)).ShouldBe(
        [
            (ChatRole.User, "Write a title"),
            (ChatRole.System, "new format"),
        ]);
    }

    [Fact]
    public void ReplaceFormatInstructions_WithContext_KeepsContextMessage()
    {
        var format = new ChatMessage(ChatRole.System, "old format");
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "entity context"),
            new(ChatRole.User, "Write a title"),
            format,
        ];

        AIPromptService.ReplaceFormatInstructions(messages, format, "new format");

        messages.Select(m => (m.Role, m.Text)).ShouldBe(
        [
            (ChatRole.System, "entity context"),
            (ChatRole.User, "Write a title"),
            (ChatRole.System, "new format"),
        ]);
    }

    [Fact]
    public void ReplaceFormatInstructions_FormatNotLast_ReplacesItInPlace()
    {
        var format = new ChatMessage(ChatRole.System, "old format");
        List<ChatMessage> messages =
        [
            format,
            new(ChatRole.User, "Write a title"),
            new(ChatRole.System, "something added later"),
        ];

        AIPromptService.ReplaceFormatInstructions(messages, format, "new format");

        messages.Select(m => m.Text).ShouldBe(["new format", "Write a title", "something added later"]);
    }

    [Fact]
    public void ReplaceFormatInstructions_CalledTwiceWithReturnedMessage_ReplacesPreviousRetry()
    {
        var format = new ChatMessage(ChatRole.System, "old format");
        List<ChatMessage> messages = [new(ChatRole.User, "Write a title"), format];

        var retry1 = AIPromptService.ReplaceFormatInstructions(messages, format, "retry 1");
        AIPromptService.ReplaceFormatInstructions(messages, retry1, "retry 2");

        messages.Select(m => m.Text).ShouldBe(["Write a title", "retry 2"]);
    }

    [Fact]
    public void ReplaceFormatInstructions_NoCurrentMessage_AppendsInstructions()
    {
        List<ChatMessage> messages = [new(ChatRole.User, "Write a title")];

        AIPromptService.ReplaceFormatInstructions(messages, null, "new format");

        messages.Select(m => m.Text).ShouldBe(["Write a title", "new format"]);
    }
}
