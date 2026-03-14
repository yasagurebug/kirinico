using Kirinico.App.Models;
using System.Windows.Media.Imaging;
using ImageSource = System.Windows.Media.ImageSource;

namespace Kirinico.App.Services;

public static class ToolbarIconService
{
    public static ImageSource FileOpenIcon { get; } = LoadPngImage("file_open");

    public static ImageSource SaveIcon { get; } = LoadPngImage("save");

    public static ImageSource SyncIcon { get; } = LoadPngImage("sync");

    public static ImageSource HandIcon { get; } = LoadPngImage("hand");

    public static ImageSource WandAddIcon { get; } = LoadPngImage("wand_add");

    public static ImageSource WandRemoveIcon { get; } = LoadPngImage("wand_remove");

    public static ImageSource DropperIcon { get; } = LoadPngImage("dropper");

    public static ImageSource PaletteIcon { get; } = LoadPngImage("palette");

    public static ImageSource GetCursorIcon(EditorMode mode) => mode switch
    {
        EditorMode.Hand => HandIcon,
        EditorMode.WandAddSeed => WandAddIcon,
        EditorMode.Eyedropper => DropperIcon,
        _ => HandIcon,
    };

    private static ImageSource LoadPngImage(string imageName)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Kirinico.App;component/Assets/ToolbarIcons/{imageName}.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
