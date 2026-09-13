using System;
using Bimwright.Ipt.Shared.Infrastructure;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Thickness is written as a text expression, and Inventor rejects both a localized display string
/// and, on some locales, the invariant decimal point. The accepted forms are pinned here; the live
/// smoke test proves which one the installation takes.
/// </summary>
public sealed class SheetMetalExpressionTests
{
    [Fact]
    public void Whole_millimetres_need_no_decimal_fallback()
    {
        Assert.Equal(new[] { "2 mm" }, SheetMetalLengthExpression.Forms(2));
        Assert.Equal(new[] { "10 mm" }, SheetMetalLengthExpression.Forms(10));
    }

    [Fact]
    public void Fractional_values_offer_point_then_comma()
    {
        Assert.Equal(new[] { "0.8 mm", "0,8 mm" }, SheetMetalLengthExpression.Forms(0.8));
        Assert.Equal(new[] { "2.5 mm", "2,5 mm" }, SheetMetalLengthExpression.Forms(2.5));
    }

    [Fact]
    public void Formatting_never_depends_on_the_current_culture()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            Assert.Equal("1.25 mm", SheetMetalLengthExpression.Forms(1.25)[0]);
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Fact]
    public void Values_are_rounded_to_four_decimals()
        => Assert.Equal("0.1235 mm", SheetMetalLengthExpression.Forms(0.12345)[0]);
}
