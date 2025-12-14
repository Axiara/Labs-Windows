// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace CommunityToolkit.WinUI.Controls;

/// <summary>
/// <see cref="CanvasView"/> is an <see cref="ItemsControl"/> which uses a <see cref="Canvas"/> for the layout of its items.
/// It which provides built-in support for presenting a collection of items bound to specific coordinates 
/// and drag-and-drop support of those items.
/// </summary>
/// 
public partial class CanvasView : ItemsControl
{
    private (DependencyProperty, string)[] LiftedProperties = new (DependencyProperty, string)[] {
        (Canvas.LeftProperty, "(Canvas.Left)"),
        (Canvas.TopProperty, "(Canvas.Top)"),
        (Canvas.ZIndexProperty, "(Canvas.ZIndex)"),
        (ManipulationModeProperty, "ManipulationMode")
    };

    public Rect ContentBounds
    {
        get => (Rect)GetValue(ContentBoundsProperty);
        private set => SetValue(ContentBoundsProperty, value);
    }
    public static readonly DependencyProperty ContentBoundsProperty =
        DependencyProperty.Register(nameof(ContentBounds), typeof(Rect), typeof(CanvasView),
            new PropertyMetadata(new Rect(0, 0, 0, 0)));

    public Point ContentOffset
    {
        get => (Point)GetValue(ContentOffsetProperty);
        private set => SetValue(ContentOffsetProperty, value);
    }
    public static readonly DependencyProperty ContentOffsetProperty =
        DependencyProperty.Register(nameof(ContentOffset), typeof(Point), typeof(CanvasView),
            new PropertyMetadata(new Point(0, 0)));


    /// <summary>
    /// Controls how CanvasView updates the dragged item's position.
    /// <list type="bullet">
    /// <item><see cref="CanvasViewDragPositionUpdateMode.BindingExpression"/> preserves existing bindings by writing to the binding source (reflection-based).</item>
    /// <item><see cref="CanvasViewDragPositionUpdateMode.DataContextInterface"/> updates <see cref="ICanvasViewPositionable"/> on the DataContext (trim/AOT-friendly).</item>
    /// <item><see cref="CanvasViewDragPositionUpdateMode.CanvasAttachedProperties"/> writes Canvas.Left/Top directly (may overwrite bindings).</item>
    /// <item><see cref="CanvasViewDragPositionUpdateMode.Auto"/> prefers the interface path; on WebAssembly it avoids reflection by default.</item>
    /// </list>
    /// </summary>
    public CanvasViewDragPositionUpdateMode DragPositionUpdateMode { get; set; } = CanvasViewDragPositionUpdateMode.Auto;

#if __UNO__
    // Uno currently doesn't automatically discover default styles from Themes/Generic.xaml in referenced control libraries on all targets
    // (notably WebAssembly). Ensure the control library's Generic.xaml is merged at runtime as a fallback.
    // Apps can still explicitly merge the dictionary in App.xaml if they prefer.
    private static bool _unoResourcesMerged;

    private static void EnsureUnoResourcesMerged()
    {
        if (_unoResourcesMerged)
        {
            return;
        }

        try
        {
            var app = Application.Current;
            if (app is null)
            {
                return;
            }

            // If the implicit style is already reachable, nothing to do.
            if (app.Resources?.ContainsKey(typeof(CanvasView)) == true)
            {
                _unoResourcesMerged = true;
                return;
            }

            string? assemblyName = typeof(CanvasView).Assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
            {
                return;
            }

            var uri = new global::System.Uri($"ms-appx:///{assemblyName}/Themes/Generic.xaml");
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });

            _unoResourcesMerged = true;
        }
        catch
        {
            // Swallow - app can still merge resources manually.
        }
    }
#endif

    public CanvasView()
    {
#if __UNO__
        EnsureUnoResourcesMerged();
#endif

        // ItemsPanel is provided by the default style in Themes/Generic.xaml.
        // This avoids WinUI 3 runtime XamlReader.Load limitations with custom types.
        DefaultStyleKey = typeof(CanvasView);
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        // ContentPresenter is the default container for CanvasView items.
        if (element is ContentPresenter cp)
        {
            // We need to wait for the ContentPresenter to realize its template root before we can:
            //  - lift Canvas.(Left/Top/ZIndex) from the template root onto the container
            //  - hook drag handlers on the template root
            //
            // CompositionTarget-based scheduling is not reliable across all Uno targets (e.g. WebAssembly),
            // so we use LayoutUpdated as a cross-platform "template realized" signal.
            EnsureChildBinding(cp);

            // Loaded still helps on Windows, but isn't always raised reliably for dynamically added items.
            cp.Loaded += ContentPresenter_Loaded;
        }

        // TODO: Do we want to support something else in a custom template?? else if (item is FrameworkElement fe && fe.FindDescendant/GetContentControl?)
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        base.ClearContainerForItemOverride(element, item);

        if (element is ContentPresenter cp)
        {
            _pendingChildBindings.Remove(cp);
            cp.LayoutUpdated -= ContentPresenter_LayoutUpdated;

            if (CanvasViewPanel.GetPositioningElement(cp) is UIElement root)
            {
                UnhookDragHandlers(root);
            }

            cp.Loaded -= ContentPresenter_Loaded;
        }
    }

    private void ContentPresenter_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentPresenter cp)
        {
            cp.Loaded -= ContentPresenter_Loaded;

            EnsureChildBinding(cp);
        }
    }

    private void EnsureChildBinding(ContentPresenter cp)
    {
        // If the template root is ready, bind + hook immediately.
        if (TrySetupChildBinding(cp))
        {
            _pendingChildBindings.Remove(cp);
            cp.LayoutUpdated -= ContentPresenter_LayoutUpdated;
            return;
        }

        // Otherwise, schedule a retry after the next layout pass.
        if (_pendingChildBindings.Add(cp))
        {
            cp.LayoutUpdated += ContentPresenter_LayoutUpdated;
        }
    }

    private void ContentPresenter_LayoutUpdated(object? sender, object e)
    {
        if (sender is ContentPresenter cp)
        {
            cp.LayoutUpdated -= ContentPresenter_LayoutUpdated;
            _pendingChildBindings.Remove(cp);

            // Try again now that layout has progressed.
            EnsureChildBinding(cp);
        }
    }

    private bool TrySetupChildBinding(ContentPresenter cp)
    {
        int count = VisualTreeHelper.GetChildrenCount(cp);
        if (count <= 0)
        {
            return false;
        }

        DependencyObject child = VisualTreeHelper.GetChild(cp, 0);

        // Tell the items host to read Canvas.Left/Top from the template root (or first realized visual).
        CanvasViewPanel.SetPositioningElement(cp, child);

        if (child is UIElement root)
        {
            HookDragHandlers(root);
        }

        // TODO: Should we avoid doing this twice?

        // Hook up any properties we care about from the templated children to it's parent ContentPresenter.
        foreach ((var prop, var path) in LiftedProperties)
        {
            var binding = new Binding();
            binding.Source = child;
            ////binding.Mode = BindingMode.TwoWay; // TODO: Should this be exposed as a general property?
            binding.Path = new PropertyPath(path);

            cp.SetBinding(prop, binding);
        }

        return true;
    }

    private sealed class DragSession
    {
        public DragSession(UIElement target, Point startPointerView, double startLeft, double startTop)
        {
            Target = target;
            StartPointerView = startPointerView;
            StartLeft = startLeft;
            StartTop = startTop;
        }

        public UIElement Target { get; }
        public Point StartPointerView { get; }
        public double StartLeft { get; }
        public double StartTop { get; }
    }

    private readonly HashSet<UIElement> _dragTargets = new();
    private readonly Dictionary<uint, DragSession> _drags = new();
    private readonly HashSet<ContentPresenter> _pendingChildBindings = new();

    private void HookDragHandlers(UIElement root)
    {
        if (!_dragTargets.Add(root))
        {
            return;
        }

        root.PointerPressed += OnItemPointerPressed;
        root.PointerMoved += OnItemPointerMoved;
        root.PointerReleased += OnItemPointerReleased;
        root.PointerCanceled += OnItemPointerCanceled;
        root.PointerCaptureLost += OnItemPointerCaptureLost;

        if (root is FrameworkElement fe)
        {
            fe.Unloaded += OnItemUnloaded;
        }
    }

    private void UnhookDragHandlers(UIElement root)
    {
        if (!_dragTargets.Remove(root))
        {
            return;
        }

        root.PointerPressed -= OnItemPointerPressed;
        root.PointerMoved -= OnItemPointerMoved;
        root.PointerReleased -= OnItemPointerReleased;
        root.PointerCanceled -= OnItemPointerCanceled;
        root.PointerCaptureLost -= OnItemPointerCaptureLost;

        if (root is FrameworkElement fe)
        {
            fe.Unloaded -= OnItemUnloaded;
        }
    }

    private void OnItemUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement root)
        {
            UnhookDragHandlers(root);
        }
    }

    private Point ViewToHost(Point viewPoint)
    {
        if (_itemsHost is null)
        {
            return viewPoint;
        }

        GeneralTransform transform = TransformToVisual(_itemsHost);
        return transform.TransformPoint(viewPoint);
    }

    private Point ViewToWorld(Point viewPoint)
    {
        Point hostPoint = ViewToHost(viewPoint);

        if (_itemsHost is null)
        {
            return hostPoint;
        }

        Point offset = _itemsHost.ContentOffset;
        return new Point(hostPoint.X - offset.X, hostPoint.Y - offset.Y);
    }

    private static double ReadCanvasLeft(UIElement element)
    {
        double value = Canvas.GetLeft(element);
        return double.IsNaN(value) ? 0 : value;
    }

    private static double ReadCanvasTop(UIElement element)
    {
        double value = Canvas.GetTop(element);
        return double.IsNaN(value) ? 0 : value;
    }

    private static bool TryUpdatePositionableDataItem(FrameworkElement fe, double left, double top)
    {
        if (fe.DataContext is ICanvasViewPositionable ctx)
        {
            ctx.X = left;
            ctx.Y = top;
            return true;
        }

        // Also support ElementName / RelativeSource bindings where DataItem isn't the DataContext.
        var leftBinding = fe.GetBindingExpression(Canvas.LeftProperty);
        if (leftBinding?.DataItem is FrameworkElement subfe)
        {
            if (subfe.DataContext is ICanvasViewPositionable subctx)
            {
                subctx.X = left;
                subctx.Y = top;
                return true;
            }
        }
        else if (leftBinding?.DataItem is ICanvasViewPositionable dataItem)
        {
            dataItem.X = left;
            dataItem.Y = top;
            return true;
        }

        return false;
    }

#if NET8_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingExpression-based dragging updates the source via reflection. Prefer DragPositionUpdateMode=DataContextInterface for trim/AOT targets.")]
#endif
    private void SetCanvasPosition(UIElement element, double left, double top)
    {
        // Prefer trim/AOT-friendly updates when possible.
        if (element is FrameworkElement fe)
        {
            switch (DragPositionUpdateMode)
            {
                case CanvasViewDragPositionUpdateMode.DataContextInterface:
                    if (TryUpdatePositionableDataItem(fe, left, top))
                    {
                        return;
                    }
                    break;

                case CanvasViewDragPositionUpdateMode.BindingExpression:
                    fe.SetBindingExpressionValue(Canvas.LeftProperty, left);
                    fe.SetBindingExpressionValue(Canvas.TopProperty, top);
                    return;

                case CanvasViewDragPositionUpdateMode.CanvasAttachedProperties:
                    Canvas.SetLeft(fe, left);
                    Canvas.SetTop(fe, top);
                    return;

                case CanvasViewDragPositionUpdateMode.Auto:
                default:
                    if (TryUpdatePositionableDataItem(fe, left, top))
                    {
                        return;
                    }

#if __WASM__
                    // WebAssembly builds are commonly trimmed/AOT; avoid reflection by default.
                    Canvas.SetLeft(fe, left);
                    Canvas.SetTop(fe, top);
                    return;
#else
                    fe.SetBindingExpressionValue(Canvas.LeftProperty, left);
                    fe.SetBindingExpressionValue(Canvas.TopProperty, top);
                    return;
#endif
            }
        }

        // Fallback for non-FrameworkElement (rare).
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    private void OnItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement target)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);

        // Require left button (mouse) OR an in-contact pointer (touch/pen).
        if (!point.Properties.IsLeftButtonPressed && !point.IsInContact)
        {
            return;
        }

        target.CapturePointer(e.Pointer);

        Point startPointerView = point.Position;

        double startLeft = ReadCanvasLeft(target);
        double startTop = ReadCanvasTop(target);

        _drags[e.Pointer.PointerId] = new DragSession(target, startPointerView, startLeft, startTop);

        e.Handled = true;
    }

    private void OnItemPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_drags.TryGetValue(e.Pointer.PointerId, out DragSession? drag))
        {
            return;
        }

        Point currentView = e.GetCurrentPoint(this).Position;
        
        double dx;
        double dy;
        
        if (_itemsHost is null)
        {
            dx = currentView.X - drag.StartPointerView.X;
            dy = currentView.Y - drag.StartPointerView.Y;
        }
        else
        {
            GeneralTransform t = TransformToVisual(_itemsHost);
            Point startHost = t.TransformPoint(drag.StartPointerView);
            Point currentHost = t.TransformPoint(currentView);
            
            dx = currentHost.X - startHost.X;
            dy = currentHost.Y - startHost.Y;
        }

        SetCanvasPosition(drag.Target, drag.StartLeft + dx, drag.StartTop + dy);

        // Ensure extents update even on platforms where DP change callbacks aren't supported.
        _itemsHost?.InvalidateMeasure();

        e.Handled = true;
    }

    private void OnItemPointerReleased(object sender, PointerRoutedEventArgs e) => EndDrag(e);
    private void OnItemPointerCanceled(object sender, PointerRoutedEventArgs e) => EndDrag(e);
    private void OnItemPointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag(e);

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (_drags.TryGetValue(e.Pointer.PointerId, out DragSession? drag))
        {
            drag.Target.ReleasePointerCapture(e.Pointer);
            _drags.Remove(e.Pointer.PointerId);
            _itemsHost?.InvalidateMeasure();
        }
    }

    private CanvasViewPanel? _itemsHost;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old
        if (_itemsHost is not null)
        {
            _itemsHost.ContentBoundsChanged -= OnHostBoundsChanged;
        }

        _itemsHost = FindDescendant<CanvasViewPanel>(this);
        if (_itemsHost is not null)
        {
            _itemsHost.ContentBoundsChanged += OnHostBoundsChanged;

            // Critical: ensure no content-anchored clip survives:
            _itemsHost.Clip = null;

            SyncBoundsFromHost();
        }
    }

    private void OnHostBoundsChanged(object? sender, EventArgs e) => SyncBoundsFromHost();

    private void SyncBoundsFromHost()
    {
        if (_itemsHost is null) return;
        ContentBounds = _itemsHost.ContentBounds;
        ContentOffset = _itemsHost.ContentOffset;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            T? nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return default;
    }

}
