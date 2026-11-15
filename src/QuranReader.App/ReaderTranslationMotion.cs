using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace QuranReader.App;

/// <summary>One compositor translation at a time; no opacity, scale or layout animation.</summary>
internal sealed class ReaderTranslationMotion
{
    private UIElement? target;
    private int revision;
    private TaskCompletionSource completion = Completed();
    public Task WaitForCompletionAsync() => completion.Task;
    private static TaskCompletionSource Completed()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }
    public void Stop()
    {
        ++revision;
        completion.TrySetResult();
        if (target is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(target);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        target = null;
    }
    public void Enter(UIElement element, float from, bool enabled)
    {
        Stop();
        if (!enabled) return;
        Start(element, from, 0);
    }
    public async Task<bool> ExitAsync(UIElement element, float to, bool enabled)
    {
        Stop();
        var serial = revision;
        if (enabled) { Start(element, 0, to); await completion.Task; }
        if (serial != revision) return false;
        Stop();
        return true;
    }
    private void Start(UIElement element, float from, float to)
    {
        target = element;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = completion;
        var serial = revision;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        Windows.Foundation.TypedEventHandler<object, Microsoft.UI.Composition.CompositionBatchCompletedEventArgs>? completed = null;
        completed = (_, _) =>
        {
            batch.Completed -= completed;
            if (serial == revision)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                target = null;
            }
            finished.TrySetResult();
            batch.Dispose();
        };
        batch.Completed += completed;
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(200);
        animation.InsertKeyFrame(0, new(from, 0, 0));
        animation.InsertKeyFrame(1, new(to, 0, 0), visual.Compositor.CreateCubicBezierEasingFunction(new(.16f, 1), new(.3f, 1)));
        visual.StartAnimation("Translation", animation);
        batch.End();
    }
}
