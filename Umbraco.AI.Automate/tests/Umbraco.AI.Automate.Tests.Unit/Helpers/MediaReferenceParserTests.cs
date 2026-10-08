using Shouldly;
using Umbraco.AI.Automate.Helpers;
using Xunit;

namespace Umbraco.AI.Automate.Tests.Unit.Helpers;

public class MediaReferenceParserTests
{
    private static readonly Guid KeyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid KeyB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_WithEmptyValue_ReturnsNoKeys(string? value)
    {
        MediaReferenceParser.TryParse(value, out var keys, out _).ShouldBeTrue();
        keys.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa, bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\nbbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [InlineData("umb://media/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;umb://media/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("""[{"key":"1","mediaKey":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},{"mediaKey":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}]""")]
    [InlineData("""[{"udi":"umb://media/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]""")]
    public void TryParse_WithSupportedFormats_ReturnsKeysInOrder(string value)
    {
        MediaReferenceParser.TryParse(value, out var keys, out _).ShouldBeTrue();
        keys.ShouldBe([KeyA, KeyB]);
    }

    [Fact]
    public void TryParse_WithSingleMediaPickerObject_ReturnsKey()
    {
        MediaReferenceParser.TryParse("""{"mediaKey":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""", out var keys, out _)
            .ShouldBeTrue();
        keys.ShouldBe([KeyA]);
    }

    [Fact]
    public void TryParse_WithDuplicateReferences_ReturnsDistinctKeys()
    {
        MediaReferenceParser.TryParse($"{KeyA}, umb://media/{KeyA:N}", out var keys, out _).ShouldBeTrue();
        keys.ShouldBe([KeyA]);
    }

    [Theory]
    [InlineData("/media/1234/photo.png")]
    [InlineData("umb://document/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("""[{"src":"/media/1234/photo.png"}]""")]
    [InlineData("{not json")]
    public void TryParse_WithUnsupportedReference_Fails(string value)
    {
        MediaReferenceParser.TryParse(value, out _, out var invalid).ShouldBeFalse();
        invalid.ShouldNotBeNullOrEmpty();
    }
}
