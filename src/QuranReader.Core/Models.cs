namespace QuranReader.Core;

public readonly record struct AyahId(int Surah, int Ayah)
{
    public override string ToString() => $"{Surah}:{Ayah}";
}

public sealed record Verse(AyahId Id, string SurahName, int Page, int Juz, string Text);
public sealed record Surah(int Number, string ArabicName, string EnglishName, int FirstPage)
{
    public override string ToString() => $"{Number}. {EnglishName} · {ArabicName}";
}

public enum ReadingTheme { Light = 0, Sepia = 1, Dark = 2, System = 3 }
public enum ReadingMode { Tafsir, Mushaf, AdaptiveBook }
public enum TafsirPresentation { FocusedPopup = 0, SlidingSidebar = 1, BottomSheet = 2, EdgeDrawer = 3 }
public sealed record ReadingPosition(AyahId Ayah, double Offset = 0, int TextOffset = 0);
public sealed record ReaderSettings(ReadingTheme Theme = ReadingTheme.Sepia, double Scale = 28, bool TafsirOpen = false,
    ReadingMode Mode = ReadingMode.Tafsir, bool DrawerPinned = false, double DrawerWidth = 380, bool ReduceMotion = false, bool FitSpread = true,
    TafsirPresentation Presentation = TafsirPresentation.FocusedPopup);
public sealed record UserState(ReadingPosition Position, ReaderSettings Settings, IReadOnlyList<AyahId> Bookmarks, AyahId? SelectedAyah = null)
{
    public static UserState Default => new(new(new(1, 1)), new(), []);
}

public sealed class ResourceException(string message, Exception? inner = null) : Exception(message, inner);

public interface IContentRepository
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default);
    Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default);
    Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default);
}

public interface IUserStateStore
{
    Task<UserState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserState state, CancellationToken cancellationToken = default);
}

public interface IContinuousContentRepository : IContentRepository
{
    Task<IReadOnlyList<Verse>> GetSequenceAsync(AyahId start, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Verse>> GetPrecedingAsync(AyahId before, int limit, CancellationToken cancellationToken = default);
}
