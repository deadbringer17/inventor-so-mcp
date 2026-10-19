using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui;
public class TextEntryTests
{
    [Fact]
    public void LiteralExpressionIsPreservedAndOnlyExplicitCommitUpdatesValue()
    {
        var entry=new TextEntry("expression","12 mm",2048);int commits=0;entry.Committed+=()=>commits++;
        entry.BeginEdit();Assert.True(entry.Replace("d0 / 2 + 12 mm"));Assert.Equal("12 mm",entry.Value);Assert.Equal(0,commits);
        Assert.True(entry.Commit(out _));Assert.Equal("d0 / 2 + 12 mm",entry.Value);Assert.Equal(1,commits);Assert.False(entry.Commit(out _));
    }
    [Fact]
    public void CursorEditsWithoutDiscardingTheRestAndCancelPreservesValue()
    {
        var entry=new TextEntry("name","Width",128);entry.BeginEdit();entry.MoveCursor(-1);entry.Backspace();entry.Type('X');
        Assert.Equal("WidXh",entry.Buffer);entry.CancelEdit();Assert.Equal("Width",entry.Value);Assert.False(entry.Editing);
    }
    [Fact]
    public void OversizedControlAndEmptyTextCannotCommitOrReplaceTheProposal()
    {
        var entry=new TextEntry("name","d0",4);entry.BeginEdit();Assert.False(entry.Replace("12345"));Assert.False(entry.Replace("a\nb"));
        Assert.Equal("d0",entry.Buffer);entry.Replace("  ");Assert.False(entry.Commit(out var reason));Assert.NotEmpty(reason);Assert.True(entry.Editing);
        entry.Replace("1234");Assert.False(entry.Type('5'));Assert.Equal("1234",entry.Buffer);
    }
    [Fact]
    public void LongExpressionsAreInspectableThroughTheCursorWithoutTruncatingStoredText()
    {
        var value=new string('a',100)+" mm";var entry=new TextEntry("expression",value,2048);entry.BeginEdit();Assert.StartsWith("…",entry.Display);
        for(int i=0;i<value.Length;i++) entry.MoveCursor(-1);Assert.StartsWith("│",entry.Display);Assert.EndsWith("…",entry.Display);
        Assert.Equal(value,entry.Buffer);
    }
    [Fact]
    public void FullNativeSuppressionBudgetIsEditableWithoutRelaxingExpressionFieldLimits()
    {
        var value=string.Join(",",Enumerable.Range(1,2000));Assert.True(value.Length>2048);
        var entry=new TextEntry("suppressed",value,12000);entry.BeginEdit();Assert.True(entry.Commit(out _));Assert.Equal(value,entry.Value);
        var expression=new TextEntry("expression","1 mm",2048);expression.BeginEdit();Assert.False(expression.Replace(value));
        Assert.Throws<ArgumentException>(()=>new TextEntry("oversized","",16385));
    }
}
