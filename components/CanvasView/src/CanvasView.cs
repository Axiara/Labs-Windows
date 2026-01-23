// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Collections.Generic;

namespace CommunityToolkit.WinUI.Controls;

/// <summary>
/// <see cref="CanvasView"/> is an <see cref="ItemsControl"/> which uses a <see cref="CanvasViewPanel"/>
/// for the layout of its items. It provides built-in support for presenting a collection of items
/// bound to specific coordinates and drag-and-drop support of those items.
/// </summary>
public partial class CanvasView : ItemsControl
{
    private readonly (DependencyProperty, string)[] _liftedProperties = new (DependencyProperty, string)[]
    {
        (Canvas.LeftProperty, "(Canvas.Left)"),
        (Canvas.TopProperty, "(Canvas.Top)"),
        (Canvas.ZIndexProperty, "(Canvas.ZIndex)"),
        (ManipulationModeProperty, "ManipulationMode")
    };

    private readonly HashSet<ContentPresenter> _pendingChildBindings = new();
    private CanvasViewPanel? _itemsHost;

    /// <summary>
    /// Gets the bounding rectangle of all children in canvas coordinates.
    /// </summary>
    public Rect ContentBounds
    {
        get => (Rect)GetValue(ContentBoundsProperty);
        private set => SetValue(ContentBoundsProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="ContentBounds"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentBoundsProperty =
        DependencyProperty.Register(nameof(ContentBounds), typeof(Rect), typeof(CanvasView),
            new PropertyMetadata(new Rect(0, 0, 0, 0)));

    /// <summary>
    /// Gets the offset applied to shift negative coordinates into positive space.
    /// </summary>
    public Point ContentOffset
    {
        get => (Point)GetValue(ContentOffsetProperty);
        private set => SetValue(ContentOffsetProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="ContentOffset"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentOffsetProperty =
        DependencyProperty.Register(nameof(ContentOffset), typeof(Point), typeof(CanvasView),
            new PropertyMetadata(new Point(0, 0)));

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasView"/> class.
    /// </summary>
    public CanvasView()
    {
        // ItemsPanel is provided by the default style in Themes/Generic.xaml.
        // This avoids WinUI 3 runtime XamlReader.Load limitations with custom types.
        DefaultStyleKey = typeof(CanvasView);
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_itemsHost is not null)
        {
            _itemsHost.ContentBoundsChanged -= OnHostBoundsChanged;
        }

        _itemsHost = FindDescendant<CanvasViewPanel>(this);

        if (_itemsHost is not null)
        {
            _itemsHost.ContentBoundsChanged += OnHostBoundsChanged;
            SyncBoundsFromHost();
        }
    }

    /// <inheritdoc/>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is ContentPresenter cp)
        {
            // Use LayoutUpdated as a cross-platform signal that the template root is realized.
            EnsureChildBinding(cp);
            cp.Loaded += ContentPresenter_Loaded;
            cp.ManipulationDelta += ContentPresenter_ManipulationDelta;
        }
    }

    /// <inheritdoc/>
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        base.ClearContainerForItemOverride(element, item);

        if (element is ContentPresenter cp)
        {
            _pendingChildBindings.Remove(cp);
            cp.LayoutUpdated -= ContentPresenter_LayoutUpdated;
            cp.Loaded -= ContentPresenter_Loaded;
            cp.ManipulationDelta -= ContentPresenter_ManipulationDelta;
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
        if (TrySetupChildBinding(cp))
        {
            _pendingChildBindings.Remove(cp);
            cp.LayoutUpdated -= ContentPresenter_LayoutUpdated;
            return;
        }

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

        // Tell the items host to read Canvas.Left/Top from the template root.
        CanvasViewPanel.SetPositioningElement(cp, child);

        // Hook up lifted properties from the template child to the container.
        foreach ((var prop, var path) in _liftedProperties)
        {
            var binding = new Binding
            {
                Source = child,
                Path = new PropertyPath(path)
            };
            cp.SetBinding(prop, binding);
        }

        return true;
    }

#if NET8_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "SetBindingExpressionValue uses reflection; prefer ICanvasViewPositionable for trim/AOT.")]
#endif
    private void ContentPresenter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is ContentPresenter cp)
        {
            cp.SetBindingExpressionValue(Canvas.LeftProperty, Canvas.GetLeft(cp) + e.Delta.Translation.X);
            cp.SetBindingExpressionValue(Canvas.TopProperty, Canvas.GetTop(cp) + e.Delta.Translation.Y);
        }
    }

    private void OnHostBoundsChanged(object? sender, EventArgs e) => SyncBoundsFromHost();

    private void SyncBoundsFromHost()
    {
        if (_itemsHost is null)
        {
            return;
        }

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
