using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace QuranReader.App;

public sealed partial class MainWindow
{
    private int surfaceRevision;
    private bool adaptivePresented;

    private void PresentAdaptiveSurface()
    {
        if (!continuousBook.Ready || !IsContinuousPreview || IsPreviewOpening || model.IsBusy) return;
        ++surfaceRevision;
        continuousBook.SetPresentationActive(true);
        ReadingSurface.Visibility = mushafView.Visibility = Visibility.Collapsed;
        adaptivePresented = true;
        NavigationTrace.Mark(NavigationTrace.Current, "AdaptiveHostPresented");
    }

    private void QueueBookPresentation(int serial)
    {
        var trace = NavigationTrace.Current;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (closing || serial != surfaceRevision || model.IsBusy || !IsBookMode || IsContinuousPreview && !IsPreviewOpening) return;
            NavigationTrace.Mark(trace, "WinUiOpeningMeasureArrangeStarted");
            mushafView.UpdateLayout();
            NavigationTrace.Mark(trace, "WinUiOpeningMeasureArrangeCompleted");
            // The replacement has valid native geometry before retiring the old
            // presentation. Park both bounded adaptive banks without detaching
            // their overflow chain or invalidating retained external UIA ranges.
            mushafView.Opacity = 1; mushafView.IsHitTestVisible = true;
            ReadingSurface.Visibility = Visibility.Collapsed;
            continuousBook.Park(); adaptivePresented = false;
            NavigationTrace.Incoming(trace);
            _ = NavigationTrace.AnimationAsync(trace, Task.CompletedTask);
        })) throw new InvalidOperationException("The book presentation queue is unavailable.");
    }

    private void QueueStudyPresentation(int serial)
    {
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (closing || serial != surfaceRevision || model.IsBusy || IsBookMode) return;
            ReadingSurface.UpdateLayout(); ReadingSurface.Opacity = 1;
            mushafView.Visibility = Visibility.Collapsed;
            continuousBook.Park(); adaptivePresented = false;
        })) throw new InvalidOperationException("The study presentation queue is unavailable.");
    }
}
