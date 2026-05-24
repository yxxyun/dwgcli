using System.Reflection;
using ACadSharp;
using DwgCli.Core;

namespace DwgCli.Tests;

public class ParseColorTests
{
    private static readonly MethodInfo? ParseColorMethod;

    static ParseColorTests()
    {
        // DwgHandler is internal sealed partial, but we have InternalsVisibleTo
        var type = typeof(DwgHandler);
        ParseColorMethod = type.GetMethod("ParseColor", BindingFlags.Static | BindingFlags.NonPublic);
        if (ParseColorMethod == null)
            throw new InvalidOperationException("Could not find DwgHandler.ParseColor method");
    }

    private static ACadSharp.Color? InvokeParseColor(string? value)
    {
        var result = ParseColorMethod!.Invoke(null, [value]);
        return (ACadSharp.Color?)result;
    }

    [Fact]
    public void ParseColor_Null_ReturnsNull()
    {
        var result = InvokeParseColor(null);
        Assert.Null(result);
    }

    [Fact]
    public void ParseColor_Empty_ReturnsNull()
    {
        var result = InvokeParseColor("");
        Assert.Null(result);
    }

    [Fact]
    public void ParseColor_ByLayer_ReturnsByLayer()
    {
        var result = InvokeParseColor("byLayer");
        Assert.NotNull(result);
        Assert.Equal(ACadSharp.Color.ByLayer, result.Value);
    }

    [Fact]
    public void ParseColor_ByBlock_ReturnsByBlock()
    {
        var result = InvokeParseColor("byBlock");
        Assert.NotNull(result);
        Assert.Equal(ACadSharp.Color.ByBlock, result.Value);
    }

    [Fact]
    public void ParseColor_Red_ReturnsCorrectAci()
    {
        var result = InvokeParseColor("red");
        Assert.NotNull(result);
        Assert.Equal((short)1, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Grey_NormalizesViaInputValidator()
    {
        // "grey" normalizes to "gray" via InputValidator, but "gray" is not in the named color switch
        // (which maps red, yellow, green, cyan, blue, magenta, white, black).
        // This test documents the current behavior — grey is normalized but then falls through.
        var result = InvokeParseColor("grey");
        Assert.Null(result);
    }

    [Fact]
    public void ParseColor_AciIndex_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("1");
        Assert.NotNull(result);
        Assert.Equal((short)1, result.Value.Index);
    }

    [Fact]
    public void ParseColor_AciIndex255_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("255");
        Assert.NotNull(result);
        Assert.Equal((short)255, result.Value.Index);
    }

    [Fact]
    public void ParseColor_RgbHex_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("#FF0000");
        Assert.NotNull(result);
        Assert.True(result.Value.IsTrueColor);
    }

    [Fact]
    public void ParseColor_RgbHexWithoutHash_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("00FF00");
        Assert.NotNull(result);
        Assert.True(result.Value.IsTrueColor);
    }

    [Fact]
    public void ParseColor_Yellow_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("yellow");
        Assert.NotNull(result);
        Assert.Equal((short)2, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Green_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("green");
        Assert.NotNull(result);
        Assert.Equal((short)3, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Cyan_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("cyan");
        Assert.NotNull(result);
        Assert.Equal((short)4, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Blue_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("blue");
        Assert.NotNull(result);
        Assert.Equal((short)5, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Magenta_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("magenta");
        Assert.NotNull(result);
        Assert.Equal((short)6, result.Value.Index);
    }

    [Fact]
    public void ParseColor_Black_ReturnsCorrectColor()
    {
        var result = InvokeParseColor("black");
        Assert.NotNull(result);
        Assert.Equal((short)250, result.Value.Index);
    }

    [Fact]
    public void ParseColor_InvalidString_ReturnsNull()
    {
        var result = InvokeParseColor("notacolor");
        Assert.Null(result);
    }
}
