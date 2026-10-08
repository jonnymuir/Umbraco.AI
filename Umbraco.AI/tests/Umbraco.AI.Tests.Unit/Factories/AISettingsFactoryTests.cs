using Umbraco.AI.Core.Settings;
using Umbraco.AI.Persistence.Settings;

namespace Umbraco.AI.Tests.Unit.Factories;

public class AISettingsFactoryTests
{
    [Fact]
    public void BuildDomain_WithNoDisclosureRow_DefaultsToAlways()
    {
        // Existing installs have no row until settings are next saved.
        var settings = AISettingsFactory.BuildDomain([]);

        settings.DisclosureNoticeMode.ShouldBe(AIDisclosureNoticeMode.Always);
    }

    [Theory]
    [InlineData(AIDisclosureNoticeMode.Always)]
    [InlineData(AIDisclosureNoticeMode.Dismissible)]
    [InlineData(AIDisclosureNoticeMode.Off)]
    public void DisclosureNoticeMode_RoundTripsThroughEntities(AIDisclosureNoticeMode mode)
    {
        var entities = AISettingsFactory.BuildEntities(
            new AISettings { DisclosureNoticeMode = mode }, [], userId: null).ToList();

        var settings = AISettingsFactory.BuildDomain(entities);

        settings.DisclosureNoticeMode.ShouldBe(mode);
    }

    [Fact]
    public void BuildDomain_WithUnreadableDisclosureValue_FallsBackToAlways()
    {
        var entities = new[]
        {
            new AISettingsEntity { Id = Guid.NewGuid(), Key = nameof(AISettings.DisclosureNoticeMode), Value = "not-a-mode" },
        };

        var settings = AISettingsFactory.BuildDomain(entities);

        settings.DisclosureNoticeMode.ShouldBe(AIDisclosureNoticeMode.Always);
    }
}
