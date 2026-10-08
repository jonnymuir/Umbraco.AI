using System.Reflection;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Conversations.Core.Conversations;
using Umbraco.AI.Agent.Copilot.Workspace.Core.Surfaces;
using Umbraco.AI.Agent.Copilot.Workspace.Web.NotificationHandlers;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.Cms.Core.Events;
using Xunit;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Copilot.Workspace.Tests.Unit.NotificationHandlers;

/// <summary>
/// Tests for <see cref="CopilotWorkspaceConversationAgentSavingNotificationHandler"/> - the guard that
/// stops a conversation being saved with a named agent that can't run in Copilot Workspace.
/// </summary>
public class CopilotWorkspaceConversationAgentSavingNotificationHandlerTests
{
    private static readonly Guid ConversationId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public async Task HandleAsync_WithAutoOrNoAgent_DoesNotCancel(string? agentIdOrAlias)
    {
        // Arrange
        var harness = new Harness();

        // Act
        var notification = await harness.SaveAsync(agentIdOrAlias);

        // Assert
        notification.Cancel.ShouldBeFalse();
        harness.AgentService.Verify(
            x => x.GetAgentByAliasAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithAnOptedInAgentById_DoesNotCancel()
    {
        // Arrange
        var agent = CreateAgent("workspace-agent");
        var harness = new Harness(agent);

        // Act
        var notification = await harness.SaveAsync(agent.Id.ToString());

        // Assert
        notification.Cancel.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithAnOptedInAgentByAlias_DoesNotCancel()
    {
        // Arrange
        var agent = CreateAgent("workspace-agent");
        var harness = new Harness(agent);

        // Act
        var notification = await harness.SaveAsync("workspace-agent");

        // Assert
        notification.Cancel.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithAMissingAgent_CancelsWithAReason()
    {
        // Arrange
        var harness = new Harness();

        // Act
        var notification = await harness.SaveAsync(Guid.NewGuid().ToString());

        // Assert
        notification.Cancel.ShouldBeTrue();
        notification.Messages.GetAll().ShouldContain(m => m.Message.Contains("not available"));
    }

    [Fact]
    public async Task HandleAsync_WithAnInactiveAgent_Cancels()
    {
        // Arrange
        var agent = CreateAgent("inactive-agent", isActive: false);
        var harness = new Harness(agent);

        // Act
        var notification = await harness.SaveAsync("inactive-agent");

        // Assert
        notification.Cancel.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithAnAgentNotOptedInToWorkspace_Cancels()
    {
        // Arrange
        var agent = CreateAgent("sidebar-only-agent", surfaceIds: ["copilot"]);
        var harness = new Harness(agent);

        // Act
        var notification = await harness.SaveAsync(agent.Id.ToString());

        // Assert
        notification.Cancel.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithAnAgentOnNoSurface_Cancels()
    {
        // Arrange
        var agent = CreateAgent("no-surface-agent", surfaceIds: []);
        var harness = new Harness(agent);

        // Act
        var notification = await harness.SaveAsync("no-surface-agent");

        // Assert
        notification.Cancel.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_ExistingConversationKeepingAnAgentThatBecameUnavailable_DoesNotCancel()
    {
        // Arrange
        // The agent was fine when picked and has since been opted out. A rename/pin/truncate re-saves the
        // same value; the stream already falls back to auto for it, so the save must not be blocked.
        var agent = CreateAgent("opted-out-later", surfaceIds: []);
        var harness = new Harness(agent, storedAgentIdOrAlias: "opted-out-later");

        // Act
        var notification = await harness.SaveAsync("opted-out-later");

        // Assert
        notification.Cancel.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_ExistingConversationSwitchingToAnUnavailableAgent_Cancels()
    {
        // Arrange
        var agent = CreateAgent("sidebar-only-agent", surfaceIds: ["copilot"]);
        var harness = new Harness(agent, storedAgentIdOrAlias: "auto");

        // Act
        var notification = await harness.SaveAsync("sidebar-only-agent");

        // Assert
        notification.Cancel.ShouldBeTrue();
    }

    // AIAgent.Id has an internal setter (Umbraco.AI.Agent.Core doesn't grant this assembly
    // InternalsVisibleTo), so it's set via reflection - same workaround as the stream tests.
    private static UmbracoAIAgent CreateAgent(
        string alias,
        bool isActive = true,
        IReadOnlyList<string>? surfaceIds = null)
    {
        var agent = new UmbracoAIAgent
        {
            Alias = alias,
            Name = alias,
            AgentType = AIAgentType.Standard,
            IsActive = isActive,
            SurfaceIds = surfaceIds ?? [CopilotWorkspaceAgentSurface.SurfaceId],
        };

        typeof(UmbracoAIAgent).GetProperty(nameof(UmbracoAIAgent.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(agent, Guid.NewGuid());

        return agent;
    }

    private sealed class Harness
    {
        private readonly CopilotWorkspaceConversationAgentSavingNotificationHandler _handler;

        public Harness(UmbracoAIAgent? agent = null, string? storedAgentIdOrAlias = null)
        {
            if (agent is not null)
            {
                AgentService
                    .Setup(x => x.GetAgentAsync(agent.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(agent);
                AgentService
                    .Setup(x => x.GetAgentByAliasAsync(agent.Alias, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(agent);
            }

            var conversationService = new Mock<IAIConversationService>();
            if (storedAgentIdOrAlias is not null)
            {
                conversationService
                    .Setup(x => x.GetConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new AIConversation { Id = ConversationId, AgentIdOrAlias = storedAgentIdOrAlias });
            }

            _handler = new CopilotWorkspaceConversationAgentSavingNotificationHandler(
                conversationService.Object,
                AgentService.Object,
                new AIAgentScopeValidator(),
                new AIAgentSurfaceCollection(() => [new CopilotWorkspaceAgentSurface()]));
        }

        public Mock<IAIAgentService> AgentService { get; } = new();

        public async Task<AIConversationSavingNotification> SaveAsync(string? agentIdOrAlias)
        {
            var notification = new AIConversationSavingNotification(
                new AIConversation { Id = ConversationId, AgentIdOrAlias = agentIdOrAlias },
                new EventMessages());

            await _handler.HandleAsync(notification, CancellationToken.None);

            return notification;
        }
    }
}
