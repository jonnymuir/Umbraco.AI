using Umbraco.AI.Core.Settings;
using Umbraco.AI.Web.Api.Management.Settings.Mapping;

namespace Umbraco.AI.Tests.Unit.Api.Management.Settings;

public class SettingsMapDefinitionTests
{
    [Theory]
    [InlineData("Always", AIDisclosureNoticeMode.Always)]
    [InlineData("Dismissible", AIDisclosureNoticeMode.Dismissible)]
    [InlineData("off", AIDisclosureNoticeMode.Off)]
    public void ParseDisclosureNoticeMode_WithKnownValue_ReturnsMode(string value, AIDisclosureNoticeMode expected)
        => SettingsMapDefinition.ParseDisclosureNoticeMode(value).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sometimes")]
    [InlineData("5")]
    public void ParseDisclosureNoticeMode_WithMissingOrUnknownValue_FallsBackToAlways(string? value)
        // A bad value must never switch the notice off.
        => SettingsMapDefinition.ParseDisclosureNoticeMode(value).ShouldBe(AIDisclosureNoticeMode.Always);
}
