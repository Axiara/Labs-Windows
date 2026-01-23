// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Windows.Foundation;

#if WINAPPSDK
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
#endif

namespace CommunityToolkit.WinUI.Controls;

/// <summary>
/// Items host for <see cref="CanvasView"/>.
/// </summary>
/// <remarks>
/// <para>
/// Positions each item container using Canvas.Left/Top values found on the container's template root
/// (ContentPresenter.ContentTemplateRoot), falling back to the container itself.
/// </para>
/// <para>
/// Computes DesiredSize from the union of all item bounds so ScrollViewer gets a correct extent.
/// Tracks negative coordinates via <see cref="ContentOffset"/> so content remains reachable in a ScrollViewer.
/// </para>
/// </remarks>
public partial class CanvasViewPanel : Panel
{
    private Rect _contentBounds = Rect.Empty;
    private Point _contentOffset = new Point(0, 0);

    /// <summary>
    /// Gets the bounding rectangle of all children in canvas coordinates.
    /// </summary>
    internal Rect ContentBounds => _contentBounds;

    /// <summary>
    /// Gets the offset applied to shift negative coordinates into positive space.
    /// </summary>
    internal Point ContentOffset => _contentOffset;

    /// <summary>
    /// Raised when <see cref="ContentBounds"/> or <see cref="ContentOffset"/> changes.
    /// </summary>
    internal event EventHandler? ContentBoundsChanged;

    private readonly Dictionary<DependencyObject, (UIElement owner, long leftToken, long topToken)> _tokens = new();

    private const string PositioningElementPropertyName = "PositioningElement";

    /// <summary>
    /// Attached property allowing <see cref="CanvasView"/> to specify which element provides Canvas.Left/Top.
    /// </summary>
    internal static readonly DependencyProperty PositioningElementProperty =
        DependencyProperty.RegisterAttached(
            PositioningElementPropertyName,
            typeof(DependencyObject),
            typeof(CanvasViewPanel),
            new PropertyMetadata(null, OnPositioningElementChanged));

    internal static void SetPositioningElement(DependencyObject element, DependencyObject value)
        => element.SetValue(PositioningElementProperty, value);

    internal static DependencyObject? GetPositioningElement(DependencyObject element)
        => element.GetValue(PositioningElementProperty) as DependencyObject;

    private static void OnPositioningElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement ui)
        {
            ui.InvalidateMeasure();
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        double minX = 0;
        double minY = 0;
        double maxX = 0;
        double maxY = 0;
        bool hasAny = false;

        foreach (UIElement child in Children)
        {
            DependencyObject positioningElement = GetPositioningElement(child) ?? child;
            HookPositionWatch(child, positioningElement);

            double left = ReadCanvasLeft(positioningElement);
            double top = ReadCanvasTop(positioningElement);
            Size size = child.DesiredSize;
            double w = CoerceFiniteNonNegative(size.Width);
            double h = CoerceFiniteNonNegative(size.Height);

            if (!hasAny)
            {
                hasAny = true;
                minX = left;
                minY = top;
                maxX = left + w;
                maxY = top + h;
            }
            else
            {
                minX = Math.Min(minX, left);
                minY = Math.Min(minY, top);
                maxX = Math.Max(maxX, left + w);
                maxY = Math.Max(maxY, top + h);
            }
        }

        Rect newBounds;
        Point newOffset;
        Size desired;

        if (!hasAny)
        {
            newBounds = Rect.Empty;
            newOffset = new Point(0, 0);
            desired = new Size(0, 0);
        }
        else
        {
            double boundsWidth = Math.Max(0, maxX - minX);
            double boundsHeight = Math.Max(0, maxY - minY);
            double offsetX = minX < 0 ? -minX : 0;
            double offsetY = minY < 0 ? -minY : 0;

            // DesiredSize must be relative to (0,0) after applying offset for negative coordinates.
            double desiredWidth = CoerceFiniteNonNegative(maxX + offsetX);
            double desiredHeight = CoerceFiniteNonNegative(maxY + offsetY);

            newBounds = new Rect(minX, minY, boundsWidth, boundsHeight);
            newOffset = new Point(offsetX, offsetY);
            desired = new Size(desiredWidth, desiredHeight);
        }

        UpdateBounds(newBounds, newOffset);
        return desired;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            DependencyObject positioningElement = GetPositioningElement(child) ?? child;
            double left = ReadCanvasLeft(positioningElement) + _contentOffset.X;
            double top = ReadCanvasTop(positioningElement) + _contentOffset.Y;
            Size size = child.DesiredSize;
            child.Arrange(new Rect(left, top, size.Width, size.Height));
        }

        return finalSize;
    }

    private void UpdateBounds(Rect bounds, Point offset)
    {
        if (_contentBounds != bounds || _contentOffset != offset)
        {
            _contentBounds = bounds;
            _contentOffset = offset;
            ContentBoundsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static double ReadCanvasLeft(DependencyObject d)
    {
        if (d is UIElement element)
        {
            double value = Canvas.GetLeft(element);
            return (double.IsNaN(value) || double.IsInfinity(value)) ? 0 : value;
        }

        return 0;
    }

    private static double ReadCanvasTop(DependencyObject d)
    {
        if (d is UIElement element)
        {
            double value = Canvas.GetTop(element);
            return (double.IsNaN(value) || double.IsInfinity(value)) ? 0 : value;
        }

        return 0;
    }

    private void HookPositionWatch(UIElement child, DependencyObject positioningElement)
    {
        HookDependencyObject(child, child);
        if (positioningElement != null && positioningElement != child)
        {
            HookDependencyObject(child, positioningElement);
        }
    }

    private void HookDependencyObject(UIElement ownerToInvalidate, DependencyObject target)
    {
        if (target is null || _tokens.ContainsKey(target))
        {
            return;
        }

        long leftToken = RegisterPropertyChangedCallbackSafe(target, Canvas.LeftProperty, () => OnCanvasPositionChanged(target));
        long topToken = RegisterPropertyChangedCallbackSafe(target, Canvas.TopProperty, () => OnCanvasPositionChanged(target));

        _tokens[target] = (ownerToInvalidate, leftToken, topToken);

        if (target is FrameworkElement fe)
        {
            fe.Unloaded += OnTargetUnloaded;
        }
    }

    private void OnTargetUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d)
        {
            UnhookDependencyObject(d);
        }
    }

    private void UnhookDependencyObject(DependencyObject d)
    {
        if (_tokens.TryGetValue(d, out var t))
        {
            if (t.leftToken != 0)
            {
                d.UnregisterPropertyChangedCallback(Canvas.LeftProperty, t.leftToken);
            }

            if (t.topToken != 0)
            {
                d.UnregisterPropertyChangedCallback(Canvas.TopProperty, t.topToken);
            }

            _tokens.Remove(d);

            if (d is FrameworkElement fe)
            {
                fe.Unloaded -= OnTargetUnloaded;
            }
        }
    }

    private static long RegisterPropertyChangedCallbackSafe(DependencyObject d, DependencyProperty dp, Action callback)
    {
        try
        {
            return d.RegisterPropertyChangedCallback(dp, (_, __) => callback());
        }
        catch
        {
            return 0;
        }
    }

    private static double CoerceFiniteNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value;
    }

    private void OnCanvasPositionChanged(DependencyObject target)
    {
        InvalidateArrange();

        if (!_tokens.TryGetValue(target, out var info))
        {
            return;
        }

        if (_contentBounds == Rect.Empty)
        {
            InvalidateMeasure();
            return;
        }

        double left = ReadCanvasLeft(target);
        double top = ReadCanvasTop(target);
        Size size = info.owner.DesiredSize;
        double w = CoerceFiniteNonNegative(size.Width);
        double h = CoerceFiniteNonNegative(size.Height);

        double minX = _contentBounds.X;
        double minY = _contentBounds.Y;
        double maxX = _contentBounds.X + _contentBounds.Width;
        double maxY = _contentBounds.Y + _contentBounds.Height;

        if (left < minX || top < minY || (left + w) > maxX || (top + h) > maxY)
        {
            InvalidateMeasure();
        }
    }
}
