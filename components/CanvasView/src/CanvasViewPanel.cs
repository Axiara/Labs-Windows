// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;

// NOTE:
// CanvasView in Labs-Windows is multi-targeted across WinUI3/UWP in the repo.
// If your branch uses different compilation symbols/namespaces, adjust these usings
// to match the existing CanvasView.cs pattern.
#if WINAPPSDK
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
#endif

namespace CommunityToolkit.WinUI.Controls
{
    /// <summary>
    /// Items host for CanvasView.
    /// Key behavior:
    /// - Positions each *item container* using Canvas.Left/Top values found on the container's template root
    ///   (ContentPresenter.ContentTemplateRoot), falling back to the container itself.
    /// - Computes DesiredSize from the union of all item bounds so ScrollViewer gets a correct extent.
    /// - Tracks negative coordinates via ContentOffset so content remains reachable in a ScrollViewer.
    /// </summary>
    // NOTE (AOT/WinRT ABI):
    // This panel is instantiated by the XAML framework and participates in native layout callbacks.
    // When trimming/AOT is enabled, C#/WinRT requires types crossing the WinRT ABI to be partial so
    // it can generate the necessary COM callable wrappers/vtables (otherwise E_NOINTERFACE/InvalidCastException).
    public sealed partial class CanvasViewPanel : Panel
    {
        private Rect _contentBounds = Rect.Empty;
        private Point _contentOffset = new Point(0, 0);

        internal Rect ContentBounds => _contentBounds;

        internal Point ContentOffset => _contentOffset;

        internal event EventHandler? ContentBoundsChanged;

        // Used to watch Canvas.Left/Top changes on children and/or their template root.
        private readonly Dictionary<DependencyObject, (long leftToken, long topToken)> _tokens = new();

        private const string PositioningElementPropertyName = "PositioningElement";

        // Attached property to allow CanvasView to tell the panel which element should be used
        // for reading Canvas.Left/Top (e.g., template root instead of container).
        internal static readonly DependencyProperty PositioningElementProperty =
            DependencyProperty.RegisterAttached(
                (string)PositioningElementPropertyName,
                typeof(DependencyObject),
                typeof(CanvasViewPanel),
                new PropertyMetadata(null, OnPositioningElementChanged));

        internal static void SetPositioningElement(DependencyObject element, DependencyObject value)
            => element.SetValue(PositioningElementProperty, value);

        internal static DependencyObject? GetPositioningElement(DependencyObject element)
            => element.GetValue(PositioningElementProperty) as DependencyObject;

        private static void OnPositioningElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // When the positioning element changes, we want to re-measure.
            if (d is UIElement ui)
            {
                // The parent will handle hook-up during Measure pass; just invalidate.
                ui.InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Measure children normally.
            foreach (UIElement child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            // Compute bounds based on Canvas.Left/Top + DesiredSize.
            // Also support negative coordinates by producing an offset that shifts content into positive space.
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
                // If minX/minY are positive, we must still include the empty space from 0..minX/minY.
                double desiredWidth = CoerceFiniteNonNegative(maxX + offsetX);
                double desiredHeight = CoerceFiniteNonNegative(maxY + offsetY);
                
                newBounds = new Rect(minX, minY, boundsWidth, boundsHeight);
                newOffset = new Point(offsetX, offsetY);
                desired = new Size(desiredWidth, desiredHeight);
            }

            UpdateBounds(newBounds, newOffset);
            return desired;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // IMPORTANT: Do not apply a viewport-anchored Clip here.
            // ScrollViewer (or the parent) owns viewport clipping. Clipping here would
            // anchor to content space and recreate the “initial viewport only” bug.

            foreach (UIElement child in Children)
            {
                DependencyObject positioningElement = GetPositioningElement(child) ?? (DependencyObject)child;

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
            // We want to invalidate measure if Canvas.Left/Top changes on either:
            // - the container child itself, OR
            // - the assigned positioning element (template root)
            // This avoids the case where the template root is where Canvas.Left/Top is set.

            HookDependencyObject(child, child);
            if (positioningElement != null && positioningElement != child)
            {
                HookDependencyObject(child, positioningElement);
            }
        }

        private void HookDependencyObject(UIElement ownerToInvalidate, DependencyObject target)
        {
            if (target is null)
            {
                return;
            }

            if (_tokens.ContainsKey(target))
            {
                return;
            }

            long leftToken = RegisterPropertyChangedCallbackSafe(target, Canvas.LeftProperty, () => InvalidateMeasure());
            long topToken = RegisterPropertyChangedCallbackSafe(target, Canvas.TopProperty, () => InvalidateMeasure());

            _tokens[target] = (leftToken, topToken);

            // Cleanup when element is unloaded (best-effort).
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
                // Some objects may not support callback registration in certain target frameworks.
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
    }
}
