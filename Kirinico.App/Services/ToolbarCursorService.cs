using Kirinico.App.Models;
using System.IO;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace Kirinico.App.Services;

public static class ToolbarCursorService
{
    private static readonly Lazy<IReadOnlyDictionary<EditorMode, Cursor>> CursorMap = new(CreateCursorMap);

    public static Cursor GetCursor(EditorMode mode) => CursorMap.Value.TryGetValue(mode, out var cursor)
        ? cursor
        : Cursors.Arrow;

    public static string GetModeMessage(EditorMode mode) => mode switch
    {
        EditorMode.Hand => "ドラッグで表示位置を移動します。",
        EditorMode.WandAddSeed => "クリックした1点を背景 seed に追加します。",
        EditorMode.Eyedropper => "元画像または結果画像をクリックして色を取得します。",
        _ => string.Empty,
    };

    private static IReadOnlyDictionary<EditorMode, Cursor> CreateCursorMap() => new Dictionary<EditorMode, Cursor>
    {
        [EditorMode.Hand] = LoadCursor("hand.cur"),
        [EditorMode.WandAddSeed] = LoadCursor("wand_add.cur"),
        [EditorMode.Eyedropper] = LoadCursor("dropper.cur"),
    };

    private static Cursor LoadCursor(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cursors", fileName);
        if (!File.Exists(path))
        {
            return Cursors.Arrow;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new Cursor(stream);
    }
}
