using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;

namespace InventorXrSo.Core.Tests.Ui;
public class IntegerNumericEntryTests
{
    [Theory][InlineData(0.5,0,100)][InlineData(50,0.5,100)][InlineData(50,0,99.5)]
    [InlineData(50,double.NegativeInfinity,100)][InlineData(50,0,double.PositiveInfinity)]
    public void IntegerFieldsRequireFiniteIntegerInitialValuesAndBounds(double value,double min,double max)
        => Assert.Throws<ArgumentException>(()=>new NumericEntry("tension",QuantityUnit.None,value,min,max,true));
    [Theory][InlineData(-1)][InlineData(101)][InlineData(50.5)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidValuesKeepTheKeypadOpenWithoutCommitting(double value)
    {
        var entry=new NumericEntry("tension",QuantityUnit.None,50,0,100,integerOnly:true);int commits=0;entry.Committed+=()=>commits++;
        entry.BeginEdit();Assert.False(entry.CommitValue(value,out _));Assert.True(entry.Editing);Assert.Equal(50,entry.Value);Assert.Equal(0,commits);
    }
    [Fact]
    public void FractionalTypedInputIsPreservedForCorrectionAndNeverRounded()
    {
        var entry=new NumericEntry("tension",QuantityUnit.None,50,0,100,true);entry.BeginEdit();foreach(char c in "50,5")entry.Type(c);
        Assert.False(entry.Commit(out _));Assert.Equal("50,5",entry.Buffer);Assert.Equal(50,entry.Value);
        entry.Backspace();entry.Backspace();Assert.True(entry.Commit(out _));Assert.Equal(50,entry.Value);
    }
    [Fact]
    public void IntegerStepsNeverSelectTenthsAndRemainBounded()
    {
        var entry=new NumericEntry("tension",QuantityUnit.None,99,0,100,true);entry.CycleStep(-1);Assert.Equal(1,entry.Step);
        entry.Nudge(1);Assert.Equal(100,entry.Value);Assert.False(entry.Nudge(1));entry.CycleStep(1);entry.Nudge(-1);Assert.Equal(90,entry.Value);
    }
    [Theory][InlineData(0)][InlineData(100)]
    public void DomainEndpointsCommitExactly(int value)
    {
        var entry=new NumericEntry("tension",QuantityUnit.None,50,0,100,true);entry.BeginEdit();Assert.True(entry.CommitValue(value,out _));Assert.Equal(value,entry.Value);Assert.False(entry.Editing);
    }
}
