using DwgCli.Core;

namespace DwgCli.Tests;

public class InputValidatorTests
{
    // ========== Color Normalization ==========

    [Theory]
    [InlineData("grey", "gray")]
    [InlineData("light_grey", "light_gray")]
    [InlineData("darkgrey", "dark_gray")]
    [InlineData("blu", "blue")]
    [InlineData("ble", "blue")]
    [InlineData("purple", "magenta")]
    [InlineData("by-layer", "byLayer")]
    [InlineData("by_layer", "byLayer")]
    [InlineData("by-block", "byBlock")]
    [InlineData("by_block", "byBlock")]
    [InlineData("bylayer", "byLayer")]
    [InlineData("byblock", "byBlock")]
    [InlineData("red", "red")]
    [InlineData("blue", "blue")]
    [InlineData("green", "green")]
    [InlineData("1", "1")]
    [InlineData("255", "255")]
    public void NormalizeColor_VariousInputs_ReturnsExpected(string input, string expected)
    {
        var result = InputValidator.NormalizeColor(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeColor_Null_ReturnsEmptyString()
    {
        var result = InputValidator.NormalizeColor(null);
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeColor_Empty_ReturnsEmptyString()
    {
        var result = InputValidator.NormalizeColor("");
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeColor_UnknownValue_PassesThrough()
    {
        var result = InputValidator.NormalizeColor("unknown_color");
        Assert.Equal("unknown_color", result);
    }

    // ========== Double Parsing ==========

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("3", 3.0)]
    [InlineData("0", 0.0)]
    [InlineData("-1.5", -1.5)]
    public void TryParseDouble_ValidStrings_ReturnsValue(string input, double expected)
    {
        var result = InputValidator.TryParseDouble(input);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value, 6);
    }

    [Fact]
    public void TryParseDouble_WithUnitSuffix_StripsAndParses()
    {
        var result = InputValidator.TryParseDouble("2.5mm");
        Assert.NotNull(result);
        Assert.Equal(2.5, result.Value, 6);
    }

    [Fact]
    public void TryParseDouble_InvalidString_ReturnsNull()
    {
        var result = InputValidator.TryParseDouble("abc");
        Assert.Null(result);
    }

    [Fact]
    public void TryParseDouble_EmptyString_ReturnsNull()
    {
        var result = InputValidator.TryParseDouble("");
        Assert.Null(result);
    }

    [Fact]
    public void TryParseDouble_Null_ReturnsNull()
    {
        var result = InputValidator.TryParseDouble(null);
        Assert.Null(result);
    }

    // ========== Bool Parsing ==========

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("on", true)]
    [InlineData("On", true)]
    [InlineData("enabled", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("disabled", false)]
    public void TryParseBool_VariousInputs_ReturnsExpected(string input, bool expected)
    {
        var result = InputValidator.TryParseBool(input);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void TryParseBool_InvalidString_ReturnsNull()
    {
        var result = InputValidator.TryParseBool("abc");
        Assert.Null(result);
    }

    [Fact]
    public void TryParseBool_EmptyString_ReturnsNull()
    {
        var result = InputValidator.TryParseBool("");
        Assert.Null(result);
    }

    [Fact]
    public void TryParseBool_Null_ReturnsNull()
    {
        var result = InputValidator.TryParseBool(null);
        Assert.Null(result);
    }

    // ========== Coordinate Normalization ==========

    [Theory]
    [InlineData("10,20", "10,20")]
    [InlineData("10, 20", "10,20")]
    [InlineData("10;20", "10,20")]
    [InlineData("(10,20)", "10,20")]
    [InlineData("[10,20]", "10,20")]
    [InlineData("10,20,30", "10,20,30")]
    [InlineData("10, 20, 30", "10,20,30")]
    [InlineData("(10,20,30)", "10,20,30")]
    public void NormalizeCoordinate_VariousFormats_ReturnsExpected(string input, string expected)
    {
        var result = InputValidator.NormalizeCoordinate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeCoordinate_Empty_ReturnsEmptyString()
    {
        var result = InputValidator.NormalizeCoordinate("");
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeCoordinate_Null_ReturnsEmptyString()
    {
        var result = InputValidator.NormalizeCoordinate(null);
        Assert.Equal("", result);
    }

    // ========== CorrectProperties ==========

    [Fact]
    public void CorrectProperties_NormalizesColorKey()
    {
        var props = new Dictionary<string, string>
        {
            ["color"] = "blu"
        };
        InputValidator.CorrectProperties(props);
        Assert.Equal("blue", props["color"]);
    }

    [Fact]
    public void CorrectProperties_NonColorKeys_Unchanged()
    {
        var props = new Dictionary<string, string>
        {
            ["layer"] = "0",
            ["linetype"] = "Continuous"
        };
        InputValidator.CorrectProperties(props);
        Assert.Equal("0", props["layer"]);
        Assert.Equal("Continuous", props["linetype"]);
    }

    [Fact]
    public void CorrectProperties_MixedKeys_OnlyColorNormalized()
    {
        var props = new Dictionary<string, string>
        {
            ["color"] = "grey",
            ["layer"] = "test",
            ["name"] = "foo"
        };
        InputValidator.CorrectProperties(props);
        Assert.Equal("gray", props["color"]);
        Assert.Equal("test", props["layer"]);
        Assert.Equal("foo", props["name"]);
    }
}
