using Kirinico.App.ViewModels;
using Kirinico.App.Services;
using System.ComponentModel;
using Brush = System.Windows.Media.Brush;
using Cursor = System.Windows.Input.Cursor;
using DependencyObject = System.Windows.DependencyObject;
using DependencyProperty = System.Windows.DependencyProperty;
using DependencyPropertyChangedEventArgs = System.Windows.DependencyPropertyChangedEventArgs;
using FrameworkPropertyMetadata = System.Windows.FrameworkPropertyMetadata;
using ImageSource = System.Windows.Media.ImageSource;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using PropertyMetadata = System.Windows.PropertyMetadata;
using Rect = System.Windows.Rect;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using ScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;
using Size = System.Windows.Size;
using SizeChangedEventArgs = System.Windows.SizeChangedEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Cursors = System.Windows.Input.Cursors;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kirinico.App.Controls;

public partial class ZoomPanImageViewer : UserControl
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(ZoomPanImageViewer), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty OverlaySourceProperty =
        DependencyProperty.Register(nameof(OverlaySource), typeof(ImageSource), typeof(ZoomPanImageViewer));

    public static readonly DependencyProperty ViewerBackgroundProperty =
        DependencyProperty.Register(nameof(ViewerBackground), typeof(Brush), typeof(ZoomPanImageViewer));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ZoomPanImageViewer));

    public static readonly DependencyProperty ViewportStateProperty =
        DependencyProperty.Register(nameof(ViewportState), typeof(ViewportState), typeof(ZoomPanImageViewer), new PropertyMetadata(null, OnViewportStateChanged));

    public static readonly DependencyProperty CoordinateScaleXProperty =
        DependencyProperty.Register(nameof(CoordinateScaleX), typeof(double), typeof(ZoomPanImageViewer), new PropertyMetadata(1d, OnCoordinateScaleChanged));

    public static readonly DependencyProperty CoordinateScaleYProperty =
        DependencyProperty.Register(nameof(CoordinateScaleY), typeof(double), typeof(ZoomPanImageViewer), new PropertyMetadata(1d, OnCoordinateScaleChanged));

    public static readonly DependencyProperty LeftPanEnabledProperty =
        DependencyProperty.Register(nameof(LeftPanEnabled), typeof(bool), typeof(ZoomPanImageViewer), new PropertyMetadata(false, OnLeftPanEnabledChanged));

    public static readonly DependencyProperty InteractionCursorProperty =
        DependencyProperty.Register(nameof(InteractionCursor), typeof(Cursor), typeof(ZoomPanImageViewer), new PropertyMetadata(Cursors.Arrow, OnInteractionCursorChanged));

    private bool _isApplyingState;
    private bool _isViewportApplyQueued;
    private bool _isPanning;
    private Point _panStartPosition;
    private double _panStartOffsetX;
    private double _panStartOffsetY;

    public ZoomPanImageViewer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnViewerSizeChanged;
        ViewerScroll.SizeChanged += OnViewerSizeChanged;
        ViewerScroll.LostMouseCapture += OnViewerLostMouseCapture;
    }

    public event EventHandler<ImagePointerEventArgs>? PointerPressed;

    public event EventHandler<ImagePointerEventArgs>? PointerMoved;

    public event EventHandler<ImagePointerEventArgs>? PointerReleased;

    public event EventHandler? PointerExited;

    public event EventHandler? ViewerDoubleClicked;

    public event EventHandler? TitleClicked;

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ImageSource? OverlaySource
    {
        get => (ImageSource?)GetValue(OverlaySourceProperty);
        set => SetValue(OverlaySourceProperty, value);
    }

    public Brush? ViewerBackground
    {
        get => (Brush?)GetValue(ViewerBackgroundProperty);
        set => SetValue(ViewerBackgroundProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ViewportState? ViewportState
    {
        get => (ViewportState?)GetValue(ViewportStateProperty);
        set => SetValue(ViewportStateProperty, value);
    }

    public double CoordinateScaleX
    {
        get => (double)GetValue(CoordinateScaleXProperty);
        set => SetValue(CoordinateScaleXProperty, value);
    }

    public double CoordinateScaleY
    {
        get => (double)GetValue(CoordinateScaleYProperty);
        set => SetValue(CoordinateScaleYProperty, value);
    }

    public bool LeftPanEnabled
    {
        get => (bool)GetValue(LeftPanEnabledProperty);
        set => SetValue(LeftPanEnabledProperty, value);
    }

    public Cursor InteractionCursor
    {
        get => (Cursor)GetValue(InteractionCursorProperty);
        set => SetValue(InteractionCursorProperty, value);
    }

    private static void OnViewportStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (ZoomPanImageViewer)d;

        if (e.OldValue is ViewportState oldState)
        {
            oldState.PropertyChanged -= viewer.OnViewportStatePropertyChanged;
        }

        if (e.NewValue is ViewportState newState)
        {
            newState.PropertyChanged += viewer.OnViewportStatePropertyChanged;
        }

        viewer.ApplyViewportState();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ZoomPanImageViewer)d).ApplyViewportStateDeferred();
    }

    private static void OnCoordinateScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ZoomPanImageViewer)d).ApplyViewportState();
    }

    private static void OnLeftPanEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (ZoomPanImageViewer)d;
        if (!(bool)e.NewValue)
        {
            viewer.EndPanCapture();
        }
    }

    private static void OnInteractionCursorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (ZoomPanImageViewer)d;
        if (!viewer._isPanning)
        {
            viewer.Cursor = Cursors.Arrow;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyViewportStateDeferred();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewportState is not null)
        {
            ViewportState.PropertyChanged -= OnViewportStatePropertyChanged;
        }

        _isViewportApplyQueued = false;
    }

    private void OnViewportStatePropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyViewportState();

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingState)
        {
            return;
        }

        ApplyViewportStateDeferred();
    }

    private void ApplyViewportStateDeferred()
    {
        if (!IsLoaded || _isViewportApplyQueued)
        {
            return;
        }

        _isViewportApplyQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _isViewportApplyQueued = false;
            ApplyViewportState();
        }));
    }

    private void ApplyViewportState()
    {
        if (ViewportState is null)
        {
            return;
        }

        _isApplyingState = true;
        ContentScale.ScaleX = ViewportState.Zoom / Math.Max(0.0001d, CoordinateScaleX);
        ContentScale.ScaleY = ViewportState.Zoom / Math.Max(0.0001d, CoordinateScaleY);
        ViewerScroll.UpdateLayout();
        ViewerScroll.ScrollToHorizontalOffset(ViewportState.OffsetX);
        ViewerScroll.ScrollToVerticalOffset(ViewportState.OffsetY);
        _isApplyingState = false;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isApplyingState || ViewportState is null || Source is null || _isViewportApplyQueued)
        {
            return;
        }

        // Ignore layout/source replacement churn. Only user-driven offset changes should
        // overwrite the shared viewport state.
        if (Math.Abs(e.ExtentWidthChange) > 0.0001d ||
            Math.Abs(e.ExtentHeightChange) > 0.0001d ||
            Math.Abs(e.ViewportWidthChange) > 0.0001d ||
            Math.Abs(e.ViewportHeightChange) > 0.0001d)
        {
            return;
        }

        ViewportState.Update(ViewportState.Zoom, e.HorizontalOffset, e.VerticalOffset);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewportState is null)
        {
            return;
        }

        var oldZoom = ViewportState.Zoom;
        var newZoom = e.Delta > 0 ? oldZoom * 1.1d : oldZoom / 1.1d;
        newZoom = Math.Clamp(newZoom, 0.1d, 12d);

        if (!TryGetImagePoints(e, out var sourcePoint, out _))
        {
            return;
        }

        var viewPosition = e.GetPosition(ViewerScroll);

        ViewportState.Update(
            newZoom,
            (sourcePoint.X * newZoom) - viewPosition.X,
            (sourcePoint.Y * newZoom) - viewPosition.Y);

        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var isOnImage = TryGetImagePoints(e, out var sourcePoint, out var displayPoint);

        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left && isOnImage)
        {
            ViewerDoubleClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (isOnImage && (e.ChangedButton == MouseButton.Middle || (LeftPanEnabled && e.ChangedButton == MouseButton.Left)))
        {
            _isPanning = true;
            _panStartPosition = e.GetPosition(this);
            _panStartOffsetX = ViewerScroll.HorizontalOffset;
            _panStartOffsetY = ViewerScroll.VerticalOffset;
            ViewerScroll.CaptureMouse();
            Cursor = InteractionCursor;
            e.Handled = true;
            return;
        }

        if (isOnImage)
        {
            PointerPressed?.Invoke(this, new ImagePointerEventArgs(sourcePoint, displayPoint, e.LeftButton == MouseButtonState.Pressed));
            Cursor = InteractionCursor;
            return;
        }

        Cursor = Cursors.Arrow;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
        {
            EndPanCapture();
        }

        if (_isPanning && ViewportState is not null)
        {
            var current = e.GetPosition(this);
            var delta = current - _panStartPosition;
            ViewportState.Update(ViewportState.Zoom, _panStartOffsetX - delta.X, _panStartOffsetY - delta.Y);
            Cursor = InteractionCursor;
            e.Handled = true;
            return;
        }

        if (TryGetImagePoints(e, out var sourcePoint, out var displayPoint))
        {
            Cursor = InteractionCursor;
            PointerMoved?.Invoke(this, new ImagePointerEventArgs(sourcePoint, displayPoint, e.LeftButton == MouseButtonState.Pressed));
            return;
        }

        Cursor = Cursors.Arrow;
        PointerExited?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && (e.ChangedButton == MouseButton.Middle || (LeftPanEnabled && e.ChangedButton == MouseButton.Left)))
        {
            EndPanCapture();
            e.Handled = true;
            return;
        }

        if (TryGetImagePoints(e, out var sourcePoint, out var displayPoint))
        {
            Cursor = InteractionCursor;
            PointerReleased?.Invoke(this, new ImagePointerEventArgs(sourcePoint, displayPoint, e.LeftButton == MouseButtonState.Pressed));
            return;
        }

        Cursor = Cursors.Arrow;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Cursor = Cursors.Arrow;
        PointerExited?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewerLostMouseCapture(object sender, MouseEventArgs e) => EndPanCapture();

    private void EndPanCapture()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (ViewerScroll.IsMouseCaptured)
        {
            ViewerScroll.ReleaseMouseCapture();
        }

        Cursor = Cursors.Arrow;
    }

    private bool TryGetImagePoints(MouseEventArgs e, out Point sourcePoint, out Point displayPoint)
    {
        sourcePoint = default;
        displayPoint = default;
        if (Source is null || ViewportState is null)
        {
            DebugLog.WriteLine($"TryGetImagePoint failed: source-or-viewport-null source={Source is not null} viewport={ViewportState is not null}");
            return false;
        }

        if (IsFromScrollBar(e.OriginalSource as DependencyObject))
        {
            DebugLog.WriteLine("TryGetImagePoint failed: original-source-scrollbar");
            return false;
        }

        GeneralTransform transform;
        try
        {
            transform = BaseImage.TransformToAncestor(ViewerScroll);
        }
        catch (InvalidOperationException exception)
        {
            DebugLog.WriteLine($"TryGetImagePoint failed: transform-unavailable message={exception.Message}");
            return false;
        }

        var inverse = transform.Inverse;
        if (inverse is null)
        {
            DebugLog.WriteLine("TryGetImagePoint failed: inverse-transform-null");
            return false;
        }

        var viewPosition = e.GetPosition(ViewerScroll);
        var local = inverse.Transform(viewPosition);
        var x = local.X;
        var y = local.Y;
        var success = !(x < 0d || y < 0d || x >= Source.Width || y >= Source.Height);
        var transformedBounds = transform.TransformBounds(new Rect(0d, 0d, Source.Width, Source.Height));

        DebugLog.WriteLine(
            $"TryGetImagePoint success={success} raw=({viewPosition.X:0.###},{viewPosition.Y:0.###}) image=({x:0.###},{y:0.###}) sourceSize=({Source.Width:0.###},{Source.Height:0.###}) actualSize=({BaseImage.ActualWidth:0.###},{BaseImage.ActualHeight:0.###}) transformedBounds=({transformedBounds.X:0.###},{transformedBounds.Y:0.###},{transformedBounds.Width:0.###},{transformedBounds.Height:0.###}) offsets=({ViewerScroll.HorizontalOffset:0.###},{ViewerScroll.VerticalOffset:0.###})");

        if (!success)
        {
            return false;
        }

        displayPoint = new Point(x, y);
        sourcePoint = new Point(x / Math.Max(0.0001d, CoordinateScaleX), y / Math.Max(0.0001d, CoordinateScaleY));
        return true;
    }

    private static bool IsFromScrollBar(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnTitleButtonClick(object sender, RoutedEventArgs e) => TitleClicked?.Invoke(this, EventArgs.Empty);
}
