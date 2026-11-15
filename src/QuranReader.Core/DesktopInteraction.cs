namespace QuranReader.Core;

public readonly record struct DesktopCommandLayout(bool Compact, bool ShowBrand, bool ShowStatus)
{
    public static DesktopCommandLayout ForWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return new(width < 1100, width >= 1100, width >= 1250);
    }
}

/// <summary>Suppress only a repeated pointer turn; keyboard navigation bypasses this gate.</summary>
public sealed class PointerPageTurnGate
{
    private long? last;
    private bool direction;

    public bool TryAccept(bool forward, long milliseconds)
    {
        if (last is { } previous && direction == forward && milliseconds - previous < 300) return false;
        last = milliseconds;
        direction = forward;
        return true;
    }
}
