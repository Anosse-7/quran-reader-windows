using System.ComponentModel;

namespace QuranReader.Core;

public sealed class ReaderViewModel(IContentRepository content, IUserStateStore store) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? navigation;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly HashSet<AyahId> bookmarks = [];
    private long tafsirRevision;
    private long locationRevision;
    public ContinuousScripture? Continuous { get; private set; }
    private ContinuousScripture? presentedContinuous;
    public IReadOnlyList<Verse> FacingVerses { get; private set; } = [];
    public IReadOnlyList<Surah> Surahs { get; private set; } = [];
    public IReadOnlyList<Verse> Verses { get; private set; } = [];
    public Verse? SelectedVerse { get; private set; }
    // Explicit session selection is distinct from automatic/restored reading anchors.
    public bool HasActiveSelection { get; private set; }
    public Verse? ActiveTafsirVerse => HasActiveSelection ? SelectedVerse : null;
    public int ActivePage => Page;
    public bool CanonicalSelectionLoaded => SelectedVerse is not null && Verses.Concat(FacingVerses).Any(v => v.Id == SelectedVerse.Id);
    public Surah? ActiveSurah => Surahs.FirstOrDefault(s => s.Number == (Settings.Mode == ReadingMode.AdaptiveBook || !CanonicalSelectionLoaded
        ? Position.Ayah.Surah : SelectedVerse?.Id.Surah ?? Verses.FirstOrDefault()?.Id.Surah ?? 1));
    public string ActiveSurahName => SelectedVerse?.SurahName ?? Verses.FirstOrDefault()?.SurahName ?? "";
    public string TafsirTitle => SelectedVerse is { } v ? $"تفسير سورة {v.SurahName} · آية {SurahMetadata.ToEasternArabicDigits(v.Id.Ayah)}" : "التفسير الميسر";
    public ReadingPosition Position { get; private set; } = new(new(1, 1));
    public ReaderSettings Settings { get; private set; } = new();
    public int Page { get; private set; } = 1;
    public string Tafsir { get; private set; } = "";
    public string? Error { get; private set; }
    public bool Ready { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsBookmarked => SelectedVerse is not null && bookmarks.Contains(SelectedVerse.Id);
    public IReadOnlyList<AyahId> Bookmarks => bookmarks.OrderBy(x => x.Surah).ThenBy(x => x.Ayah).ToArray();
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        try
        {
            await content.ValidateAsync();
            Surahs = await content.GetSurahsAsync();
            var state = await store.LoadAsync();
            Settings = state.Settings;
            Position = state.Position;
            foreach (var id in state.Bookmarks) bookmarks.Add(id);
            Ready = true;
            var page = await content.FindPageAsync(Position.Ayah);
            if (page is null) throw new ResourceException("Saved reading position is invalid. Review the separate user database.");
            await NavigateAsync(page.Value, Position.Ayah, Position.Offset, Position.TextOffset, activateTarget: false);
            if (state.SelectedAyah is { } selected)
            {
                var restored = LoadedVerses().FirstOrDefault(v => v.Id == selected);
                if (restored is null && Settings.Mode == ReadingMode.AdaptiveBook)
                {
                    var selectedPage = await content.FindPageAsync(selected);
                    if (selectedPage is { } p) restored = (await content.GetPageAsync(p)).FirstOrDefault(v => v.Id == selected);
                    if (restored is not null)
                    {
                        SelectedVerse = restored;
                        await LoadTafsirAsync();
                    }
                }
                if (restored is not null) await SelectAsync(restored, false, activateSelection: false);
            }
        }
        catch (Exception ex) when (ex is ResourceException or IOException or UnauthorizedAccessException)
        {
            Ready = false;
            Error = ex.Message;
        }
        HasActiveSelection = false;
        if (Settings.Mode != ReadingMode.Tafsir && Settings.Presentation == TafsirPresentation.EdgeDrawer) Tafsir = "";
        NotifyAll();
    }

    public async Task NavigateAsync(int page, AyahId? target = null, double offset = 0, int textOffset = 0, bool activateTarget = true)
    {
        if (!Ready || page is < 1 or > 604) return;
        ++locationRevision;
        ++tafsirRevision;
        Tafsir = "";
        navigation?.Cancel();
        navigation?.Dispose();
        navigation = new();
        var token = navigation.Token;
        IsBusy = true;
        NotifyAll();
        try
        {
            var verses = await content.GetPageAsync(page, token);
            var facing = Settings.Mode == ReadingMode.Mushaf || (Settings.Mode == ReadingMode.AdaptiveBook && page <= 2)
                ? await content.GetPageAsync(page % 2 == 0 ? page - 1 : Math.Min(604, page + 1), token) : [];
            var continuous = Settings.Mode == ReadingMode.AdaptiveBook && verses.Count > 0
                ? await ReadContinuousSourceAsync(new(target ?? verses[0].Id, offset, textOffset), token) : null;
            token.ThrowIfCancellationRequested();
            if (verses.Count == 0) throw new ResourceException($"Content page {page} is empty.");
            Page = page;
            Verses = verses;
            FacingVerses = facing;
            Continuous = continuous;
            presentedContinuous = null;
            SelectedVerse = target.HasValue
                ? verses.Concat(facing).FirstOrDefault(v => v.Id == target.Value) ?? verses[0]
                : verses[0];
            HasActiveSelection = target.HasValue && activateTarget;
            if (facing.Count > 0 && SelectedVerse.Page == facing[0].Page)
            {
                (Verses, FacingVerses) = (FacingVerses, Verses);
                Page = SelectedVerse.Page;
            }
            Position = new(SelectedVerse.Id, offset, textOffset);
            Error = null;
            await LoadTafsirAsync();
            token.ThrowIfCancellationRequested();
            await SaveAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ResourceException ex) { Error = ex.Message; }
        finally
        {
            if (!token.IsCancellationRequested) { IsBusy = false; NotifyAll(); }
        }
    }

    public async Task GoToAsync(AyahId id, bool activateSelection = true)
    {
        var revision = ++locationRevision;
        try
        {
            var page = await content.FindPageAsync(id);
            if (page is not null && revision == locationRevision) await NavigateAsync(page.Value, id, activateTarget: activateSelection);
        }
        catch (ResourceException ex) { Error = ex.Message; NotifyAll(); }
    }

    public async Task SelectAsync(Verse verse, bool userInitiated = true, bool activateSelection = true)
    {
        if (!LoadedVerses().Any(v => v.Id == verse.Id)) return;
        SelectedVerse = verse;
        HasActiveSelection = activateSelection;
        if (Settings.Mode == ReadingMode.AdaptiveBook)
        {
            if (userInitiated) Settings = Settings with { TafsirOpen = true };
            Tafsir = "";
            NotifyAll();
            await LoadTafsirAsync();
            await SaveAsync();
            NotifyAll();
            return;
        }
        Page = verse.Page;
        if (FacingVerses.Count > 0 && FacingVerses[0].Page == verse.Page)
        {
            (Verses, FacingVerses) = (FacingVerses, Verses);
        }
        else if (Verses.Count == 0 || Verses[0].Page != verse.Page)
        {
            try { Verses = await content.GetPageAsync(verse.Page); }
            catch (ResourceException ex) { Error = ex.Message; }
        }
        if (userInitiated && Settings.Mode == ReadingMode.Mushaf) Settings = Settings with { TafsirOpen = true };
        Tafsir = "";
        NotifyAll();
        await LoadTafsirAsync();
        await SaveAsync();
        NotifyAll();
    }

    private async Task LoadTafsirAsync()
    {
        var revision = ++tafsirRevision;
        Tafsir = "";
        if (!Settings.TafsirOpen || SelectedVerse is null || (Settings.Mode != ReadingMode.Tafsir && Settings.Presentation == TafsirPresentation.EdgeDrawer && !HasActiveSelection)) return;
        try
        {
            var value = await content.GetTafsirAsync(SelectedVerse.Id);
            if (revision == tafsirRevision) Tafsir = value;
        }
        catch (ResourceException ex) { if (revision == tafsirRevision) Error = ex.Message; }
    }

    public async Task ClearSelectionAsync()
    {
        ++tafsirRevision;
        HasActiveSelection = false;
        SelectedVerse = null;
        Tafsir = "";
        NotifyAll();
        await SaveAsync();
    }

    public async Task UpdateSettingsAsync(ReaderSettings settings)
    {
        var previous = Settings;
        var activeSelection = HasActiveSelection;
        var next = settings with
        {
            Scale = double.IsFinite(settings.Scale) ? Math.Clamp(settings.Scale, ReadingLayout.MinComfortableScale, ReadingLayout.MaxManualScale) : previous.Scale,
            DrawerWidth = double.IsFinite(settings.DrawerWidth) ? Math.Clamp(settings.DrawerWidth, 300, 560) : previous.DrawerWidth,
            TafsirOpen = settings.Mode != previous.Mode && settings.Mode != ReadingMode.Tafsir && !settings.DrawerPinned ? false : settings.TafsirOpen
        };
        if (next == previous) return;
        Settings = next;
        if (Settings.Mode == ReadingMode.AdaptiveBook && Continuous is null)
        {
            await LoadContinuousWindowAsync(Position);
        }
        else if (previous.Mode == ReadingMode.AdaptiveBook && Settings.Mode != ReadingMode.AdaptiveBook)
        {
            var mode = Settings.Mode;
            var anchor = Position;
            var selected = SelectedVerse;
            var page = await content.FindPageAsync(anchor.Ayah);
            if (Settings.Mode == mode && page is { } p)
            {
                await NavigateAsync(p, anchor.Ayah, anchor.Offset, anchor.TextOffset, activateTarget: false);
                if (Settings.Mode == mode && selected is not null)
                {
                    SelectedVerse = selected;
                    HasActiveSelection = activeSelection;
                    await LoadTafsirAsync();
                }
            }
        }
        if ((Settings.Mode == ReadingMode.Mushaf || (Settings.Mode == ReadingMode.AdaptiveBook && Page <= 2)) && FacingVerses.Count == 0 && Verses.Count > 0)
        {
            var original = Verses;
            var requestedMode = Settings.Mode;
            try
            {
                var pairedPage = original[0].Page % 2 == 0 ? original[0].Page - 1 : Math.Min(604, original[0].Page + 1);
                var facing = await content.GetPageAsync(pairedPage);
                if (Settings.Mode == requestedMode && ReferenceEquals(original, Verses)) FacingVerses = facing;
            }
            catch (ResourceException ex) { Error = ex.Message; }
        }
        else if (Settings.Mode == ReadingMode.Tafsir)
        {
            FacingVerses = [];
            if (Verses.Count == 0 || Verses[0].Page != Page)
            {
                try { Verses = await content.GetPageAsync(Page); }
                catch (ResourceException ex) { Error = ex.Message; }
            }
        }
        HasActiveSelection = activeSelection;
        if (previous.TafsirOpen != Settings.TafsirOpen || previous.Presentation != Settings.Presentation || previous.Mode != Settings.Mode) await LoadTafsirAsync();
        await SaveAsync();
        NotifyAll();
    }

    public async Task ToggleBookmarkAsync()
    {
        if (SelectedVerse is null) return;
        if (!bookmarks.Add(SelectedVerse.Id)) bookmarks.Remove(SelectedVerse.Id);
        await SaveAsync();
        NotifyAll();
    }

    public async Task RememberPositionAsync(AyahId id, double offset, int textOffset = 0)
    {
        var source = LoadedVerses().FirstOrDefault(v => v.Id == id);
        if (!Ready || source is null) return;
        ContinuousScripture.RequireBoundary(source.Text, textOffset);
        Position = new(id, double.IsFinite(offset) ? Math.Clamp(offset, -10000, 10000) : 0, textOffset);
        if (Settings.Mode == ReadingMode.AdaptiveBook) Page = source.Page;
        await SaveAsync();
    }

    private IEnumerable<Verse> LoadedVerses() => Verses.Concat(FacingVerses)
        .Concat((Settings.Mode == ReadingMode.AdaptiveBook ? presentedContinuous : null)?.Spans.Select(s => s.Source) ?? [])
        .Concat(Continuous?.Spans.Select(s => s.Source) ?? []);

    public void BindPresentedContinuous(ContinuousScripture source)
    {
        if (Settings.Mode == ReadingMode.AdaptiveBook) presentedContinuous = source;
    }

    public async Task<ContinuousScripture> ReadContinuousSourceAsync(ReadingPosition anchor, CancellationToken token = default)
    {
        if (content is not IContinuousContentRepository stream)
            throw new ResourceException("This content provider does not support verified continuous reading.");
        return new(await stream.GetSequenceAsync(anchor.Ayah, ContinuousScripture.MaxVerses, token), anchor);
    }

    public async Task LoadContinuousWindowAsync(ReadingPosition anchor)
    {
        if (!Ready || Settings.Mode != ReadingMode.AdaptiveBook) return;
        ++locationRevision;
        ++tafsirRevision;
        navigation?.Cancel();
        navigation?.Dispose();
        navigation = new();
        var token = navigation.Token;
        IsBusy = true;
        Tafsir = "";
        NotifyAll();
        try
        {
            var continuous = await ReadContinuousSourceAsync(anchor, token);
            token.ThrowIfCancellationRequested();
            if (Settings.Mode != ReadingMode.AdaptiveBook) return;
            Continuous = continuous;
            presentedContinuous = null;
            Position = anchor;
            Error = null;
            await LoadTafsirAsync();
            token.ThrowIfCancellationRequested();
            await SaveAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ResourceException ex) { Error = ex.Message; }
        finally
        {
            if (!token.IsCancellationRequested) { IsBusy = false; NotifyAll(); }
        }
    }

    public async Task SaveAsync()
    {
        await saveGate.WaitAsync();
        try { await store.SaveAsync(new(Position, Settings, Bookmarks, SelectedVerse?.Id)); }
        catch (ResourceException ex) { Error = ex.Message; NotifyAll(); }
        finally { saveGate.Release(); }
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { navigation?.Cancel(); navigation?.Dispose(); saveGate.Dispose(); }
}
