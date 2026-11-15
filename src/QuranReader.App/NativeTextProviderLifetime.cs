using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QuranReader.App;

internal static class NativeTextProviderLifetime
{
    private static readonly ConditionalWeakTable<FrameworkElement, WeakReference<RichTextBlock>> masters = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> prepared = new();
    internal static void Protect(FrameworkElement element, FrameworkElement? document = null)
    {
        if (document is RichTextBlock master)
        {
            masters.Remove(element); masters.Add(element, new(master));
        }
        if (element is not (TextBlock or RichTextBlock or RichTextBlockOverflow) || prepared.TryGetValue(element, out _)) return;
        // Settle WinUI's native text view before measurement. Do not root the
        // native provider back to its owner: that creates a native ref cycle.
        _ = FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetPattern(PatternInterface.Text);
        prepared.Add(element, new object());
    }
    internal static RichTextBlock? Master(FrameworkElement element) => element as RichTextBlock
        ?? (masters.TryGetValue(element, out var weak) && weak.TryGetTarget(out var master) ? master : null);
    internal static void ProtectTree(DependencyObject element)
    {
        if (element is FrameworkElement owner) Protect(owner);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) ProtectTree(VisualTreeHelper.GetChild(element, i));
    }
}
