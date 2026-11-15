using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuranReader.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace QuranReader.App;

/// <summary>Isolated read-only visual reference; no invented text or hit regions.</summary>
internal sealed class OfficialArtworkWindow : Window
{
    internal static readonly IReadOnlyDictionary<int, string> PagePins = new Dictionary<int, string>
    {
        [1] = "78aa4f5dcfab4fcf4b4b8fa8814990233320bca72520dc0982f92034cdaaf58f",
        [2] = "c48ba58f26f34e964dde19a2c49a53dbb9c380fc66d063e23e9f8e9da8544450",
        [3] = "375b1fc9284e724029d9da86dcb3779f4e6afdec9ca117dba9cb58a2d9575182",
        [4] = "c883936e3fc9a617d9492e9e11659177063de600a7501837f5f76a36b32f93ef",
        [42] = "7a8738c702a0f8af86ec105af297c758b22873b1c5f2e904b98da971b047aef2",
        [187] = "4d6ad720b12883b2465188ee799ad6dfd07c6438193b37a30680bcc4c7f0458e",
        [604] = "511a9b427240a56777d2d5ddae71ed4af31be5e6fa80a9be3b7cea807ce22437"
    };
    private readonly Grid root = new() { Padding = new(16), RowSpacing = 12 };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly ComboBox picker = new() { Width = 140, ItemsSource = PagePins.Keys.ToArray(), SelectedIndex = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly DispatcherTimer resize = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private FileStream? guard;
    private int revision;
    private bool closed;
    public bool Rendered { get; private set; }
    public int CurrentPage => picker.SelectedItem is int page ? page : 1;
    public string? Failure { get; private set; }
    internal FrameworkElement CaptureSurface => root;

    public OfficialArtworkWindow()
    {
        Title = "Official 1441 Hafs artwork — visual reference only";
        AppWindow.Resize(new(1000, 960));
        var palette = ReaderPalette.For(ReadingTheme.Light);
        root.RequestedTheme = ElementTheme.Light;
        root.Background = palette.ReadingSurface;
        picker.Foreground = palette.ScriptureForeground;
        picker.Background = palette.ToolbarSurface;
        picker.BorderBrush = palette.BorderColor;
        status.Foreground = palette.MetadataForeground;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var title = new TextBlock { Text = "KFGQPC 1441 AH · Hafs39__DM · original artwork colours", FontSize = 18, Foreground = palette.ScriptureForeground };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        top.Children.Add(picker); top.Children.Add(title);
        root.Children.Add(top); root.Children.Add(status); root.Children.Add(image);
        Grid.SetRow(status, 1); Grid.SetRow(image, 2);
        Content = root;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, "Official reference page");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, "Official page artwork; no selectable scripture or word hitboxes");
        picker.SelectionChanged += async (_, _) => await RenderPageAsync();
        root.Loaded += async (_, _) => await RenderPageAsync();
        root.SizeChanged += (_, _) => { resize.Stop(); resize.Start(); };
        resize.Tick += async (_, _) => { resize.Stop(); await RenderPageAsync(); };
        Closed += (_, _) => { closed = true; ++revision; resize.Stop(); guard?.Dispose(); guard = null; image.Source = null; };
    }

    internal async Task SelectPageAsync(int page)
    {
        picker.SelectedItem = page;
        await RenderPageAsync();
    }

    private async Task RenderPageAsync()
    {
        if (closed || root.ActualHeight <= 0) return;
        var current = ++revision;
        var page = CurrentPage;
        Rendered = false;
        Failure = null;
        image.Source = null;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranReader", "Reference", "1441");
        var path = Path.Combine(directory, $"{page:000}.ai");
        status.Text = $"Loading verified artwork p. {page}…";
        FileStream? nextGuard = null;
        try
        {
            nextGuard = await Task.Run(() =>
            {
                if (!File.Exists(path)) throw new ResourceException("Reference artwork is not installed. Run tools/prepare_official_artwork.py with the complete pinned official archive. The adaptive reader works independently.");
                var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    var hash = Convert.ToHexString(SHA256.HashData(file));
                    if (!hash.Equals(PagePins[page], StringComparison.OrdinalIgnoreCase))
                        throw new ResourceException("Official artwork page failed its pinned SHA-256. Restore the verified local reference; no substitute was loaded.");
                    return file;
                }
                catch { file.Dispose(); throw; }
            });
            var document = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            if (document.PageCount != 1) throw new ResourceException("Expected one page in the official PDF-compatible artwork file.");
            using var pdfPage = document.GetPage(0);
            using var output = new InMemoryRandomAccessStream();
            var targetWidth = (uint)Math.Clamp((root.ActualWidth - 32) * (root.XamlRoot?.RasterizationScale ?? 1), 300, 2400);
            await pdfPage.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = targetWidth });
            if (current != revision || closed) return;
            output.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(output);
            if (current != revision || closed) return;
            guard?.Dispose(); guard = nextGuard; nextGuard = null;
            image.Source = bitmap;
            Rendered = true;
            status.Text = $"Official artwork p. {page} · {pdfPage.Size.Width:0.###} × {pdfPage.Size.Height:0.###} DIP · full page box, no cropping/inversion · visual comparison only; word/ayah mapping is NOT implemented.";
        }
        catch (Exception ex) when (ex is ResourceException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (current != revision || closed) return;
            Failure = ex.Message;
            status.Text = $"Reference unavailable: {ex.Message}";
        }
        finally { nextGuard?.Dispose(); }
    }
}
