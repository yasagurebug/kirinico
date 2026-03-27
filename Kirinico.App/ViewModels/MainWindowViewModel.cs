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
using Brushes = System.Windows.Media.Brushes;
using Cursor = System.Windows.Input.Cursor;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPoint = System.Windows.Point;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Visibility = System.Windows.Visibility;

namespace Kirinico.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string ResultViewerBackgroundHelpText = "ダブルクリックで背景色を変更";
    private const double FastPreviewScale = 0.25d;
    private const int PyMattingStartDebounceMs = 500;
    private readonly string _workerTempDirectory;

    private readonly CharacterCutoutProcessor _processor;
    private readonly BatchCoordinator _batchCoordinator;
    private readonly ImageDocument _imageDocument = new();
    private readonly PreviewSession _previewSession = new();
    private readonly object _syncRoot = new();
    private readonly DispatcherTimer _seedBlinkTimer;
    private readonly InternalSettings _internalSettings = new();
    private CancellationTokenSource? _presentationRefreshCts;
    private int _presentationRefreshVersion;
    private bool _syncingDimensions;
    private bool _suspendPreviewInvalidation;

    private BitmapSource? _originalImage;
    private BitmapSource? _trimapImage;
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
    private string _backgroundColorHex = "FFFFFF";
    private double _backgroundTolerance = 0.5d;
    private ContourSettingMethod _selectedContourSettingMethod = ContourSettingMethod.Width;
    private double _noiseRemoval = 0.3d;
    private double _contourTolerance = 0.4d;
    private double _maxContourWidth = 0.1d;
    private MattingMethod _selectedContourInferenceMethod = MattingMethod.Cf;
    private double _transparencyCut = 0.15d;
    private double _opaqueAlphaThreshold = 0.75d;
    private double _despillExpansion = 0.2d;
    private double _despillMix = 0.5d;
    private double _despillBrightness = 0.5d;
    private double _scalePercent = 100d;
    private ResizeInterpolationMode _selectedResizeInterpolation = ResizeInterpolationMode.Lanczos4;
    private int _outputWidth;
    private int _outputHeight;
    private bool _outlineEnabled;
    private string _outlineColorHex = "000000";
    private double _outlineThickness = 1d;
    private double _outlineThicknessSliderPosition = 15.019048213408957d;
    private string _outlineThicknessText = "1.0";
    private EditorMode _selectedEditorMode = EditorMode.Hand;
    private bool _autoReprocess = true;
    private bool _fastPreviewEnabled = true;
    private bool _isFastPreviewActive;
    private string _zoomPercentText = "100%";
    private double _resultCoordinateScaleX = 1d;
    private double _resultCoordinateScaleY = 1d;
    private ViewBackgroundKind _finalBackgroundKind = ViewBackgroundKind.Checker;
    private int _seedBlinkHue;
    private bool _isResultShowingAlpha;
    private ColorPickTarget _activeColorPickTarget = ColorPickTarget.Outline;
    private RgbColor? _eyedropperPreviewColor;
    private RgbColor? _coordinateStatusColor;

    public MainWindowViewModel()
    {
        _workerTempDirectory = Path.Combine(Path.GetTempPath(), "kirinico", "worker");
        _processor = new CharacterCutoutProcessor(new PythonWorkerAlphaMatteEstimator(new PythonWorkerOptions
        {
            WorkerExecutablePath = Path.Combine(AppContext.BaseDirectory, "python_worker", "Kirinico.PyWorker.exe"),
            WorkingDirectory = AppContext.BaseDirectory,
                TempDirectory = _workerTempDirectory,
        }));
        _batchCoordinator = new BatchCoordinator(new BatchImageProcessor(_processor));
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

    private Mat? _sourceImage => _imageDocument.SourceImage;

    private Mat? _backgroundSeedAddMap => _imageDocument.BackgroundSeedAddMap;

    private Mat? _latestTrimapMask => _imageDocument.LatestTrimapMask;

    private PreResizeCutoutResult? _cachedPreResizeResult => _imageDocument.CachedPreResizeResult;

    private CutoutResult? _latestResult => _imageDocument.LatestResult;

    public IReadOnlyList<int> ZoomPresetOptions { get; } = [10, 50, 100, 200, 500];

    public IReadOnlyList<SelectionOption<BackgroundSpecificationMode>> BackgroundSpecificationModeOptions { get; } =
    [
        new SelectionOption<BackgroundSpecificationMode>(BackgroundSpecificationMode.ManualSeed, "手動seed"),
        new SelectionOption<BackgroundSpecificationMode>(BackgroundSpecificationMode.ColorRange, "色域指定"),
    ];

    public IReadOnlyList<SelectionOption<ResizeInterpolationMode>> ResizeInterpolationOptions { get; } =
    [
        new SelectionOption<ResizeInterpolationMode>(ResizeInterpolationMode.Nearest, "最近傍"),
        new SelectionOption<ResizeInterpolationMode>(ResizeInterpolationMode.Linear, "線形"),
        new SelectionOption<ResizeInterpolationMode>(ResizeInterpolationMode.Cubic, "Cubic"),
        new SelectionOption<ResizeInterpolationMode>(ResizeInterpolationMode.Lanczos4, "Lanczos"),
        new SelectionOption<ResizeInterpolationMode>(ResizeInterpolationMode.Area, "Area"),
    ];

    public IReadOnlyList<SelectionOption<MattingMethod>> ContourInferenceMethodOptions { get; } =
    [
        new SelectionOption<MattingMethod>(MattingMethod.Cf, "高速"),
        new SelectionOption<MattingMethod>(MattingMethod.Knn, "精密"),
    ];

    public IReadOnlyList<SelectionOption<ContourSettingMethod>> ContourSettingMethodOptions { get; } =
    [
        new SelectionOption<ContourSettingMethod>(ContourSettingMethod.Width, "幅"),
        new SelectionOption<ContourSettingMethod>(ContourSettingMethod.ColorDifference, "色差"),
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

    public BitmapSource? TrimapImage
    {
        get => _trimapImage;
        private set => SetProperty(ref _trimapImage, value);
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

    public string AppExecutableName
    {
        get
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "kirinico.exe";
            return Path.GetFileName(executablePath);
        }
    }

    public string AppVersion
    {
        get
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "kirinico.exe";
            return FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "0.0.0";
        }
    }

    public string AuthorName => "yasagurebug";

    public string RepositoryUrl => "https://github.com/yasagurebug/kirinico";

    public string CoordinateStatusText
    {
        get => _coordinateStatusText;
        private set => SetProperty(ref _coordinateStatusText, value);
    }

    public Brush CoordinateStatusColorBrush => _coordinateStatusColor.HasValue
        ? CreateSolidBrush(_coordinateStatusColor.Value)
        : Brushes.Transparent;

    public Visibility CoordinateStatusColorVisibility => _coordinateStatusColor.HasValue
        ? Visibility.Visible
        : Visibility.Collapsed;

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

    public double BackgroundTolerance
    {
        get => _backgroundTolerance;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _backgroundTolerance, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
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
                if (value != BackgroundSpecificationMode.ManualSeed && SelectedEditorMode == EditorMode.WandAddSeed)
                {
                    UpdateModeState(EditorMode.Hand);
                }

                OnPropertyChanged(nameof(IsManualSeedBackgroundMode));
                OnPropertyChanged(nameof(IsColorRangeBackgroundMode));
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
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
                OnPropertyChanged(nameof(BackgroundColorPreviewBrush));
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public double NoiseRemoval
    {
        get => _noiseRemoval;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _noiseRemoval, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public double ContourTolerance
    {
        get => _contourTolerance;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _contourTolerance, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public ContourSettingMethod SelectedContourSettingMethod
    {
        get => _selectedContourSettingMethod;
        set
        {
            if (SetProperty(ref _selectedContourSettingMethod, value))
            {
                OnPropertyChanged(nameof(ContourToleranceVisibility));
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public Visibility ContourToleranceVisibility => SelectedContourSettingMethod == ContourSettingMethod.Width
        ? Visibility.Collapsed
        : Visibility.Visible;

    public double MaxContourWidth
    {
        get => _maxContourWidth;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _maxContourWidth, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public MattingMethod SelectedContourInferenceMethod
    {
        get => _selectedContourInferenceMethod;
        set
        {
            if (SetProperty(ref _selectedContourInferenceMethod, value))
            {
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
            }
        }
    }

    public double TransparencyCut
    {
        get => _transparencyCut;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _transparencyCut, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
            }
        }
    }

    public double DespillExpansion
    {
        get => _despillExpansion;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _despillExpansion, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
            }
        }
    }

    public double OpaqueAlphaThreshold
    {
        get => _opaqueAlphaThreshold;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _opaqueAlphaThreshold, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
            }
        }
    }

    public double DespillMix
    {
        get => _despillMix;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _despillMix, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
            }
        }
    }

    public double DespillBrightness
    {
        get => _despillBrightness;
        set
        {
            var sanitized = Math.Clamp(value, 0d, 1d);
            if (SetProperty(ref _despillBrightness, sanitized))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
            MarkPreviewDirty(PreviewDirtyKind.Presentation);
        }
    }

    public ResizeInterpolationMode SelectedResizeInterpolation
    {
        get => _selectedResizeInterpolation;
        set
        {
            if (SetProperty(ref _selectedResizeInterpolation, value))
            {
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
            }
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
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
                OnPropertyChanged(nameof(OutlineColorPreviewBrush));
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
                MarkPreviewDirty(PreviewDirtyKind.Presentation);
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
                    MarkPreviewDirty(PreviewDirtyKind.Presentation);
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

    public Brush BackgroundColorPreviewBrush => CreatePreviewBrush(ColorPickTarget.Background, BackgroundColorHex);

    public Brush OutlineColorPreviewBrush => CreatePreviewBrush(ColorPickTarget.Outline, OutlineColorHex);

    public bool AutoReprocess
    {
        get => _autoReprocess;
        set
        {
            if (SetProperty(ref _autoReprocess, value) && value && _previewSession.IsDirty)
            {
                TryQueueAutoRender(immediate: true);
            }
        }
    }

    public bool FastPreviewEnabled
    {
        get => _fastPreviewEnabled;
        set
        {
            if (SetProperty(ref _fastPreviewEnabled, value)
                && !value
                && _isFastPreviewActive
                && AutoReprocess
                && _sourceImage is not null)
            {
                QueueRender(PreviewRenderMode.FullPipeline, immediate: true, allowFastPreview: false);
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
            _imageDocument.ReplaceSourceImage(loaded.Clone());
            _previewSession.MarkPendingFullRender();
        }

        var sourceImage = _sourceImage ?? throw new InvalidOperationException("元画像の保持に失敗しました。");
        CurrentFilePath = path;
        OriginalImage = BitmapSourceFactory.FromBgr(sourceImage);
        var topLeftPixel = sourceImage.At<Vec3b>(0, 0);
        SharedViewport.Reset();
        ResetUiParametersForLoadedImage(new RgbColor(topLeftPixel.Item2, topLeftPixel.Item1, topLeftPixel.Item0).ToHex());

        RefreshEditOverlay();
        UpdateModeState(EditorMode.Hand);
        if (AutoReprocess)
        {
            QueueRender(PreviewRenderMode.FullPipeline, immediate: true);
        }
        else
        {
            TrimapImage = null;
            PreviewStageText = "再処理待ち";
            _statusText = GetPendingWorkMessage();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public async Task SaveCurrentImageAsync(string path)
    {
        await EnsureFullQualityResultAsync();
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

    public async Task SaveRawAlphaMaskAsync(string path)
    {
        await EnsureFullQualityResultAsync();
        var alphaMask = _latestResult?.AlphaMask ?? _cachedPreResizeResult?.AlphaMask;
        if (alphaMask is null)
        {
            throw new InvalidOperationException("保存できる A_raw がありません。");
        }

        using var saveMat = alphaMask.Clone();
        await Task.Run(() =>
        {
            if (!Cv2.ImWrite(path, saveMat))
            {
                throw new InvalidOperationException("A_raw を保存できませんでした。");
            }
        });

        _statusText = $"A_raw を保存しました: {path}";
        OnPropertyChanged(nameof(StatusText));
    }

    public void Reprocess()
    {
        if (_sourceImage is null)
        {
            return;
        }

        QueueRender(PreviewRenderMode.FullPipeline, immediate: true, allowFastPreview: false);
    }

    public void CycleFinalBackground()
    {
        FinalBackgroundKind = FinalBackgroundKind switch
        {
            ViewBackgroundKind.Checker => ViewBackgroundKind.White,
            ViewBackgroundKind.White => ViewBackgroundKind.Black,
            ViewBackgroundKind.Black => ViewBackgroundKind.Red,
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
        await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8);
        _statusText = $"設定を書き出しました: {filePath}";
        OnPropertyChanged(nameof(StatusText));
    }

    public async Task ImportSettingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("設定ファイルが見つかりません。", filePath);
        }

        var json = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
        var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json, CreateJsonOptions())
            ?? throw new InvalidOperationException("設定ファイルを読み込めませんでした。");

        ApplySettingsSnapshot(snapshot);
        _statusText = $"設定を読み込みました: {filePath}";
        OnPropertyChanged(nameof(StatusText));
    }

    public void BeginOutlineColorPick()
    {
        _activeColorPickTarget = ColorPickTarget.Outline;
        ClearEyedropperPreview();
        OnPropertyChanged(nameof(IsOutlineColorEyedropperMode));
        OnPropertyChanged(nameof(IsBackgroundColorEyedropperMode));
        UpdateModeState(EditorMode.Eyedropper);
    }

    public void BeginBackgroundColorPick()
    {
        _activeColorPickTarget = ColorPickTarget.Background;
        ClearEyedropperPreview();
        OnPropertyChanged(nameof(IsOutlineColorEyedropperMode));
        OnPropertyChanged(nameof(IsBackgroundColorEyedropperMode));
        UpdateModeState(EditorMode.Eyedropper);
    }

    public void CancelEyedropperMode()
    {
        if (SelectedEditorMode != EditorMode.Eyedropper)
        {
            return;
        }

        UpdateModeState(EditorMode.Hand);
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
                    CoordinateText = $"({x}, {y})",
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
        MarkPreviewDirty(PreviewDirtyKind.Trimap);
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

        var imageX = ToPixelIndex(point.X, _sourceImage.Width);
        var imageY = ToPixelIndex(point.Y, _sourceImage.Height);

        switch (SelectedEditorMode)
        {
            case EditorMode.WandAddSeed:
                if (!IsManualSeedBackgroundMode)
                {
                    UpdateModeState(EditorMode.Hand);
                    return;
                }

                ApplySeedPoint(_backgroundSeedAddMap, null, imageX, imageY);
                RefreshEditOverlay();
                MarkPreviewDirty(PreviewDirtyKind.Trimap);
                UpdateModeState(EditorMode.Hand);
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
            ClearCoordinateStatus();
            ClearEyedropperPreview();
            return;
        }

        var x = ToPixelIndex(point.X, _sourceImage.Width);
        var y = ToPixelIndex(point.Y, _sourceImage.Height);
        var color = GetOriginalImageColor(x, y);
        UpdateEyedropperPreview(color);
        SetCoordinateStatus(FormatCoordinateStatus("元画像", x, y, color, 100d), color);
    }

    public void UpdateAlphaHover(WinPoint point)
    {
        var alphaMask = _latestResult?.AlphaMask ?? _cachedPreResizeResult?.AlphaMask;
        if (alphaMask is null)
        {
            ClearCoordinateStatus();
            ClearEyedropperPreview();
            return;
        }

        var x = ToPixelIndex(point.X, alphaMask.Width);
        var y = ToPixelIndex(point.Y, alphaMask.Height);
        var value = alphaMask.At<byte>(y, x);
        var grayscale = new RgbColor(value, value, value);
        SetCoordinateStatus(FormatCoordinateStatus("アルファ", x, y, grayscale, value * 100d / 255d), grayscale);
    }

    public void UpdateTrimapHover(WinPoint point)
    {
        var trimap = _latestTrimapMask ?? _latestResult?.TrimapMask ?? _cachedPreResizeResult?.TrimapMask;
        if (trimap is null)
        {
            ClearCoordinateStatus();
            return;
        }

        var x = ToPixelIndex(point.X, trimap.Width);
        var y = ToPixelIndex(point.Y, trimap.Height);
        var value = trimap.At<byte>(y, x);
        var label = value switch
        {
            <= 0 => "背景",
            >= 255 => "前景",
            _ => "unknown",
        };
        RgbColor? swatchColor = value switch
        {
            <= 0 => new RgbColor(0, 0, 0),
            >= 255 => GetOriginalImageColor(x, y),
            _ => BlendColors(GetOriginalImageColor(x, y), new RgbColor(255, 0, 0), 0.5d),
        };
        SetCoordinateStatus($"trimap ({x}, {y}) {label}", swatchColor);
    }

    public void UpdateFinalHover(WinPoint point)
    {
        if (_latestResult is null)
        {
            ClearCoordinateStatus();
            SetResultViewerBackgroundHelpActive(false);
            ClearEyedropperPreview();
            return;
        }

        var finalRgba = _latestResult.FinalRgba;
        var x = ToPixelIndex(point.X, finalRgba.Width);
        var y = ToPixelIndex(point.Y, finalRgba.Height);
        var pixel = finalRgba.At<Vec4b>(y, x);
        var displayColor = GetDisplayedResultColor(pixel, x, y);
        UpdateEyedropperPreview(displayColor);
        SetCoordinateStatus(FormatCoordinateStatus("結果", x, y, displayColor, pixel.Item3 * 100d / 255d), displayColor);
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
            if (!IsManualSeedBackgroundMode)
            {
                UpdateModeState(EditorMode.Hand);
                return;
            }

            var imageX = ToPixelIndex(displayPoint.X / Math.Max(0.0001d, ResultCoordinateScaleX), _sourceImage.Width);
            var imageY = ToPixelIndex(displayPoint.Y / Math.Max(0.0001d, ResultCoordinateScaleY), _sourceImage.Height);
            ApplySeedPoint(_backgroundSeedAddMap, null, imageX, imageY);
            RefreshEditOverlay();
            MarkPreviewDirty(PreviewDirtyKind.Trimap);
            UpdateModeState(EditorMode.Hand);
            return;
        }

        if (SelectedEditorMode != EditorMode.Eyedropper)
        {
            return;
        }

        var finalRgba = _latestResult.FinalRgba;
        var x = ToPixelIndex(displayPoint.X, finalRgba.Width);
        var y = ToPixelIndex(displayPoint.Y, finalRgba.Height);
        var pixel = finalRgba.At<Vec4b>(y, x);
        ApplyPickedColor(GetDisplayedResultColor(pixel, x, y));
    }

    public void ClearCoordinateInfo()
    {
        ClearCoordinateStatus();
        SetResultViewerBackgroundHelpActive(false);
        ClearEyedropperPreview();
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
            var summary = await _batchCoordinator.RunAsync(
                files,
                BuildParameters(),
                update =>
                {
                    BatchProgressPercent = update.ProgressPercent;
                    BatchStatusText = update.StatusText;
                },
                cancellationToken);
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
        _previewSession.Dispose();
        _imageDocument.Dispose();
        _processor.Dispose();
        _seedBlinkTimer.Stop();
        _seedBlinkTimer.Tick -= OnSeedBlinkTimerTick;
        PythonWorkerTempDirectoryCleaner.TryDelete(_workerTempDirectory);
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
                break;
            default:
                OutlineColorHex = color.ToHex();
                statusText = $"縁取り色を取得しました: {OutlineColorHex}";
                break;
        }

        ClearEyedropperPreview();
        UpdateModeState(EditorMode.Hand);
        _statusText = statusText;
        OnPropertyChanged(nameof(StatusText));
    }

    private void QueueRender(PreviewRenderMode mode, bool immediate = false, bool allowFastPreview = true)
    {
        if (_sourceImage is null)
        {
            return;
        }

        CancelPendingPresentationRefresh();
        _processor.CancelPendingMatting();
        var ticket = _previewSession.BeginRender(mode);
        IsPreviewBusy = true;
        PreviewProgressPercent = 0d;
        PreviewStageText = mode switch
        {
            PreviewRenderMode.TrimapOnly => immediate ? "trimap を更新" : "trimap 更新待機",
            PreviewRenderMode.PresentationOnly => immediate ? "結果を再描画" : "結果再描画待機",
            _ => immediate ? "PyMatting を実行" : "PyMatting 実行待機",
        };
        _statusText = "プレビューを更新しています。";
        OnPropertyChanged(nameof(StatusText));
        _ = RenderPreviewAsync(ticket.Token, ticket.RenderVersion, immediate ? 0 : 120, mode, allowFastPreview);
    }

    private void MarkPreviewDirty(PreviewDirtyKind kind = PreviewDirtyKind.Trimap)
    {
        if (kind == PreviewDirtyKind.Trimap)
        {
            CancelPendingPresentationRefresh();
            DisposeCachedPreResizeResult();
        }

        if (_suspendPreviewInvalidation)
        {
            return;
        }

        if (_sourceImage is null)
        {
            return;
        }

        if (kind == PreviewDirtyKind.Presentation
            && _previewSession.IsRendering
            && _previewSession.ActiveRenderMode == PreviewRenderMode.FullPipeline)
        {
            QueuePresentationRefreshFromCache();
            _statusText = "PyMatting を継続しながら後補正を更新します。";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        _previewSession.MarkDirty(kind);

        if (AutoReprocess && TryQueueAutoRender(immediate: false))
        {
            return;
        }

        _processor.CancelPendingMatting();
        _previewSession.InvalidateCurrentRender();
        IsPreviewBusy = false;
        PreviewProgressPercent = 0d;
        PreviewStageText = "再処理待ち";
        _statusText = GetPendingWorkMessage();
        OnPropertyChanged(nameof(StatusText));
    }

    private bool TryQueueAutoRender(bool immediate)
    {
        if (_sourceImage is null)
        {
            return false;
        }

        var mode = _previewSession.GetNextRenderMode(_sourceImage is not null, _cachedPreResizeResult is not null);
        if (mode.HasValue)
        {
            QueueRender(mode.Value, immediate);
            return true;
        }

        return false;
    }

    private string GetPendingWorkMessage()
    {
        if (_previewSession.RequiresCoreRender || _previewSession.RequiresPresentationRender)
        {
            return "変更があります。再処理を押してください。";
        }

        return "設定を変更しました。再処理を押してください。";
    }

    private void ClearLatestResult()
    {
        _imageDocument.ClearLatestResult();
        SetFastPreviewActive(false);
        FinalImage = null;
        OnPropertyChanged(nameof(DisplayedResultImage));
        ResultCoordinateScaleX = 1d;
        ResultCoordinateScaleY = 1d;
    }

    private void DisposeLatestTrimapMask()
    {
        _imageDocument.ClearLatestTrimapMask();
    }

    private void DisposeCachedPreResizeResult()
    {
        _imageDocument.ClearCachedPreResizeResult();
    }

    private void SetLatestTrimapMask(Mat trimapMask)
    {
        _imageDocument.ReplaceLatestTrimapMask(trimapMask);
    }

    private AppSettingsSnapshot CreateSettingsSnapshot() => new()
    {
        Ui = CaptureUiSettingsSnapshot(),
        Internal = InternalSettingsCloner.Clone(_internalSettings),
    };

    private AppSettingsSnapshot.UiSettingsSnapshot CaptureUiSettingsSnapshot() => new()
    {
        BackgroundSpecificationMode = SelectedBackgroundSpecificationMode,
        BackgroundColorHex = BackgroundColorHex,
        BackgroundTolerance = BackgroundTolerance,
        ContourSettingMethod = SelectedContourSettingMethod,
        ContourTolerance = ContourTolerance,
        MaxContourWidth = MaxContourWidth,
        DenoiseStrength = NoiseRemoval,
        ContourInferenceMethod = SelectedContourInferenceMethod,
        BackgroundSeeds = CollectSeedSnapshots(),
        TransparencyCut = TransparencyCut,
        OpaqueAlphaThreshold = OpaqueAlphaThreshold,
        DespillExpansion = DespillExpansion,
        DespillMix = DespillMix,
        DespillBrightness = DespillBrightness,
        AutoReprocess = AutoReprocess,
        FastPreviewEnabled = FastPreviewEnabled,
        ResizeInterpolation = SelectedResizeInterpolation,
        ScalePercent = ScalePercent,
        OutputWidth = OutputWidth,
        OutputHeight = OutputHeight,
        OutlineEnabled = OutlineEnabled,
        OutlineColorHex = OutlineColorHex,
        OutlineThickness = OutlineThickness,
    };

    private void ResetUiParametersForLoadedImage(string backgroundColorHex)
    {
        var defaults = new AppSettingsSnapshot.UiSettingsSnapshot();
        _suspendPreviewInvalidation = true;
        try
        {
            SelectedBackgroundSpecificationMode = defaults.BackgroundSpecificationMode;
            BackgroundColorHex = backgroundColorHex;
            BackgroundTolerance = defaults.BackgroundTolerance;
            SelectedContourSettingMethod = defaults.ContourSettingMethod;
            NoiseRemoval = defaults.DenoiseStrength;
            ContourTolerance = defaults.ContourTolerance;
            MaxContourWidth = defaults.MaxContourWidth;
            SelectedContourInferenceMethod = defaults.ContourInferenceMethod;
            TransparencyCut = defaults.TransparencyCut;
            OpaqueAlphaThreshold = defaults.OpaqueAlphaThreshold;
            DespillExpansion = defaults.DespillExpansion;
            DespillMix = defaults.DespillMix;
            DespillBrightness = defaults.DespillBrightness;
            AutoReprocess = defaults.AutoReprocess;
            FastPreviewEnabled = defaults.FastPreviewEnabled;
            SelectedResizeInterpolation = defaults.ResizeInterpolation;
            OutlineEnabled = defaults.OutlineEnabled;
            OutlineColorHex = defaults.OutlineColorHex;
            OutlineThickness = defaults.OutlineThickness;
            FinalBackgroundKind = ViewBackgroundKind.Checker;
            IsResultShowingAlpha = false;

            _syncingDimensions = true;
            _outputWidth = Math.Max(1, _sourceImage?.Width ?? defaults.OutputWidth);
            _outputHeight = Math.Max(1, _sourceImage?.Height ?? defaults.OutputHeight);
            _scalePercent = defaults.ScalePercent;
            _syncingDimensions = false;
            OnPropertyChanged(nameof(OutputWidth));
            OnPropertyChanged(nameof(OutputHeight));
            OnPropertyChanged(nameof(ScalePercent));
        }
        finally
        {
            _suspendPreviewInvalidation = false;
        }
    }

    private void ApplySettingsSnapshot(AppSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _suspendPreviewInvalidation = true;
        try
        {
            var ui = snapshot.Ui ?? new AppSettingsSnapshot.UiSettingsSnapshot();

            SelectedBackgroundSpecificationMode = ui.BackgroundSpecificationMode;
            BackgroundColorHex = ui.BackgroundColorHex;
            BackgroundTolerance = ui.BackgroundTolerance;
            SelectedContourSettingMethod = ui.ContourSettingMethod;
            NoiseRemoval = ui.DenoiseStrength;
            ContourTolerance = ui.ContourTolerance;
            MaxContourWidth = ui.MaxContourWidth;
            SelectedContourInferenceMethod = ui.ContourInferenceMethod;
            TransparencyCut = ui.TransparencyCut;
            OpaqueAlphaThreshold = ui.OpaqueAlphaThreshold;
            DespillExpansion = ui.DespillExpansion;
            DespillMix = ui.DespillMix;
            DespillBrightness = ui.DespillBrightness;
            ScalePercent = ui.ScalePercent;
            SelectedResizeInterpolation = ui.ResizeInterpolation;
            OutputWidth = Math.Max(1, ui.OutputWidth);
            OutputHeight = Math.Max(1, ui.OutputHeight);
            OutlineEnabled = ui.OutlineEnabled;
            OutlineColorHex = ui.OutlineColorHex;
            OutlineThickness = ui.OutlineThickness;
            AutoReprocess = ui.AutoReprocess;
            FastPreviewEnabled = ui.FastPreviewEnabled;
            RestoreSeedSnapshots(ui.BackgroundSeeds);
            InternalSettingsCloner.CopyTo(_internalSettings, snapshot.Internal);
        }
        finally
        {
            _suspendPreviewInvalidation = false;
        }

        if (_sourceImage is null)
        {
            return;
        }

        if (AutoReprocess && TryQueueAutoRender(immediate: true))
        {
            return;
        }

        _previewSession.MarkPendingFullRender();
        _processor.CancelPendingMatting();
        _previewSession.InvalidateCurrentRender();
        IsPreviewBusy = false;
        PreviewProgressPercent = 0d;
        PreviewStageText = "再処理待ち";
        _statusText = GetPendingWorkMessage();
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

    private List<AppSettingsSnapshot.SeedPointSnapshot> CollectSeedSnapshots()
    {
        return _imageDocument.CollectSeedSnapshots();
    }

    private void RestoreSeedSnapshots(IReadOnlyList<AppSettingsSnapshot.SeedPointSnapshot>? seeds)
    {
        _imageDocument.RestoreSeedSnapshots(seeds);
        RefreshEditOverlay();
    }

    private async Task RenderPreviewAsync(
        CancellationToken cancellationToken,
        int renderVersion,
        int delayMs,
        PreviewRenderMode mode,
        bool allowFastPreview)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }

            if (!_previewSession.IsCurrentVersion(renderVersion))
            {
                return;
            }

            Mat? sourceClone = null;
            ManualEditMaps? manualMaps = null;
            PreResizeCutoutResult? preResizeCacheClone = null;
            CutoutParameters parameters;

            lock (_syncRoot)
            {
                parameters = BuildParameters();

                if (mode is PreviewRenderMode.TrimapOnly or PreviewRenderMode.FullPipeline)
                {
                    sourceClone = _imageDocument.CloneSourceImage();
                    manualMaps = _imageDocument.CreateManualEditMapsClone();
                }
                else if (_cachedPreResizeResult is not null)
                {
                    preResizeCacheClone = _imageDocument.CloneCachedPreResizeResult();
                }
            }

            if (mode is PreviewRenderMode.TrimapOnly or PreviewRenderMode.FullPipeline
                && parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed
                && !HasAnyBackgroundSeed(manualMaps))
            {
                sourceClone?.Dispose();
                manualMaps?.Dispose();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_previewSession.IsCurrentVersion(renderVersion))
                    {
                        return;
                    }

                    ClearLatestResult();
                    DisposeLatestTrimapMask();
                    DisposeCachedPreResizeResult();
                    _previewSession.MarkPendingFullRender();
                    TrimapImage = null;
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
                if (!_previewSession.IsCurrentVersion(renderVersion))
                {
                    return;
                }

                IsPreviewBusy = true;
                PreviewProgressPercent = Math.Clamp(report.ProgressPercent, 0d, 100d);
                PreviewStageText = report.StageText;
                _statusText = $"プレビュー更新中: {report.StageText}";
                OnPropertyChanged(nameof(StatusText));
            });

            PreResizeCutoutResult? refreshedPreResize = null;

            using (sourceClone)
            using (manualMaps)
            using (preResizeCacheClone)
            {
                if (mode == PreviewRenderMode.TrimapOnly)
                {
                    using var preparedTrimap = await Task.Run(() => _processor.PrepareTrimap(sourceClone!, parameters, manualMaps, progress), cancellationToken);
                    if (!_previewSession.IsCurrentVersion(renderVersion))
                    {
                        return;
                    }

                    var trimapPreviewBitmap = BitmapSourceFactory.FromTrimapPreview(preparedTrimap.OriginalBgr, preparedTrimap.TrimapMask);
                    var trimapCache = preparedTrimap.TrimapMask.Clone();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            trimapCache.Dispose();
                            return;
                        }

                        SetLatestTrimapMask(trimapCache);
                        DisposeCachedPreResizeResult();
                        ClearLatestResult();
                        TrimapImage = trimapPreviewBitmap;
                        _previewSession.MarkTrimapRendered();
                        IsPreviewBusy = false;
                        PreviewProgressPercent = 0d;
                        PreviewStageText = "再処理待ち";
                        _statusText = "trimap を更新しました。PyMatting を実行してください。";
                        OnPropertyChanged(nameof(StatusText));
                    });

                    return;
                }

                CutoutResult result;
                if (mode == PreviewRenderMode.FullPipeline)
                {
                    using var preparedTrimap = await Task.Run(() => _processor.PrepareTrimap(sourceClone!, parameters, manualMaps, progress), cancellationToken);
                    if (!_previewSession.IsCurrentVersion(renderVersion))
                    {
                        return;
                    }

                    var interimTrimapBitmap = BitmapSourceFactory.FromTrimapPreview(preparedTrimap.OriginalBgr, preparedTrimap.TrimapMask);
                    var interimTrimapCache = preparedTrimap.TrimapMask.Clone();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            interimTrimapCache.Dispose();
                            return;
                        }

                        SetLatestTrimapMask(interimTrimapCache);
                        TrimapImage = interimTrimapBitmap;
                    });

                    if (cancellationToken.IsCancellationRequested || !_previewSession.IsCurrentVersion(renderVersion))
                    {
                        return;
                    }

                    if (allowFastPreview && FastPreviewEnabled)
                    {
                        using var previewPreResize = await Task.Run(
                            () => _processor.EstimateAlphaPreviewFromTrimap(preparedTrimap, parameters, FastPreviewScale, progress),
                            cancellationToken);
                        if (previewPreResize is null)
                        {
                            return;
                        }

                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            return;
                        }

                        using var previewResult = await Task.Run(
                            () => _processor.FinalizeFromPreResize(previewPreResize, parameters, progress),
                            cancellationToken);
                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            return;
                        }

                        var previewAlphaBitmap = BitmapSourceFactory.FromAlphaPreview(previewResult.AlphaMask);
                        var previewFinalBitmap = BitmapSourceFactory.FromBgra(previewResult.FinalRgba);
                        var previewResultScaleX = _sourceImage is null ? 1d : previewResult.FinalRgba.Width / (double)_sourceImage.Width;
                        var previewResultScaleY = _sourceImage is null ? 1d : previewResult.FinalRgba.Height / (double)_sourceImage.Height;

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (!_previewSession.IsCurrentVersion(renderVersion))
                            {
                                return;
                            }

                            _imageDocument.ReplaceLatestResult(new CutoutResult(
                                previewResult.TrimapMask.Clone(),
                                previewResult.AlphaMask.Clone(),
                                previewResult.FinalRgba.Clone(),
                                previewResult.ResolvedBackgroundColor));
                            AlphaImage = previewAlphaBitmap;
                            FinalImage = previewFinalBitmap;
                            OnPropertyChanged(nameof(DisplayedResultImage));
                            ResultCoordinateScaleX = previewResultScaleX;
                            ResultCoordinateScaleY = previewResultScaleY;
                            SetFastPreviewActive(true);
                            IsPreviewBusy = true;
                            PreviewProgressPercent = 80d;
                            PreviewStageText = "高速プレビュー中";
                            _statusText = "プレビュー更新中: 高速プレビューを表示しています。";
                            OnPropertyChanged(nameof(StatusText));
                        });

                        if (cancellationToken.IsCancellationRequested || !_previewSession.IsCurrentVersion(renderVersion))
                        {
                            return;
                        }
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            return;
                        }

                        IsPreviewBusy = true;
                        PreviewProgressPercent = 30d;
                        PreviewStageText = "PyMatting 開始待ち";
                        _statusText = "プレビュー更新中: PyMatting 開始待ち";
                        OnPropertyChanged(nameof(StatusText));
                    });

                    await Task.Delay(PyMattingStartDebounceMs, cancellationToken);
                    if (cancellationToken.IsCancellationRequested || !_previewSession.IsCurrentVersion(renderVersion))
                    {
                        return;
                    }

                    refreshedPreResize = await Task.Run(() => _processor.EstimateAlphaFromTrimap(preparedTrimap, parameters, progress), cancellationToken);
                    if (refreshedPreResize is null)
                    {
                        return;
                    }

                    if (!_previewSession.IsCurrentVersion(renderVersion))
                    {
                        refreshedPreResize.Dispose();
                        return;
                    }

                    CutoutParameters latestParameters;
                    lock (_syncRoot)
                    {
                        latestParameters = BuildParameters();
                    }

                    result = await Task.Run(() => _processor.FinalizeFromPreResize(refreshedPreResize, latestParameters, progress), cancellationToken);
                }
                else
                {
                    if (preResizeCacheClone is null)
                    {
                        throw new InvalidOperationException("PyMatting の実行結果がありません。");
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_previewSession.IsCurrentVersion(renderVersion))
                        {
                            return;
                        }

                        IsPreviewBusy = true;
                        PreviewProgressPercent = 95d;
                        PreviewStageText = "リサイズと縁取りを適用";
                        _statusText = "プレビュー更新中: リサイズと縁取りを適用";
                        OnPropertyChanged(nameof(StatusText));
                    });

                    result = await Task.Run(() => _processor.FinalizeFromPreResize(preResizeCacheClone, parameters, progress), cancellationToken);
                }

                if (!_previewSession.IsCurrentVersion(renderVersion))
                {
                    result.Dispose();
                    refreshedPreResize?.Dispose();
                    return;
                }

                var alphaBitmap = BitmapSourceFactory.FromAlphaPreview(result.AlphaMask);
                var finalBitmap = BitmapSourceFactory.FromBgra(result.FinalRgba);
                var resultScaleX = _sourceImage is null ? 1d : result.FinalRgba.Width / (double)_sourceImage.Width;
                var resultScaleY = _sourceImage is null ? 1d : result.FinalRgba.Height / (double)_sourceImage.Height;
                BitmapSource? trimapBitmap = null;
                if (mode == PreviewRenderMode.PresentationOnly)
                {
                    trimapBitmap = BitmapSourceFactory.FromTrimapPreview(preResizeCacheClone?.OriginalBgr ?? _sourceImage!, result.TrimapMask);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_previewSession.IsCurrentVersion(renderVersion))
                    {
                        result.Dispose();
                        return;
                    }

                    if (mode == PreviewRenderMode.PresentationOnly)
                    {
                        SetLatestTrimapMask(result.TrimapMask.Clone());
                        TrimapImage = trimapBitmap;
                    }

                    if (refreshedPreResize is not null)
                    {
                        _imageDocument.ReplaceCachedPreResizeResult(refreshedPreResize);
                        refreshedPreResize = null;
                    }
                        _imageDocument.ReplaceLatestResult(result);
                        SetFastPreviewActive(false);
                        AlphaImage = alphaBitmap;
                        FinalImage = finalBitmap;
                    OnPropertyChanged(nameof(DisplayedResultImage));
                    ResultCoordinateScaleX = resultScaleX;
                    ResultCoordinateScaleY = resultScaleY;
                    _previewSession.MarkRenderCompleted();
                    if (AutoReprocess && TryQueueAutoRender(immediate: false))
                    {
                        return;
                    }

                    IsPreviewBusy = false;
                    PreviewProgressPercent = 0d;
                    PreviewStageText = "待機";
                    _statusText = $"プレビュー更新完了。背景 {result.ResolvedBackgroundColor.ToHex()}  出力 {result.FinalRgba.Width} x {result.FinalRgba.Height}";
                    OnPropertyChanged(nameof(StatusText));
                });
            }
        }
        catch (OperationCanceledException)
        {
            if (!_previewSession.IsCurrentVersion(renderVersion))
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _previewSession.MarkRenderInterrupted(mode);
                IsPreviewBusy = false;
                PreviewProgressPercent = 0d;
                PreviewStageText = "待機";
                _statusText = _previewSession.IsDirty ? GetPendingWorkMessage() : GetCurrentModeMessage();
                OnPropertyChanged(nameof(StatusText));
            });
        }
        catch (Exception exception)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!_previewSession.IsCurrentVersion(renderVersion))
                {
                    return;
                }

                _previewSession.MarkRenderInterrupted(mode);
                IsPreviewBusy = false;
                PreviewProgressPercent = 0d;
                SetFastPreviewActive(false);
                PreviewStageText = "エラー";
                _statusText = $"プレビュー更新失敗: {exception.Message}";
                OnPropertyChanged(nameof(StatusText));
            });
        }
    }

    private async Task EnsureFullQualityResultAsync()
    {
        if (_sourceImage is null || !_isFastPreviewActive)
        {
            return;
        }

        _processor.CancelPendingMatting();
        var ticket = _previewSession.BeginRender(PreviewRenderMode.FullPipeline);
        IsPreviewBusy = true;
        PreviewProgressPercent = 0d;
        PreviewStageText = "保存前に最終品質を再計算";
        _statusText = "保存前に最終品質を再計算しています。";
        OnPropertyChanged(nameof(StatusText));
        await RenderPreviewAsync(ticket.Token, ticket.RenderVersion, 0, PreviewRenderMode.FullPipeline, allowFastPreview: false);

        if (_isFastPreviewActive)
        {
            throw new InvalidOperationException("最終品質のプレビュー生成に失敗したため保存できません。");
        }
    }

    private void SetFastPreviewActive(bool isActive)
    {
        _isFastPreviewActive = isActive;
    }

    private void QueuePresentationRefreshFromCache()
    {
        CancelPendingPresentationRefresh();

        PreResizeCutoutResult? cachedPreResizeClone;
        CutoutParameters parameters;
        lock (_syncRoot)
        {
            cachedPreResizeClone = _imageDocument.CloneCachedPreResizeResult();
            parameters = BuildParameters();
        }

        if (cachedPreResizeClone is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _presentationRefreshCts = cts;
        var version = Interlocked.Increment(ref _presentationRefreshVersion);
        _ = RenderPresentationRefreshFromCacheAsync(cachedPreResizeClone, parameters, version, cts.Token);
    }

    private async Task RenderPresentationRefreshFromCacheAsync(
        PreResizeCutoutResult cachedPreResizeClone,
        CutoutParameters parameters,
        int version,
        CancellationToken cancellationToken)
    {
        using (cachedPreResizeClone)
        {
            try
            {
                var result = await Task.Run(() => _processor.FinalizeFromPreResize(cachedPreResizeClone, parameters), cancellationToken);
                if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _presentationRefreshVersion))
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
                    if (cancellationToken.IsCancellationRequested
                        || version != Volatile.Read(ref _presentationRefreshVersion)
                        || !_previewSession.IsRendering
                        || _previewSession.ActiveRenderMode != PreviewRenderMode.FullPipeline)
                    {
                        result.Dispose();
                        return;
                    }

                    _imageDocument.ReplaceLatestResult(result);
                    AlphaImage = alphaBitmap;
                    FinalImage = finalBitmap;
                    OnPropertyChanged(nameof(DisplayedResultImage));
                    ResultCoordinateScaleX = resultScaleX;
                    ResultCoordinateScaleY = resultScaleY;
                    _statusText = "PyMatting を継続しながら後補正を更新しました。";
                    OnPropertyChanged(nameof(StatusText));
                });
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void CancelPendingPresentationRefresh()
    {
        _presentationRefreshCts?.Cancel();
        _presentationRefreshCts?.Dispose();
        _presentationRefreshCts = null;
    }

    private CutoutParameters BuildParameters()
    {
        return SettingsMapper.BuildCutoutParameters(CaptureUiSettingsSnapshot(), _internalSettings);
    }

    private void UpdateModeState(EditorMode mode)
    {
        if (mode != EditorMode.Eyedropper)
        {
            ClearEyedropperPreview();
        }

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

        return _activeColorPickTarget switch
        {
            ColorPickTarget.Background => "元画像または結果画像をクリックして背景色を取得します。",
            _ => "元画像または結果画像をクリックして縁取り色を取得します。",
        };
    }

    private void UpdateZoomPercentText() => ZoomPercentText = $"{SharedViewport.Zoom * 100d:0.#}%";

    private static bool HasAnyBackgroundSeed(ManualEditMaps? manualMaps)
    {
        return manualMaps?.BackgroundSeedAddMap is not null
            && !manualMaps.BackgroundSeedAddMap.Empty()
            && Cv2.CountNonZero(manualMaps.BackgroundSeedAddMap) > 0;
    }

    private RgbColor GetDisplayedResultColor(Vec4b pixel, int x, int y)
    {
        var alpha = pixel.Item3 / 255d;
        var background = GetBackgroundSample(FinalBackgroundKind, x, y);
        return new RgbColor(
            BlendDisplayedChannel(pixel.Item2, background.R, alpha),
            BlendDisplayedChannel(pixel.Item1, background.G, alpha),
            BlendDisplayedChannel(pixel.Item0, background.B, alpha));
    }

    private static RgbColor GetBackgroundSample(ViewBackgroundKind kind, int x, int y)
    {
        return kind switch
        {
            ViewBackgroundKind.White => new RgbColor(255, 255, 255),
            ViewBackgroundKind.Black => new RgbColor(0, 0, 0),
            ViewBackgroundKind.Red => new RgbColor(255, 0, 0),
            ViewBackgroundKind.Green => new RgbColor(0, 255, 0),
            ViewBackgroundKind.Blue => new RgbColor(0, 0, 255),
            _ => (((x / 12) + (y / 12)) & 1) == 0
                ? new RgbColor(210, 210, 210)
                : new RgbColor(164, 164, 164),
        };
    }

    private static byte BlendDisplayedChannel(byte foreground, byte background, double alpha)
        => (byte)Math.Clamp((int)Math.Round((foreground * alpha) + (background * (1d - alpha))), 0, 255);

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
        => $"{label} ({x},{y}) {color.ToHex()} {alphaPercent:0.0}%";

    private static int ToPixelIndex(double coordinate, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Floor(coordinate), 0, size - 1);
    }

    private void SetCoordinateStatus(string text, RgbColor? color = null)
    {
        CoordinateStatusText = text;
        if (_coordinateStatusColor == color)
        {
            return;
        }

        _coordinateStatusColor = color;
        OnPropertyChanged(nameof(CoordinateStatusColorBrush));
        OnPropertyChanged(nameof(CoordinateStatusColorVisibility));
    }

    private void ClearCoordinateStatus()
    {
        SetCoordinateStatus(string.Empty);
    }

    private void UpdateEyedropperPreview(RgbColor color)
    {
        if (SelectedEditorMode != EditorMode.Eyedropper)
        {
            return;
        }

        _eyedropperPreviewColor = color;
        NotifyColorPreviewBrushesChanged();
    }

    private void ClearEyedropperPreview()
    {
        if (!_eyedropperPreviewColor.HasValue)
        {
            return;
        }

        _eyedropperPreviewColor = null;
        NotifyColorPreviewBrushesChanged();
    }

    private void NotifyColorPreviewBrushesChanged()
    {
        OnPropertyChanged(nameof(BackgroundColorPreviewBrush));
        OnPropertyChanged(nameof(OutlineColorPreviewBrush));
    }

    private Brush CreatePreviewBrush(ColorPickTarget target, string hex)
    {
        var previewColor = IsEyedropperMode && _activeColorPickTarget == target
            ? _eyedropperPreviewColor
            : null;
        if (previewColor.HasValue)
        {
            return CreateSolidBrush(previewColor.Value);
        }

        return RgbColor.TryParseHex(hex, out var parsed)
            ? CreateSolidBrush(parsed)
            : Brushes.Transparent;
    }

    private static Brush CreateSolidBrush(RgbColor color)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private RgbColor GetOriginalImageColor(int x, int y)
    {
        if (_sourceImage is null)
        {
            return new RgbColor(0, 0, 0);
        }

        var clampedX = Math.Clamp(x, 0, _sourceImage.Width - 1);
        var clampedY = Math.Clamp(y, 0, _sourceImage.Height - 1);
        var pixel = _sourceImage.At<Vec3b>(clampedY, clampedX);
        return new RgbColor(pixel.Item2, pixel.Item1, pixel.Item0);
    }

    private static RgbColor BlendColors(RgbColor baseColor, RgbColor overlayColor, double overlayOpacity)
    {
        var t = Math.Clamp(overlayOpacity, 0d, 1d);
        return new RgbColor(
            (byte)Math.Clamp((int)Math.Round((baseColor.R * (1d - t)) + (overlayColor.R * t)), 0, 255),
            (byte)Math.Clamp((int)Math.Round((baseColor.G * (1d - t)) + (overlayColor.G * t)), 0, 255),
            (byte)Math.Clamp((int)Math.Round((baseColor.B * (1d - t)) + (overlayColor.B * t)), 0, 255));
    }

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

    private static double ConvertInternalBrightnessToUi(double internalValue)
        => Math.Clamp((internalValue + 10d) / 20d, 0d, 1d);

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
