using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace QuranReader.App;

/// <summary>Native UIA semantics with explicit managed ownership of outgoing text patterns/ranges.</summary>
public sealed partial class AutomationRootGrid : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RootPeer(this);

    private sealed record Lease(DependencyObject Owner, RichTextBlock? Document);

    private sealed partial class RootPeer(AutomationRootGrid owner) : FrameworkElementAutomationPeer(owner)
    {
        private readonly ConditionalWeakTable<AutomationPeer, WeakReference<NodePeer>> peers = new();
        private readonly Lease rootLease = new(owner, null);
        protected override IList<AutomationPeer> GetChildrenCore() => (base.GetChildrenCore() ?? []).Select(p => Wrap(p, rootLease)).ToList();
        internal AutomationPeer Wrap(AutomationPeer native, Lease fallback)
        {
            if (native is NodePeer) return native;
            if (peers.TryGetValue(native, out var old) && old.TryGetTarget(out var live)) return live;
            var element = (native as FrameworkElementAutomationPeer)?.Owner as FrameworkElement;
            var lease = element is null ? fallback : new Lease(element, NativeTextProviderLifetime.Master(element) ?? fallback.Document);
            var peer = new NodePeer(this, native, lease);
            peers.Remove(native); peers.Add(native, new(peer));
            native.EventsSource = peer;
            return peer;
        }
        internal IRawElementProviderSimple WrapProvider(IRawElementProviderSimple provider, Lease lease) => ProviderFromPeer(Wrap(PeerFromProvider(provider), lease));
        internal IRawElementProviderSimple UnwrapProvider(IRawElementProviderSimple provider) => PeerFromProvider(provider) is NodePeer node ? ProviderFromPeer(node.Native) : provider;
    }

    private sealed partial class NodePeer(RootPeer root, AutomationPeer native, Lease lease) : AutomationPeer
    {
        internal AutomationPeer Native => native;
        protected override object GetPatternCore(PatternInterface pattern)
        {
            var value = native.GetPattern(pattern);
            return value switch
            {
                ITextProvider text => new OwnedText(text, root, lease),
                ITextChildProvider child => new OwnedTextChild(child, root, lease),
                IInvokeProvider invoke => new OwnedInvoke(invoke, lease),
                _ => value
            };
        }
        protected override IList<AutomationPeer> GetChildrenCore() => (native.GetChildren() ?? []).Select(p => root.Wrap(p, lease)).ToList();
        protected override string GetAcceleratorKeyCore() => native.GetAcceleratorKey();
        protected override string GetAccessKeyCore() => native.GetAccessKey();
        protected override AutomationControlType GetAutomationControlTypeCore() => native.GetAutomationControlType();
        protected override string GetAutomationIdCore() => native.GetAutomationId();
        protected override Rect GetBoundingRectangleCore() => native.GetBoundingRectangle();
        protected override string GetClassNameCore() => native.GetClassName();
        protected override Point GetClickablePointCore() => native.GetClickablePoint();
        protected override string GetHelpTextCore() => native.GetHelpText();
        protected override string GetItemStatusCore() => native.GetItemStatus();
        protected override string GetItemTypeCore() => native.GetItemType();
        protected override AutomationPeer GetLabeledByCore() => native.GetLabeledBy() is { } peer ? root.Wrap(peer, lease) : null!;
        protected override string GetLocalizedControlTypeCore() => native.GetLocalizedControlType();
        protected override string GetNameCore() => native.GetName();
        protected override AutomationOrientation GetOrientationCore() => native.GetOrientation();
        protected override bool HasKeyboardFocusCore() => native.HasKeyboardFocus();
        protected override bool IsContentElementCore() => native.IsContentElement();
        protected override bool IsControlElementCore() => native.IsControlElement();
        protected override bool IsEnabledCore() => native.IsEnabled();
        protected override bool IsKeyboardFocusableCore() => native.IsKeyboardFocusable();
        protected override bool IsOffscreenCore() => lease.Owner is FrameworkElement element && !element.IsLoaded || native.IsOffscreen();
        protected override bool IsPasswordCore() => native.IsPassword();
        protected override bool IsRequiredForFormCore() => native.IsRequiredForForm();
        protected override AutomationHeadingLevel GetHeadingLevelCore() => native.GetHeadingLevel();
        protected override void SetFocusCore() => native.SetFocus();
    }

    private sealed partial class OwnedText(ITextProvider native, RootPeer root, Lease lease) : ITextProvider
    {
        public ITextRangeProvider DocumentRange => new OwnedRange(native.DocumentRange, root, lease);
        public SupportedTextSelection SupportedTextSelection => native.SupportedTextSelection;
        public ITextRangeProvider[] GetSelection() => (native.GetSelection() ?? []).Select(r => (ITextRangeProvider)new OwnedRange(r, root, lease)).ToArray();
        public ITextRangeProvider[] GetVisibleRanges() => (native.GetVisibleRanges() ?? []).Select(r => (ITextRangeProvider)new OwnedRange(r, root, lease)).ToArray();
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple child) => new OwnedRange(native.RangeFromChild(root.UnwrapProvider(child)), root, lease);
        public ITextRangeProvider RangeFromPoint(Point screenLocation) => new OwnedRange(native.RangeFromPoint(screenLocation), root, lease);
    }

    private sealed partial class OwnedTextChild(ITextChildProvider native, RootPeer root, Lease lease) : ITextChildProvider
    {
        public IRawElementProviderSimple TextContainer => root.WrapProvider(native.TextContainer, lease);
        public ITextRangeProvider TextRange => new OwnedRange(native.TextRange, root, lease);
    }

    private sealed partial class OwnedInvoke(IInvokeProvider native, Lease lease) : IInvokeProvider
    {
        public void Invoke() { EnsureAttached(lease); native.Invoke(); }
    }

    private static void EnsureAttached(Lease lease)
    {
        if (lease.Owner is FrameworkElement element && !element.IsLoaded)
            throw new COMException("The text owner has left the active reading view.", unchecked((int)0x80040201));
    }

    private sealed partial class OwnedRange(ITextRangeProvider native, RootPeer root, Lease lease) : ITextRangeProvider
    {
        private static ITextRangeProvider Unwrap(ITextRangeProvider range) => range is OwnedRange owned ? owned.Native : range;
        private ITextRangeProvider Native => native;
        public ITextRangeProvider Clone() => new OwnedRange(native.Clone(), root, lease);
        public bool Compare(ITextRangeProvider range) => native.Compare(Unwrap(range));
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) => native.CompareEndpoints(endpoint, Unwrap(targetRange), targetEndpoint);
        public void ExpandToEnclosingUnit(TextUnit unit) => native.ExpandToEnclosingUnit(unit);
        public ITextRangeProvider FindAttribute(int attribute, object value, bool backward) => native.FindAttribute(attribute, value, backward) is { } found ? new OwnedRange(found, root, lease) : null!;
        public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase) => native.FindText(text, backward, ignoreCase) is { } found ? new OwnedRange(found, root, lease) : null!;
        public object GetAttributeValue(int attribute) => native.GetAttributeValue(attribute);
        public void GetBoundingRectangles(out double[] rectangles)
        {
            if (lease.Owner is FrameworkElement element && !element.IsLoaded) rectangles = [];
            else native.GetBoundingRectangles(out rectangles);
        }
        public IRawElementProviderSimple GetEnclosingElement() => root.WrapProvider(native.GetEnclosingElement(), lease);
        public string GetText(int maxLength)
        {
            var text = native.GetText(maxLength);
            GC.KeepAlive(lease);
            return text;
        }
        public int Move(TextUnit unit, int count) => native.Move(unit, count);
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) => native.MoveEndpointByRange(endpoint, Unwrap(targetRange), targetEndpoint);
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count) => native.MoveEndpointByUnit(endpoint, unit, count);
        public void RemoveFromSelection() { EnsureAttached(lease); native.RemoveFromSelection(); }
        public void ScrollIntoView(bool alignToTop) { EnsureAttached(lease); native.ScrollIntoView(alignToTop); }
        public void Select() { EnsureAttached(lease); native.Select(); }
        public void AddToSelection() { EnsureAttached(lease); native.AddToSelection(); }
        public IRawElementProviderSimple[] GetChildren() => (native.GetChildren() ?? []).Select(p => root.WrapProvider(p, lease)).ToArray();
    }
}
