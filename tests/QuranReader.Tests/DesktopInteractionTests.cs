using QuranReader.Core;

namespace QuranReader.Tests;

public sealed class DesktopInteractionTests
{
    [Theory]
    [InlineData(752, true, false, false)]
    [InlineData(1099, true, false, false)]
    [InlineData(1100, false, true, false)]
    [InlineData(1250, false, true, true)]
    [InlineData(2500, false, true, true)]
    public void CommandPolicyPreservesTargetsAtCompactWidths(double width, bool compact, bool brand, bool status)
    {
        Assert.Equal(new(compact, brand, status), DesktopCommandLayout.ForWidth(width));
    }

    [Fact]
    public void PointerDoubleTurnIsSuppressedWithoutBlockingReturnOrLaterTurns()
    {
        var gate = new PointerPageTurnGate();
        Assert.True(gate.TryAccept(true, 1000));
        Assert.False(gate.TryAccept(true, 1100));
        Assert.False(gate.TryAccept(true, 1299));
        Assert.True(gate.TryAccept(true, 1300));
        Assert.True(gate.TryAccept(false, 1301));
        Assert.True(gate.TryAccept(true, 1302));
    }
}
