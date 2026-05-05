using System.Text.RegularExpressions;
using FluentAssertions;
using ViewGrid.Core.Settings;

namespace ViewGrid.Core.Tests.Settings;

public sealed class AccentColorPresetsTests
{
    private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    [Fact]
    public void All_Contains_Six_Presets()
    {
        AccentColorPresets.All.Should().HaveCount(6);
    }

    [Fact]
    public void All_Ids_Are_Unique()
    {
        var ids = AccentColorPresets.All.Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Default_Is_Sky()
    {
        AccentColorPresets.Default.Id.Should().Be("Sky");
    }

    [Theory]
    [InlineData("Sky")]
    [InlineData("Emerald")]
    [InlineData("Rose")]
    [InlineData("Violet")]
    [InlineData("Amber")]
    [InlineData("Slate")]
    public void Get_Returns_Preset_For_Known_Id(string id)
    {
        AccentColorPresets.Get(id).Id.Should().Be(id);
    }

    [Fact]
    public void Get_Returns_Default_For_Unknown_Id()
    {
        AccentColorPresets.Get("Unknown").Should().Be(AccentColorPresets.Default);
        AccentColorPresets.Get(null).Should().Be(AccentColorPresets.Default);
        AccentColorPresets.Get(string.Empty).Should().Be(AccentColorPresets.Default);
    }

    [Fact]
    public void All_Hex_Values_Are_Valid()
    {
        // 全プリセットの全 HEX (Light 7 + Dark 7 + SwatchColor) が #RRGGBB 形式
        foreach (var preset in AccentColorPresets.All)
        {
            HexPattern.IsMatch(preset.SwatchColor).Should().BeTrue($"swatch of {preset.Id}");
            ValidatePalette(preset.Id, "Light", preset.Light);
            ValidatePalette(preset.Id, "Dark", preset.Dark);
        }

        static void ValidatePalette(string presetId, string variant, AccentColorPalette p)
        {
            HexPattern.IsMatch(p.Color).Should().BeTrue($"{presetId}/{variant}/Color");
            HexPattern.IsMatch(p.Dark1).Should().BeTrue($"{presetId}/{variant}/Dark1");
            HexPattern.IsMatch(p.Dark2).Should().BeTrue($"{presetId}/{variant}/Dark2");
            HexPattern.IsMatch(p.Dark3).Should().BeTrue($"{presetId}/{variant}/Dark3");
            HexPattern.IsMatch(p.Light1).Should().BeTrue($"{presetId}/{variant}/Light1");
            HexPattern.IsMatch(p.Light2).Should().BeTrue($"{presetId}/{variant}/Light2");
            HexPattern.IsMatch(p.Light3).Should().BeTrue($"{presetId}/{variant}/Light3");
        }
    }
}
