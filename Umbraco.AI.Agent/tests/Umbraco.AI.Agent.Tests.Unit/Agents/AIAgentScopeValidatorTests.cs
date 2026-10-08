using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Surfaces;
using Xunit;

namespace Umbraco.AI.Agent.Tests.Unit.Agents;

/// <summary>
/// Verifies <see cref="AIAgentScopeValidator"/> availability semantics, with particular focus on
/// the broad/unscoped surface case (empty <see cref="IAIAgentSurface.SupportedScopeDimensions"/>,
/// e.g. the copilot-workspace surface) where dimension-based rules cannot meaningfully apply.
/// </summary>
public class AIAgentScopeValidatorTests
{
    private readonly AIAgentScopeValidator _sut = new();

    private static AIAgent AgentWithScope(AIAgentScope? scope) =>
        new() { Alias = "test-agent", Name = "Test Agent", Scope = scope };

    private static IAIAgentSurface Surface(params string[] dimensions)
    {
        var mock = new Mock<IAIAgentSurface>();
        mock.SetupGet(x => x.Id).Returns("test-surface");
        mock.SetupGet(x => x.Icon).Returns("icon-chat");
        mock.SetupGet(x => x.SupportedScopeDimensions).Returns(dimensions);
        return mock.Object;
    }

    private static AgentAvailabilityContext ContentDocument => new()
    {
        Section = "content",
        EntityType = "document",
    };

    [Fact]
    public void NullScope_ReturnsTrue()
    {
        var result = _sut.IsAgentAvailable(AgentWithScope(null), ContentDocument, Surface("section", "entityType"));

        result.ShouldBeTrue();
    }

    // --- Broad surface (empty dimensions) — the copilot-workspace case ---

    [Fact]
    public void EmptyDimensions_WithDenyRule_ReturnsTrue()
    {
        // A deny rule must NOT match vacuously on a dimensionless surface (regression guard for S5:
        // previously every deny rule matched → agent denied everywhere on the workspace surface).
        var agent = AgentWithScope(new AIAgentScope
        {
            DenyRules = [new AIAgentScopeRule { Sections = ["settings"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, Surface(/* no dimensions */));

        result.ShouldBeTrue();
    }

    [Fact]
    public void EmptyDimensions_WithAllowRule_ReturnsTrue()
    {
        var agent = AgentWithScope(new AIAgentScope
        {
            AllowRules = [new AIAgentScopeRule { Sections = ["content"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, Surface(/* no dimensions */));

        result.ShouldBeTrue();
    }

    [Fact]
    public void NullSurface_ReturnsTrue()
    {
        var agent = AgentWithScope(new AIAgentScope
        {
            DenyRules = [new AIAgentScopeRule { Sections = ["settings"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, surface: null);

        result.ShouldBeTrue();
    }

    // --- Scoped surface (section + entityType) — existing Copilot behaviour must be preserved ---

    [Fact]
    public void ScopedSurface_DenyRuleMatches_ReturnsFalse()
    {
        var agent = AgentWithScope(new AIAgentScope
        {
            DenyRules = [new AIAgentScopeRule { Sections = ["content"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, Surface("section", "entityType"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void ScopedSurface_AllowRuleMatches_ReturnsTrue()
    {
        var agent = AgentWithScope(new AIAgentScope
        {
            AllowRules = [new AIAgentScopeRule { Sections = ["content"], EntityTypes = ["document"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, Surface("section", "entityType"));

        result.ShouldBeTrue();
    }

    [Fact]
    public void ScopedSurface_AllowRuleDoesNotMatch_ReturnsFalse()
    {
        var agent = AgentWithScope(new AIAgentScope
        {
            AllowRules = [new AIAgentScopeRule { Sections = ["media"] }],
        });

        var result = _sut.IsAgentAvailable(agent, ContentDocument, Surface("section", "entityType"));

        result.ShouldBeFalse();
    }

    // --- IsAgentAvailableOnSurface: the one "can this agent run here" rule (active + opted in + scope) ---

    private const string OnSurfaceId = "test-surface";

    private static AIAgent AgentOnSurfaces(bool isActive = true, AIAgentScope? scope = null, params string[] surfaceIds) =>
        new()
        {
            Alias = "test-agent",
            Name = "Test Agent",
            IsActive = isActive,
            Scope = scope,
            SurfaceIds = surfaceIds,
        };

    private static AIAgentSurfaceCollection Surfaces(params string[] dimensions) =>
        new(() => [Surface(dimensions)]);

    private static AgentAvailabilityContext ContentDocumentOnSurface => new()
    {
        Surface = OnSurfaceId,
        Section = "content",
        EntityType = "document",
    };

    [Fact]
    public void OnSurface_ActiveAndOptedIn_ReturnsTrue()
    {
        var agent = AgentOnSurfaces(surfaceIds: [OnSurfaceId]);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeTrue();
    }

    [Fact]
    public void OnSurface_OptedInWithDifferentCasing_ReturnsTrue()
    {
        var agent = AgentOnSurfaces(surfaceIds: ["TEST-Surface"]);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeTrue();
    }

    [Fact]
    public void OnSurface_OptedInToADifferentSurfaceOnly_ReturnsFalse()
    {
        var agent = AgentOnSurfaces(surfaceIds: ["some-other-surface"]);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void OnSurface_EmptySurfaceIds_ReturnsFalse()
    {
        // An empty list means the agent is on no surface - not "on every surface".
        var agent = AgentOnSurfaces(surfaceIds: []);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void OnSurface_EmptySurfaceIdsOnADimensionlessSurface_ReturnsFalse()
    {
        // The copilot-workspace case: IsAgentAvailable alone says yes here (no dimensions to check),
        // so the opt-in check is the only thing keeping a non-opted-in agent out.
        var agent = AgentOnSurfaces(surfaceIds: []);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces(/* no dimensions */));

        result.ShouldBeFalse();
    }

    [Fact]
    public void OnSurface_Inactive_ReturnsFalse()
    {
        var agent = AgentOnSurfaces(isActive: false, surfaceIds: [OnSurfaceId]);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void OnSurface_OptedInButScopeDenied_ReturnsFalse()
    {
        var agent = AgentOnSurfaces(
            scope: new AIAgentScope { DenyRules = [new AIAgentScopeRule { Sections = ["content"] }] },
            surfaceIds: [OnSurfaceId]);

        var result = _sut.IsAgentAvailableOnSurface(agent, OnSurfaceId, ContentDocumentOnSurface, Surfaces("section"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void OnSurface_UnregisteredSurface_StillRequiresOptIn()
    {
        // No registered surface to read dimensions from: scope can't apply, but opt-in still does.
        var agent = AgentOnSurfaces(surfaceIds: []);

        var result = _sut.IsAgentAvailableOnSurface(
            agent, OnSurfaceId, ContentDocumentOnSurface, new AIAgentSurfaceCollection(() => []));

        result.ShouldBeFalse();
    }
}
