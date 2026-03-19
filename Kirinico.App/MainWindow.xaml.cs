using Kirinico.App.Controls;
using Kirinico.App.Models;
using Kirinico.App.ViewModels;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using DependencyObject = System.Windows.DependencyObject;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FrameworkElement = System.Windows.FrameworkElement;
using Keyboard = System.Windows.Input.Keyboard;
using Key = System.Windows.Input.Key;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using Window = System.Windows.Window;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FormsColorDialog = System.Windows.Forms.ColorDialog;
using FormsDialogResult = System.Windows.Forms.DialogResult;

namespace Kirinico.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OpenImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.webp;*.tif;*.tiff;*.exif|すべてのファイル|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.LoadImageAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "画像を開く", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var defaultName = string.IsNullOrWhiteSpace(_viewModel.CurrentFilePath) || _viewModel.CurrentFilePath == "画像未選択"
            ? "output.png"
            : $"{System.IO.Path.GetFileNameWithoutExtension(_viewModel.CurrentFilePath)}.png";

        var dialog = new SaveFileDialog
        {
            Filter = "PNG 画像|*.png",
            FileName = defaultName,
        };

        if (!string.IsNullOrWhiteSpace(_viewModel.CurrentFilePath) && _viewModel.CurrentFilePath != "画像未選択")
        {
            var sourceDirectory = System.IO.Path.GetDirectoryName(_viewModel.CurrentFilePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                dialog.InitialDirectory = sourceDirectory;
            }
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.SaveCurrentImageAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "PNG 保存", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReprocessButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.Reprocess();

    private async void ExportSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON ファイル|*.json|すべてのファイル|*.*",
            FileName = "kirinico-settings.json",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.ExportSettingsAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "設定エクスポート", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON ファイル|*.json|すべてのファイル|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.ImportSettingsAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "設定インポート", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow(_viewModel)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void HandModeButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectMode(EditorMode.Hand);

    private void WandAddModeButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectMode(EditorMode.WandAddSeed);

    private void OutlineEyedropperModeButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginOutlineColorPick();

    private void BackgroundEyedropperModeButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginBackgroundColorPick();

    private void LineEyedropperModeButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginLineColorPick();

    private void WandRemoveModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SeedManagerWindow(_viewModel.BuildSeedPreviewItems(), _viewModel.DeleteSeed)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void ZoomComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomComboBox.SelectedItem is int zoom)
        {
            _viewModel.ApplyZoomText($"{zoom}%");
        }
    }

    private void ZoomComboBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.ApplyZoomText(ZoomComboBox.Text);

    private void ZoomComboBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.ApplyZoomText(ZoomComboBox.Text);
            e.Handled = true;
        }
    }

    private void SettingHelp_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string help)
        {
            _viewModel.SetHoverHelp(help);
        }
    }

    private void SettingHelp_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _viewModel.SetHoverHelp(null);

    private void OutlineColorPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(out var color))
        {
            _viewModel.OutlineColorHex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    private void BackgroundColorPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(out var color))
        {
            _viewModel.BackgroundColorHex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    private void LineColorPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(out var color))
        {
            _viewModel.LineColorHex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    private void OutlineThicknessTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            _viewModel.ApplyOutlineThicknessText(textBox.Text);
        }
    }

    private void OutlineThicknessTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        _viewModel.ApplyOutlineThicknessText(textBox.Text);
        e.Handled = true;
    }

    private void OriginalViewer_OnPointerPressed(object sender, ImagePointerEventArgs e)
    {
        _viewModel.UpdateOriginalHover(e.SourcePoint);
        _viewModel.BeginEditableInteraction(e.SourcePoint, e.LeftButtonPressed);
    }

    private void OriginalViewer_OnPointerMoved(object sender, ImagePointerEventArgs e)
    {
        _viewModel.UpdateOriginalHover(e.SourcePoint);
        _viewModel.ContinueEditableInteraction(e.SourcePoint, e.LeftButtonPressed);
    }

    private void OriginalViewer_OnPointerReleased(object sender, ImagePointerEventArgs e) => _viewModel.EndEditableInteraction();

    private void AlphaViewer_OnPointerPressed(object sender, ImagePointerEventArgs e)
    {
        _viewModel.UpdateAlphaHover(e.SourcePoint);
        _viewModel.BeginEditableInteraction(e.SourcePoint, e.LeftButtonPressed);
    }

    private void AlphaViewer_OnPointerMoved(object sender, ImagePointerEventArgs e)
    {
        _viewModel.UpdateAlphaHover(e.SourcePoint);
        _viewModel.ContinueEditableInteraction(e.SourcePoint, e.LeftButtonPressed);
    }

    private void AlphaViewer_OnPointerReleased(object sender, ImagePointerEventArgs e) => _viewModel.EndEditableInteraction();

    private void TrimapViewer_OnPointerPressed(object sender, ImagePointerEventArgs e) => _viewModel.UpdateTrimapHover(e.SourcePoint);

    private void TrimapViewer_OnPointerMoved(object sender, ImagePointerEventArgs e) => _viewModel.UpdateTrimapHover(e.SourcePoint);

    private void TrimapViewer_OnPointerReleased(object sender, ImagePointerEventArgs e) => _viewModel.ClearCoordinateInfo();

    private void ResultViewer_OnTitleClicked(object sender, EventArgs e) => _viewModel.ToggleResultViewerMode();

    private void FinalViewer_OnPointerPressed(object sender, ImagePointerEventArgs e)
    {
        _viewModel.UpdateFinalHover(e.DisplayPoint);
        _viewModel.BeginResultInteraction(e.SourcePoint, e.DisplayPoint, e.LeftButtonPressed);
    }

    private void FinalViewer_OnPointerMoved(object sender, ImagePointerEventArgs e) => _viewModel.UpdateFinalHover(e.DisplayPoint);

    private void FinalViewer_OnDoubleClicked(object sender, EventArgs e) => _viewModel.CycleDisplayedResultBackground();

    private void Viewer_OnPointerExited(object sender, EventArgs e) => _viewModel.ClearCoordinateInfo();

    private void BatchDropZone_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void BatchDropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is null || files.Length == 0)
        {
            return;
        }

        try
        {
            await _viewModel.RunBatchAsync(files);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "バッチ処理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (IsDropInsideBatch(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (TryGetSingleFile(e, out _))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (IsDropInsideBatch(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (!TryGetSingleFile(e, out var filePath))
        {
            return;
        }

        try
        {
            await _viewModel.LoadImageAsync(filePath);
            e.Handled = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "画像を開く", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }

    private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsEyedropperMode)
        {
            return;
        }

        if (IsPointerInsideViewer(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _viewModel.CancelEyedropperMode();
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.O:
                OpenImageButton_OnClick(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S:
                SaveImageButton_OnClick(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.R:
                ReprocessButton_OnClick(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private static bool TryGetSingleFile(DragEventArgs e, out string filePath)
    {
        filePath = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is null || files.Length != 1)
        {
            return false;
        }

        filePath = files[0];
        return true;
    }

    private bool IsDropInsideBatch(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, BatchDropZone))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsPointerInsideViewer(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ZoomPanImageViewer)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool TryPickColor(out System.Drawing.Color color)
    {
        var dialog = new FormsColorDialog
        {
            FullOpen = true,
            AllowFullOpen = true,
        };

        if (dialog.ShowDialog() != FormsDialogResult.OK)
        {
            color = default;
            return false;
        }

        color = dialog.Color;
        return true;
    }
}
