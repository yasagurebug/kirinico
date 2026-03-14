using CommunityToolkit.Mvvm.ComponentModel;
using Kirinico.App.Models;
using Kirinico.App.Services;
using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Cursor = System.Windows.Input.Cursor;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPoint = System.Windows.Point;

namespace Kirinico.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string ResultViewerBackgroundHelpText = "ダブルクリックで背景色を変更";

    private readonly CharacterCutoutProcessor _processor = new();
    private readonly BatchImageProcessor _batchProcessor;
    private readonly object _syncRoot = new();
    private readonly DispatcherTimer _seedBlinkTimer;

    private Mat? _sourceImage;
    private Mat? _backgroundSeedAddMap;
    private CutoutResult? _latestResult;
    private CancellationTokenSource? _renderCts;
    private int _renderVersion;
    private bool _syncingDimensions;
    private bool _isDirty;
    private bool _suspendPreviewInvalidation;

    private BitmapSource? _originalImage;
    private BitmapSource? _alphaImage;
    private BitmapSource? _finalImage;
    private BitmapSource? _originalEditOverlayImage;
    private BitmapSource? _alphaEditOverlayImage;
    private string _currentFilePath = "画像未選択";
    private string _coordinateStatusText = string.Empty;
    private string _statusText = "画像を開いてください。";
    private string _hoverHelpText = string.Empty;
    private bool _isResultViewerBackgroundHelpActive;
    private string _previewStageText = "待機";
    private string _batchStatusText = "ここに複数画像をドロップすると、現在の設定で一括処理します。";
    private double _previewProgressPercent;
    private double _batchProgressPercent;
    private bool _isPreviewBusy;
    private bool _isBatchBusy;
    private BackgroundSpecificationMode _selectedBackgroundSpecificationMode = BackgroundSpecificationMode.ColorRange;
    private string _backgroundColorHex = "#FFFFFF";
    private double _extraction = 0.7d;
    private double _noiseRemoval = 0.35d;
    private double _scanWidth = 5d;
    private LinePolarity _selectedLinePolarity = LinePolarity.Unspecified;
    private double _scalePercent = 100d;
    private int _outputWidth;
    private int _outputHeight;
    private bool _outlineEnabled;
    private string _outlineColorHex = "#000000";
    private double _outlineThickness = 1d;
    private double _outlineThicknessSliderPosition = 15.019048213408957d;
    private string _outlineThicknessText = "1.0";
    private EditorMode _selectedEditorMode = EditorMode.Hand;
    private bool _autoReprocess = true;
    private string _zoomPercentText = "100%";
    private double _resultCoordinateScaleX = 1d;
    private double _resultCoordinateScaleY = 1d;
    private ViewBackgroundKind _finalBackgroundKind = ViewBackgroundKind.Checker;
    private int _seedBlinkHue;
    private bool _isResultShowingAlpha;
    private ColorPickTarget _activeColorPickTarget = ColorPickTarget.Outline;

    public MainWindowViewModel()
    {
        _batchProcessor = new BatchImageProcessor(_processor);
        _seedBlinkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(420),
        };
        _seedBlinkTimer.Tick += OnSeedBlinkTimerTick;
        _seedBlinkTimer.Start();
        SharedViewport.PropertyChanged += (_, _) => UpdateZoomPercentText();
        UpdateZoomPercentText();
        UpdateModeState(EditorMode.Hand);
    }

    public IReadOnlyList<int> ZoomPresetOptions { get; } = [10, 50, 100, 200, 500];

    public IReadOnlyList<SelectionOption<BackgroundSpecificationMode>> BackgroundSpecificationModeOptions { get; } =
    [
        new SelectionOption<BackgroundSpecificationMode>(BackgroundSpecificationMode.ManualSeed, "手動seed"),
        new SelectionOption<BackgroundSpecificationMode>(BackgroundSpecificationMode.ColorRange, "色域指定"),
    ];

    public IReadOnlyList<SelectionOption<LinePolarity>> LinePolarityOptions { get; } =
    [
        new SelectionOption<LinePolarity>(LinePolarity.Black, "暗い色"),
        new SelectionOption<LinePolarity>(LinePolarity.White, "明るい色"),
        new SelectionOption<LinePolarity>(LinePolarity.Unspecified, "指定なし"),
    ];

    public ViewportState SharedViewport { get; } = new();

    public BitmapSource? OriginalImage
    {
        get => _originalImage;
        private set => SetProperty(ref _originalImage, value);
    }

    public BitmapSource? AlphaImage
    {
        get => _alphaImage;
        private set => SetProperty(ref _alphaImage, value);
    }

    public BitmapSource? FinalImage
    {
        get => _finalImage;
        private set => SetProperty(ref _finalImage, value);
    }

    public BitmapSource? DisplayedResultImage => IsResultShowingAlpha ? AlphaImage : FinalImage;

    public BitmapSource? OriginalEditOverlayImage
    {
        get => _originalEditOverlayImage;
        private set => SetProperty(ref _originalEditOverlayImage, value);
    }

    public BitmapSource? AlphaEditOverlayImage
    {
        get => _alphaEditOverlayImage;
        private set => SetProperty(ref _alphaEditOverlayImage, value);
    }

    public string CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (SetProperty(ref _currentFilePath, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string WindowTitle => string.IsNullOrWhiteSpace(CurrentFilePath) || CurrentFilePath == "画像未選択"
        ? "Kirinico 切り抜きツール"
        : $"Kirinico 切り抜きツール - {CurrentFilePath}";

    public string AppVersionText
    {
        get
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "kirinico.exe";
            var fileName = Path.GetFileName(executablePath);
            var version = FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "0.0.0";
            return $"{fileName} ver {version}";
        }
    }

    public string CoordinateStatusText
    {
        get => _coordinateStatusText;
        private set => SetProperty(ref _coordinateStatusText, value);
    }

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_hoverHelpText))
            {
                return _hoverHelpText;
            }

            return _isResultViewerBackgroundHelpActive ? ResultViewerBackgroundHelpText : _statusText;
        }
    }

    public string BatchStatusText
    {
        get => _batchStatusText;
        private set => SetProperty(ref _batchStatusText, value);
    }

    public string PreviewStageText
    {
        get => _previewStageText;
        private set => SetProperty(ref _previewStageText, value);
    }

    public double PreviewProgressPercent
    {
        get => _previewProgressPercent;
        private set => SetProperty(ref _previewProgressPercent, value);
    }

    public bool IsPreviewBusy
    {
        get => _isPreviewBusy;
        private set
        {
            if (SetProperty(ref _isPreviewBusy, value))
            {
                OnPropertyChanged(nameof(PreviewActivityText));
            }
        }
    }

    public string PreviewActivityText => IsPreviewBusy ? "プレビュー処理中" : "待機";

    public double BatchProgressPercent
    {
        get => _batchProgressPercent;
        private set
        {
            if (SetProperty(ref _batchProgressPercent, value))
            {
                OnPropertyChanged(nameof(BatchProgressScale));
            }
        }
    }

    public double BatchProgressScale => Math.Clamp(BatchProgressPercent / 100d, 0d, 1d);

    public bool IsBatchBusy
    {
        get => _isBatchBusy;
        private set => SetProperty(ref _isBatchBusy, value);
    }

    public double Extraction
    {
        get => _extraction;
        set
        {
            if (SetProperty(ref _extraction, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public BackgroundSpecificationMode SelectedBackgroundSpecificationMode
    {
        get => _selectedBackgroundSpecificationMode;
        set
        {
            if (SetProperty(ref _selectedBackgroundSpecificationMode, value))
            {
                OnPropertyChanged(nameof(IsManualSeedBackgroundMode));
                OnPropertyChanged(nameof(IsColorRangeBackgroundMode));
                MarkPreviewDirty();
            }
        }
    }

    public bool IsManualSeedBackgroundMode => SelectedBackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed;

    public bool IsColorRangeBackgroundMode => SelectedBackgroundSpecificationMode == BackgroundSpecificationMode.ColorRange;

    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set
        {
            if (SetProperty(ref _backgroundColorHex, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public double NoiseRemoval
    {
        get => _noiseRemoval;
        set
        {
            if (SetProperty(ref _noiseRemoval, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public double ScanWidth
    {
        get => _scanWidth;
        set
        {
            var sanitized = Math.Clamp(Math.Round(value), 0d, 10d);
            if (SetProperty(ref _scanWidth, sanitized))
            {
                MarkPreviewDirty();
            }
        }
    }

    public LinePolarity SelectedLinePolarity
    {
        get => _selectedLinePolarity;
        set
        {
            if (SetProperty(ref _selectedLinePolarity, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public double ScalePercent
    {
        get => _scalePercent;
        set
        {
            var sanitized = Math.Clamp(value, 1d, 800d);
            if (!SetProperty(ref _scalePercent, sanitized))
            {
                return;
            }

            if (_syncingDimensions || _sourceImage is null)
            {
                return;
            }

            _syncingDimensions = true;
            _outputWidth = Math.Max(1, (int)Math.Round(_sourceImage.Width * (sanitized / 100d)));
            _outputHeight = Math.Max(1, (int)Math.Round(_sourceImage.Height * (sanitized / 100d)));
            OnPropertyChanged(nameof(OutputWidth));
            OnPropertyChanged(nameof(OutputHeight));
            _syncingDimensions = false;
            MarkPreviewDirty();
        }
    }

    public int OutputWidth
    {
        get => _outputWidth;
        set
        {
            var sanitized = Math.Max(1, value);
            if (!SetProperty(ref _outputWidth, sanitized))
            {
                return;
            }

            if (!_syncingDimensions && _sourceImage is not null)
            {
                _syncingDimensions = true;
                _outputHeight = Math.Max(1, (int)Math.Round(sanitized * (_sourceImage.Height / (double)_sourceImage.Width)));
                _scalePercent = sanitized * 100d / _sourceImage.Width;
                OnPropertyChanged(nameof(OutputHeight));
                OnPropertyChanged(nameof(ScalePercent));
                _syncingDimensions = false;
                MarkPreviewDirty();
            }
        }
    }

    public int OutputHeight
    {
        get => _outputHeight;
        set
        {
            var sanitized = Math.Max(1, value);
            if (!SetProperty(ref _outputHeight, sanitized))
            {
                return;
            }

            if (!_syncingDimensions && _sourceImage is not null)
            {
                _syncingDimensions = true;
                _outputWidth = Math.Max(1, (int)Math.Round(sanitized * (_sourceImage.Width / (double)_sourceImage.Height)));
                _scalePercent = _outputWidth * 100d / _sourceImage.Width;
                OnPropertyChanged(nameof(OutputWidth));
                OnPropertyChanged(nameof(ScalePercent));
                _syncingDimensions = false;
                MarkPreviewDirty();
            }
        }
    }

    public bool OutlineEnabled
    {
        get => _outlineEnabled;
        set
        {
            if (SetProperty(ref _outlineEnabled, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public string OutlineColorHex
    {
        get => _outlineColorHex;
        set
        {
            if (SetProperty(ref _outlineColorHex, value))
            {
                MarkPreviewDirty();
            }
        }
    }

    public double OutlineThickness
    {
        get => _outlineThickness;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 100d);
            if (SetProperty(ref _outlineThickness, sanitized))
            {
                _outlineThicknessSliderPosition = ConvertOutlineThicknessToSliderPosition(sanitized);
                _outlineThicknessText = sanitized.ToString("0.0");
                OnPropertyChanged(nameof(OutlineThicknessSliderPosition));
                OnPropertyChanged(nameof(OutlineThicknessText));
                MarkPreviewDirty();
            }
        }
    }

    public double OutlineThicknessSliderPosition
    {
        get => _outlineThicknessSliderPosition;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 100d);
            if (SetProperty(ref _outlineThicknessSliderPosition, sanitized))
            {
                var thickness = ConvertSliderPositionToOutlineThickness(sanitized);
                if (Math.Abs(_outlineThickness - thickness) > 0.0001d)
                {
                    _outlineThickness = thickness;
                    OnPropertyChanged(nameof(OutlineThickness));
                    _outlineThicknessText = thickness.ToString("0.0");
                    OnPropertyChanged(nameof(OutlineThicknessText));
                    MarkPreviewDirty();
                }
            }
        }
    }

    public string OutlineThicknessText
    {
        get => _outlineThicknessText;
        private set => SetProperty(ref _outlineThicknessText, value);
    }

    public EditorMode SelectedEditorMode
    {
        get => _selectedEditorMode;
        private set
        {
            if (SetProperty(ref _selectedEditorMode, value))
            {
                OnPropertyChanged(nameof(IsHandMode));
                OnPropertyChanged(nameof(IsWandAddMode));
                OnPropertyChanged(nameof(IsEyedropperMode));
                OnPropertyChanged(nameof(IsOutlineColorEyedropperMode));
                OnPropertyChanged(nameof(IsBackgroundColorEyedropperMode));
                OnPropertyChanged(nameof(EditableViewerCursor));
                OnPropertyChanged(nameof(ResultViewerCursor));
                OnPropertyChanged(nameof(EditableViewerPansWithLeftButton));
                OnPropertyChanged(nameof(ResultViewerPansWithLeftButton));
            }
        }
    }

    public bool IsHandMode => SelectedEditorMode == EditorMode.Hand;

    public bool IsWandAddMode => SelectedEditorMode == EditorMode.WandAddSeed;

    public bool IsEyedropperMode => SelectedEditorMode == EditorMode.Eyedropper;

    public bool IsOutlineColorEyedropperMode => IsEyedropperMode && _activeColorPickTarget == ColorPickTarget.Outline;

    public bool IsBackgroundColorEyedropperMode => IsEyedropperMode && _activeColorPickTarget == ColorPickTarget.Background;

    public bool AutoReprocess
    {
        get => _autoReprocess;
        set
        {
            if (SetProperty(ref _autoReprocess, value) && value && _isDirty)
            {
                QueueRender(immediate: true);
            }
        }
    }

    public string ZoomPercentText
    {
        get => _zoomPercentText;
        private set => SetProperty(ref _zoomPercentText, value);
    }

    public Brush OriginalViewerBackground => ViewerBrushes.Original;

    public double ResultCoordinateScaleX
    {
        get => _resultCoordinateScaleX;
        private set
        {
            if (SetProperty(ref _resultCoordinateScaleX, value))
            {
                OnPropertyChanged(nameof(DisplayedResultCoordinateScaleX));
            }
        }
    }

    public double ResultCoordinateScaleY
    {
        get => _resultCoordinateScaleY;
        private set
        {
            if (SetProperty(ref _resultCoordinateScaleY, value))
            {
                OnPropertyChanged(nameof(DisplayedResultCoordinateScaleY));
            }
        }
    }

    public double DisplayedResultCoordinateScaleX => 1d;

    public double DisplayedResultCoordinateScaleY => 1d;

    public Cursor EditableViewerCursor => ToolbarCursorService.GetCursor(SelectedEditorMode);

    public Cursor ResultViewerCursor => SelectedEditorMode is EditorMode.Eyedropper or EditorMode.WandAddSeed
        ? ToolbarCursorService.GetCursor(SelectedEditorMode)
        : ToolbarCursorService.GetCursor(EditorMode.Hand);

    public bool EditableViewerPansWithLeftButton => SelectedEditorMode == EditorMode.Hand;

    public bool ResultViewerPansWithLeftButton => SelectedEditorMode is not (EditorMode.Eyedropper or EditorMode.WandAddSeed);

    public ViewBackgroundKind FinalBackgroundKind
    {
        get => _finalBackgroundKind;
        private set
        {
            if (SetProperty(ref _finalBackgroundKind, value))
            {
                OnPropertyChanged(nameof(FinalViewerBackground));
            }
        }
    }

    public Brush FinalViewerBackground => IsResultShowingAlpha
        ? ViewerBrushes.AlphaFixed
        : ViewerBrushes.GetBrush(FinalBackgroundKind);

    public bool IsResultShowingAlpha
    {
        get => _isResultShowingAlpha;
        private set
        {
            if (SetProperty(ref _isResultShowingAlpha, value))
            {
                OnPropertyChanged(nameof(ResultViewerTitle));
                OnPropertyChanged(nameof(DisplayedResultImage));
                OnPropertyChanged(nameof(FinalViewerBackground));
                OnPropertyChanged(nameof(DisplayedResultCoordinateScaleX));
                OnPropertyChanged(nameof(DisplayedResultCoordinateScaleY));
            }
        }
    }

    public string ResultViewerTitle => IsResultShowingAlpha ? "アルファ" : "結果";

    public async Task LoadImageAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected image was not found.", path);
        }

        using var loaded = await Task.Run(() => ImageLoadService.LoadColorImage(path));
        if (loaded.Empty())
        {
            throw new InvalidOperationException("画像を読み込めませんでした。");
        }

        lock (_syncRoot)
        {
            _sourceImage?.Dispose();
            _sourceImage = loaded.Clone();

            _backgroundSeedAddMap?.Dispose();
            _backgroundSeedAddMap = new Mat(_sourceImage.Rows, _sourceImage.Cols, MatType.CV_8UC1, Scalar.All(0d));
            _backgroundSeedAddMap.Set(0, 0, 255);
        }

        CurrentFilePath = path;
        OriginalImage = BitmapSourceFactory.FromBgr(_sourceImage);
        var topLeftPixel = _sourceImage.At<Vec3b>(0, 0);
        _backgroundColorHex = new RgbColor(topLeftPixel.Item2, topLeftPixel.Item1, topLeftPixel.Item0).ToHex();
        OnPropertyChanged(nameof(BackgroundColorHex));
        SharedViewport.Reset();

        _syncingDimensions = true;
        _outputWidth = _sourceImage.Width;
        _outputHeight = _sourceImage.Height;
        _scalePercent = 100d;
        _syncingDimensions = false;
        OnPropertyChanged(nameof(OutputWidth));
        OnPropertyChanged(nameof(OutputHeight));
        OnPropertyChanged(nameof(ScalePercent));

        RefreshEditOverlay();
        UpdateModeState(EditorMode.Hand);
        QueueRender(immediate: true);
    }

    public async Task SaveCurrentImageAsync(string path)
    {
        if (_latestResult is null)
        {
            return;
        }

        using var saveMat = _latestResult.FinalRgba.Clone();
        await Task.Run(() =>
        {
            if (!Cv2.ImWrite(path, saveMat))
            {
                throw new InvalidOperationException("画像を保存できませんでした。");
            }
        });
    }

    public void Reprocess() => QueueRender(immediate: true);

    public void CycleFinalBackground()
    {
        FinalBackgroundKind = FinalBackgroundKind switch
        {
            ViewBackgroundKind.Checker => ViewBackgroundKind.Black,
            ViewBackgroundKind.Black => ViewBackgroundKind.White,
            ViewBackgroundKind.White => ViewBackgroundKind.Gray,
            ViewBackgroundKind.Gray => ViewBackgroundKind.Red,
            ViewBackgroundKind.Red => ViewBackgroundKind.Green,
            ViewBackgroundKind.Green => ViewBackgroundKind.Blue,
            _ => ViewBackgroundKind.Checker,
        };
    }

    public void CycleDisplayedResultBackground()
    {
        if (!IsResultShowingAlpha)
        {
            CycleFinalBackground();
        }
    }

    public void ToggleResultViewerMode()
    {
        IsResultShowingAlpha = !IsResultShowingAlpha;
        ClearCoordinateInfo();
    }

    public void SelectMode(EditorMode mode) => UpdateModeState(mode);

    public async Task ExportSettingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("保存先ファイルが指定されていません。", nameof(filePath));
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var snapshot = CreateSettingsSnapshot();
        var json = JsonSerializer.Serialize(snapshot, CreateJsonOptions());
        await File.WriteAllTextAsync(filePath, json);
        _statusText = $"設定を書き出しました: {filePath}";
        OnPropertyChanged(nameof(StatusText));
    }

    public async Task ImportSettingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("設定ファイルが見つかりません。", filePath);
        }

        var json = await File.ReadAllTextAsync(filePath);
        var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json, CreateJsonOptions())
            ?? throw new InvalidOperationException("設定ファイルを読み込めませんでした。");

        ApplySettingsSnapshot(snapshot);
        _statusText = $"設定を読み込みました: {filePath}";
        OnPropertyChanged(nameof(StatusText));
    }

    public void BeginOutlineColorPick()
    {
        _activeColorPickTarget = ColorPickTarget.Outline;
        OnPropertyChanged(nameof(IsOutlineColorEyedropperMode));
        OnPropertyChanged(nameof(IsBackgroundColorEyedropperMode));
        UpdateModeState(EditorMode.Eyedropper);
    }

    public void BeginBackgroundColorPick()
    {
        _activeColorPickTarget = ColorPickTarget.Background;
        OnPropertyChanged(nameof(IsOutlineColorEyedropperMode));
        OnPropertyChanged(nameof(IsBackgroundColorEyedropperMode));
        UpdateModeState(EditorMode.Eyedropper);
    }

    public IReadOnlyList<SeedPreviewItem> BuildSeedPreviewItems()
    {
        if (_sourceImage is null || _backgroundSeedAddMap is null || _backgroundSeedAddMap.Empty())
        {
            return [];
        }

        var items = new List<SeedPreviewItem>();
        var addSeedIndexer = _backgroundSeedAddMap.GetGenericIndexer<byte>();

        for (var y = 0; y < _backgroundSeedAddMap.Rows; y++)
        {
            for (var x = 0; x < _backgroundSeedAddMap.Cols; x++)
            {
                if (addSeedIndexer[y, x] == 0)
                {
                    continue;
                }

                items.Add(new SeedPreviewItem
                {
                    SeedPoint = new OpenCvSharp.Point(x, y),
                    PreviewImage = CreateSeedPreviewBitmap(_sourceImage, x, y),
                });
            }
        }

        return items;
    }

    public void DeleteSeed(OpenCvSharp.Point point)
    {
        if (_backgroundSeedAddMap is null || _backgroundSeedAddMap.Empty())
        {
            return;
        }

        if (point.X < 0 || point.Y < 0 || point.X >= _backgroundSeedAddMap.Cols || point.Y >= _backgroundSeedAddMap.Rows)
        {
            return;
        }

        _backgroundSeedAddMap.Set(point.Y, point.X, 0);
        RefreshEditOverlay();
        MarkPreviewDirty();
    }

    public void SetHoverHelp(string? message)
    {
        _hoverHelpText = message ?? string.Empty;
        OnPropertyChanged(nameof(StatusText));
    }

    public void ApplyZoomText(string? text)
    {
        var percent = ParsePercentageText(text, SharedViewport.Zoom * 100d);
        SharedViewport.Update(percent / 100d, SharedViewport.OffsetX, SharedViewport.OffsetY);
        UpdateZoomPercentText();
    }

    public void ApplyOutlineThicknessText(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("px", string.Empty).Trim();
        if (double.TryParse(normalized, out var parsed))
        {
            OutlineThickness = parsed;
            return;
        }

        OutlineThicknessText = OutlineThickness.ToString("0.0");
    }

    public void BeginEditableInteraction(WinPoint point, bool leftButtonPressed)
    {
        if (_sourceImage is null || !leftButtonPressed)
        {
            return;
        }

        var imageX = Math.Clamp((int)Math.Round(point.X), 0, _sourceImage.Width - 1);
        var imageY = Math.Clamp((int)Math.Round(point.Y), 0, _sourceImage.Height - 1);

        switch (SelectedEditorMode)
        {
            case EditorMode.WandAddSeed:
                ApplySeedPoint(_backgroundSeedAddMap, null, imageX, imageY);
                RefreshEditOverlay();
                MarkPreviewDirty();
                return;
            case EditorMode.Eyedropper:
                SampleColorFromOriginal(imageX, imageY);
                return;
        }
    }

    public void ContinueEditableInteraction(WinPoint point, bool leftButtonPressed) { }

    public void UpdateOriginalHover(WinPoint point)
    {
        if (_sourceImage is null)
        {
            CoordinateStatusText = string.Empty;
            return;
        }

        var x = Math.Clamp((int)Math.Round(point.X), 0, _sourceImage.Width - 1);
        var y = Math.Clamp((int)Math.Round(point.Y), 0, _sourceImage.Height - 1);
        var pixel = _sourceImage.At<Vec3b>(y, x);
        CoordinateStatusText = FormatCoordinateStatus("元画像", x, y, new RgbColor(pixel.Item2, pixel.Item1, pixel.Item0), 100d);
    }

    public void UpdateAlphaHover(WinPoint point)
    {
        if (_latestResult is null)
        {
            CoordinateStatusText = string.Empty;
            return;
        }

        var alphaMask = _latestResult.AlphaMask;
        var x = Math.Clamp((int)Math.Round(point.X), 0, alphaMask.Width - 1);
        var y = Math.Clamp((int)Math.Round(point.Y), 0, alphaMask.Height - 1);
        var value = alphaMask.At<byte>(y, x);
        var grayscale = new RgbColor(value, value, value);
        CoordinateStatusText = FormatCoordinateStatus("アルファ", x, y, grayscale, value * 100d / 255d);
    }

    public void UpdateFinalHover(WinPoint point)
    {
        if (_latestResult is null)
        {
            CoordinateStatusText = string.Empty;
            SetResultViewerBackgroundHelpActive(false);
            return;
        }

        var finalRgba = _latestResult.FinalRgba;
        var x = Math.Clamp((int)Math.Round(point.X), 0, finalRgba.Width - 1);
        var y = Math.Clamp((int)Math.Round(point.Y), 0, finalRgba.Height - 1);
        var pixel = finalRgba.At<Vec4b>(y, x);
        CoordinateStatusText = FormatCoordinateStatus("結果", x, y, new RgbColor(pixel.Item2, pixel.Item1, pixel.Item0), pixel.Item3 * 100d / 255d);
        SetResultViewerBackgroundHelpActive(SelectedEditorMode == EditorMode.Hand && !IsResultShowingAlpha);
    }

    public void BeginResultInteraction(WinPoint sourcePoint, WinPoint displayPoint, bool leftButtonPressed)
    {
        if (_latestResult is null || !leftButtonPressed || IsResultShowingAlpha)
        {
            return;
        }

        if (SelectedEditorMode == EditorMode.WandAddSeed && _sourceImage is not null)
        {
            var imageX = Math.Clamp((int)Math.Round(sourcePoint.X), 0, _sourceImage.Width - 1);
            var imageY = Math.Clamp((int)Math.Round(sourcePoint.Y), 0, _sourceImage.Height - 1);
            ApplySeedPoint(_backgroundSeedAddMap, null, imageX, imageY);
            RefreshEditOverlay();
            MarkPreviewDirty();
            return;
        }

        if (SelectedEditorMode != EditorMode.Eyedropper)
        {
            return;
        }

        var finalRgba = _latestResult.FinalRgba;
        var x = Math.Clamp((int)Math.Round(displayPoint.X), 0, finalRgba.Width - 1);
        var y = Math.Clamp((int)Math.Round(displayPoint.Y), 0, finalRgba.Height - 1);
        var pixel = finalRgba.At<Vec4b>(y, x);
        ApplyPickedColor(new RgbColor(pixel.Item2, pixel.Item1, pixel.Item0));
    }

    public void ClearCoordinateInfo()
    {
        CoordinateStatusText = string.Empty;
        SetResultViewerBackgroundHelpActive(false);
    }

    public void EndEditableInteraction() { }

    public async Task RunBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        if (IsBatchBusy)
        {
            return;
        }

        IsBatchBusy = true;
        BatchProgressPercent = 0d;
        BatchStatusText = "準備中...";

        try
        {
            var progress = new Progress<BatchProgress>(report =>
            {
                var percent = report.TotalCount == 0 ? 0d : report.CompletedCount * 100d / report.TotalCount;
                BatchProgressPercent = percent;
                var statusLine = string.IsNullOrWhiteSpace(report.CurrentFileName)
                    ? report.StatusText
                    : $"{report.StatusText}\n{report.CurrentFileName}";
                BatchStatusText = statusLine;
            });

            var summary = await _batchProcessor.ProcessAsync(files, BuildParameters(), progress, cancellationToken);
            var total = summary.Items.Count;
            BatchProgressPercent = 100d;
            BatchStatusText = summary.FailedCount > 0 || summary.SkippedCount > 0
                ? $"完了(一部失敗): {summary.SucceededCount} / {total} 失敗 {summary.FailedCount} 件, スキップ {summary.SkippedCount} 件"
                : $"完了: {summary.SucceededCount} / {total}";
        }
        finally
        {
            IsBatchBusy = false;
        }
    }

    public void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _latestResult?.Dispose();
        _sourceImage?.Dispose();
        _backgroundSeedAddMap?.Dispose();
        _seedBlinkTimer.Stop();
        _seedBlinkTimer.Tick -= OnSeedBlinkTimerTick;
    }

    private void ApplySeedPoint(Mat? targetMap, Mat? oppositeMap, int x, int y)
    {
        if (targetMap is null)
        {
            return;
        }

        targetMap.Set(y, x, 255);
        oppositeMap?.Set(y, x, 0);
    }

    private void RefreshEditOverlay()
    {
        if (_sourceImage is null)
        {
            OriginalEditOverlayImage = null;
            AlphaEditOverlayImage = null;
            return;
        }

        var rows = _sourceImage.Rows;
        var cols = _sourceImage.Cols;
        using var originalOverlay = new Mat(rows, cols, MatType.CV_8UC4, Scalar.All(0d));
        using var alphaOverlay = new Mat(rows, cols, MatType.CV_8UC4, Scalar.All(0d));
        var addSeedIndexer = _backgroundSeedAddMap?.GetGenericIndexer<byte>();
        var originalOverlayIndexer = originalOverlay.GetGenericIndexer<Vec4b>();
        var seedColor = CreateSeedOverlayColor(_seedBlinkHue);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var addSeed = addSeedIndexer is not null && addSeedIndexer[y, x] > 0;

                if (addSeed)
                {
                    originalOverlayIndexer[y, x] = seedColor;
                }
            }
        }

        OriginalEditOverlayImage = BitmapSourceFactory.FromBgra(originalOverlay);
        AlphaEditOverlayImage = BitmapSourceFactory.FromBgra(alphaOverlay);
    }

    private void OnSeedBlinkTimerTick(object? sender, EventArgs e)
    {
        if (_backgroundSeedAddMap is null)
        {
            return;
        }

        var hasAddSeeds = _backgroundSeedAddMap is not null && Cv2.CountNonZero(_backgroundSeedAddMap) > 0;
        if (!hasAddSeeds)
        {
            return;
        }

        _seedBlinkHue = (_seedBlinkHue + 20) % 360;
        RefreshEditOverlay();
    }

    private void SetResultViewerBackgroundHelpActive(bool active)
    {
        if (_isResultViewerBackgroundHelpActive == active)
        {
            return;
        }

        _isResultViewerBackgroundHelpActive = active;
        OnPropertyChanged(nameof(StatusText));
    }

    private void SampleColorFromOriginal(int x, int y)
    {
        if (_sourceImage is null)
        {
            return;
        }

        var pixel = _sourceImage.At<Vec3b>(y, x);
        ApplyPickedColor(new RgbColor(pixel.Item2, pixel.Item1, pixel.Item0));
    }

    private void ApplyPickedColor(RgbColor color)
    {
        string statusText;
        switch (_activeColorPickTarget)
        {
            case ColorPickTarget.Background:
                BackgroundColorHex = color.ToHex();
                statusText = $"背景色を取得しました: {BackgroundColorHex}";
                MarkPreviewDirty();
                break;
            default:
                OutlineColorHex = color.ToHex();
                statusText = $"縁取り色を取得しました: {OutlineColorHex}";
                break;
        }

        UpdateModeState(EditorMode.Hand);
        _statusText = statusText;
        OnPropertyChanged(nameof(StatusText));
    }

    private void QueueRender(bool immediate = false)
    {
        if (_sourceImage is null)
        {
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        var renderVersion = Interlocked.Increment(ref _renderVersion);
        IsPreviewBusy = true;
        PreviewProgressPercent = 0d;
        PreviewStageText = immediate ? "再計算を開始" : "再計算待機";
        _statusText = "プレビューを更新しています。";
        OnPropertyChanged(nameof(StatusText));
        _ = RenderPreviewAsync(_renderCts.Token, renderVersion, immediate ? 0 : 120);
    }

    private void MarkPreviewDirty()
    {
        if (_suspendPreviewInvalidation)
        {
            _isDirty = true;
            return;
        }

        if (_sourceImage is null)
        {
            return;
        }

        _isDirty = true;
        if (AutoReprocess)
        {
            QueueRender(false);
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        Interlocked.Increment(ref _renderVersion);
        IsPreviewBusy = false;
        PreviewProgressPercent = 0d;
        PreviewStageText = "再処理待ち";
        _statusText = "設定を変更しました。再処理を押してください。";
        OnPropertyChanged(nameof(StatusText));
    }

    private AppSettingsSnapshot CreateSettingsSnapshot() => new()
    {
        BackgroundSpecificationMode = SelectedBackgroundSpecificationMode,
        BackgroundColorHex = BackgroundColorHex,
        Extraction = Extraction,
        NoiseRemoval = NoiseRemoval,
        ScanWidth = ScanWidth,
        LinePolarity = SelectedLinePolarity,
        ScalePercent = ScalePercent,
        OutputWidth = OutputWidth,
        OutputHeight = OutputHeight,
        OutlineEnabled = OutlineEnabled,
        OutlineColorHex = OutlineColorHex,
        OutlineThickness = OutlineThickness,
        AutoReprocess = AutoReprocess,
    };

    private void ApplySettingsSnapshot(AppSettingsSnapshot snapshot)
    {
        _suspendPreviewInvalidation = true;
        try
        {
            SelectedBackgroundSpecificationMode = snapshot.BackgroundSpecificationMode;
            BackgroundColorHex = snapshot.BackgroundColorHex;
            Extraction = snapshot.Extraction;
            NoiseRemoval = snapshot.NoiseRemoval;
            ScanWidth = snapshot.ScanWidth;
            SelectedLinePolarity = snapshot.LinePolarity;
            ScalePercent = snapshot.ScalePercent;
            OutputWidth = Math.Max(1, snapshot.OutputWidth);
            OutputHeight = Math.Max(1, snapshot.OutputHeight);
            OutlineEnabled = snapshot.OutlineEnabled;
            OutlineColorHex = snapshot.OutlineColorHex;
            OutlineThickness = snapshot.OutlineThickness;
            AutoReprocess = snapshot.AutoReprocess;
        }
        finally
        {
            _suspendPreviewInvalidation = false;
        }

        if (_sourceImage is null)
        {
            return;
        }

        if (AutoReprocess)
        {
            QueueRender(immediate: true);
            return;
        }

        _isDirty = true;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        Interlocked.Increment(ref _renderVersion);
        IsPreviewBusy = false;
        PreviewProgressPercent = 0d;
        PreviewStageText = "再処理待ち";
        OnPropertyChanged(nameof(StatusText));
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private async Task RenderPreviewAsync(CancellationToken cancellationToken, int renderVersion, int delayMs)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }

            if (renderVersion != Volatile.Read(ref _renderVersion))
            {
                return;
            }

            Mat sourceClone;
            ManualEditMaps? manualMaps = null;
            CutoutParameters parameters;

            lock (_syncRoot)
            {
                sourceClone = _sourceImage?.Clone() ?? throw new InvalidOperationException("元画像が読み込まれていません。");
                if (_backgroundSeedAddMap is not null)
                {
                    manualMaps = new ManualEditMaps
                    {
                        BackgroundSeedAddMap = _backgroundSeedAddMap?.Clone(),
                    };
                }

                parameters = BuildParameters();
            }

            if (parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed && !HasAnyBackgroundSeed(manualMaps))
            {
                sourceClone.Dispose();
                manualMaps?.Dispose();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (renderVersion != Volatile.Read(ref _renderVersion))
                    {
                        return;
                    }

                    _latestResult?.Dispose();
                    _latestResult = null;
                    AlphaImage = null;
                    FinalImage = null;
                    OnPropertyChanged(nameof(DisplayedResultImage));
                    ResultCoordinateScaleX = 1d;
                    ResultCoordinateScaleY = 1d;
                    IsPreviewBusy = false;
                    PreviewProgressPercent = 0d;
                    PreviewStageText = "seedなし";
                    _statusText = "背景seedがありません。追加ワンドでseedを置いてください。";
                    OnPropertyChanged(nameof(StatusText));
                });
                return;
            }

            var progress = new Progress<ProcessingProgress>(report =>
            {
                if (renderVersion != Volatile.Read(ref _renderVersion))
                {
                    return;
                }

                IsPreviewBusy = true;
                PreviewProgressPercent = Math.Clamp(report.ProgressPercent, 0d, 100d);
                PreviewStageText = report.StageText;
                _statusText = $"プレビュー更新中: {report.StageText}";
                OnPropertyChanged(nameof(StatusText));
            });

            using (sourceClone)
            using (manualMaps)
            {
                var result = await Task.Run(() => _processor.Process(sourceClone, parameters, manualMaps, progress), cancellationToken);
                if (renderVersion != Volatile.Read(ref _renderVersion))
                {
                    result.Dispose();
                    return;
                }

                var alphaBitmap = BitmapSourceFactory.FromAlphaPreview(result.AlphaMask);
                var finalBitmap = BitmapSourceFactory.FromBgra(result.FinalRgba);
                var resultScaleX = _sourceImage is null ? 1d : result.FinalRgba.Width / (double)_sourceImage.Width;
                var resultScaleY = _sourceImage is null ? 1d : result.FinalRgba.Height / (double)_sourceImage.Height;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (renderVersion != Volatile.Read(ref _renderVersion))
                    {
                        result.Dispose();
                        return;
                    }

                    _latestResult?.Dispose();
                    _latestResult = result;
                    AlphaImage = alphaBitmap;
                    FinalImage = finalBitmap;
                    OnPropertyChanged(nameof(DisplayedResultImage));
                    ResultCoordinateScaleX = resultScaleX;
                    ResultCoordinateScaleY = resultScaleY;
                    IsPreviewBusy = false;
                    _isDirty = false;
                    PreviewProgressPercent = 0d;
                    PreviewStageText = "待機";
                    _statusText = $"プレビュー更新完了。背景 {result.ResolvedBackgroundColor.ToHex()}  出力 {result.FinalRgba.Width} x {result.FinalRgba.Height}";
                    OnPropertyChanged(nameof(StatusText));
                });
            }
        }
        catch (OperationCanceledException)
        {
            if (renderVersion != Volatile.Read(ref _renderVersion))
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPreviewBusy = false;
                PreviewProgressPercent = 0d;
                PreviewStageText = "待機";
                    _statusText = _isDirty ? "設定を変更しました。再処理を押してください。" : GetCurrentModeMessage();
                    OnPropertyChanged(nameof(StatusText));
                });
        }
        catch (Exception exception)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (renderVersion != Volatile.Read(ref _renderVersion))
                {
                    return;
                }

                IsPreviewBusy = false;
                PreviewProgressPercent = 0d;
                PreviewStageText = "エラー";
                _statusText = $"プレビュー更新失敗: {exception.Message}";
                OnPropertyChanged(nameof(StatusText));
            });
        }
    }

    private CutoutParameters BuildParameters()
    {
        var outlineColor = RgbColor.TryParseHex(OutlineColorHex, out var parsedOutline)
            ? parsedOutline
            : new RgbColor(0, 0, 0);

        return new CutoutParameters
        {
            BackgroundSpecificationMode = SelectedBackgroundSpecificationMode,
            BackgroundColor = RgbColor.TryParseHex(BackgroundColorHex, out var parsedBackground)
                ? parsedBackground
                : new RgbColor(255, 255, 255),
            Extraction = Extraction,
            NoiseRemoval = NoiseRemoval,
            ScanWidth = (int)Math.Round(ScanWidth),
            LinePolarity = SelectedLinePolarity,
            Resize = new ResizeOptions
            {
                Mode = ResizeMode.Scale,
                ScalePercent = ScalePercent,
                OutputWidth = OutputWidth,
                OutputHeight = OutputHeight,
            },
            Outline = new OutlineOptions
            {
                Enabled = OutlineEnabled,
                Color = outlineColor,
                Thickness = OutlineThickness,
            },
        };
    }

    private void UpdateModeState(EditorMode mode)
    {
        SelectedEditorMode = mode;
        _statusText = GetCurrentModeMessage();
        OnPropertyChanged(nameof(StatusText));
    }

    private string GetCurrentModeMessage()
    {
        if (SelectedEditorMode != EditorMode.Eyedropper)
        {
            return ToolbarCursorService.GetModeMessage(SelectedEditorMode);
        }

        return _activeColorPickTarget == ColorPickTarget.Background
            ? "元画像または結果画像をクリックして背景色を取得します。"
            : "元画像または結果画像をクリックして縁取り色を取得します。";
    }

    private void UpdateZoomPercentText() => ZoomPercentText = $"{SharedViewport.Zoom * 100d:0.#}%";

    private static bool HasAnyBackgroundSeed(ManualEditMaps? manualMaps)
    {
        return manualMaps?.BackgroundSeedAddMap is not null
            && !manualMaps.BackgroundSeedAddMap.Empty()
            && Cv2.CountNonZero(manualMaps.BackgroundSeedAddMap) > 0;
    }

    private static Vec4b CreateSeedOverlayColor(int hue)
    {
        var normalizedHue = ((hue % 360) + 360) % 360;
        var sector = normalizedHue / 60d;
        var fraction = sector - Math.Floor(sector);
        const double value = 1d;
        const double saturation = 1d;
        var p = value * (1d - saturation);
        var q = value * (1d - (saturation * fraction));
        var t = value * (1d - (saturation * (1d - fraction)));

        var (r, g, b) = (int)Math.Floor(sector) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new Vec4b(
            (byte)Math.Round(b * 255d),
            (byte)Math.Round(g * 255d),
            (byte)Math.Round(r * 255d),
            255);
    }

    private static double ParsePercentageText(string? text, double fallback)
    {
        var normalized = (text ?? string.Empty).Replace("%", string.Empty).Trim();
        return double.TryParse(normalized, out var parsed)
            ? Math.Clamp(parsed, 10d, 1200d)
            : Math.Clamp(fallback, 10d, 1200d);
    }

    private static string FormatCoordinateStatus(string label, int x, int y, RgbColor color, double alphaPercent)
        => $"{label} ({x},{y}) {color.ToHex()} {alphaPercent:0}%";

    private static double ConvertSliderPositionToOutlineThickness(double position)
    {
        if (position <= 0d)
        {
            return 0d;
        }

        return Math.Pow(101d, position / 100d) - 1d;
    }

    private static double ConvertOutlineThicknessToSliderPosition(double thickness)
    {
        if (thickness <= 0d)
        {
            return 0d;
        }

        return Math.Log(thickness + 1d, 101d) * 100d;
    }

    private static BitmapSource CreateSeedPreviewBitmap(Mat sourceImage, int x, int y)
    {
        const int sampleSize = 128;
        const int previewSize = 64;
        var half = sampleSize / 2;
        var fill = sourceImage.At<Vec3b>(Math.Clamp(y, 0, sourceImage.Rows - 1), Math.Clamp(x, 0, sourceImage.Cols - 1));
        using var sample = new Mat(sampleSize, sampleSize, MatType.CV_8UC3, new Scalar(fill.Item0, fill.Item1, fill.Item2));
        var sourceLeft = Math.Max(0, x - half);
        var sourceTop = Math.Max(0, y - half);
        var sourceRight = Math.Min(sourceImage.Cols, x + half);
        var sourceBottom = Math.Min(sourceImage.Rows, y + half);
        var sourceWidth = Math.Max(0, sourceRight - sourceLeft);
        var sourceHeight = Math.Max(0, sourceBottom - sourceTop);

        if (sourceWidth > 0 && sourceHeight > 0)
        {
            var destinationLeft = Math.Max(0, half - x);
            var destinationTop = Math.Max(0, half - y);
            using var sourceRoi = new Mat(sourceImage, new Rect(sourceLeft, sourceTop, sourceWidth, sourceHeight));
            using var destinationRoi = new Mat(sample, new Rect(destinationLeft, destinationTop, sourceWidth, sourceHeight));
            sourceRoi.CopyTo(destinationRoi);
        }

        using var preview = new Mat();
        Cv2.Resize(sample, preview, new OpenCvSharp.Size(previewSize, previewSize), 0d, 0d, InterpolationFlags.Area);
        return BitmapSourceFactory.FromBgr(preview);
    }
}
