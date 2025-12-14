// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace CommunityToolkit.WinUI.Controls;

/// <summary>
/// Controls how <see cref="CanvasView"/> applies drag position updates to items.
/// </summary>
public enum CanvasViewDragPositionUpdateMode
{
    /// <summary>
    /// Use the most appropriate approach for the current platform:
    /// <list type="bullet">
    /// <item>Prefer <see cref="DataContextInterface"/> when the DataItem implements <see cref="ICanvasViewPositionable"/>.</item>
    /// <item>On WebAssembly, avoid reflection by default (fallback to setting Canvas.Left/Top directly).</item>
    /// <item>On Windows, preserve bindings by updating the binding source (reflection-based).</item>
    /// </list>
    /// </summary>
    Auto,

    /// <summary>
    /// Preserve existing bindings by updating the binding source through <see cref="FrameworkElementExtensions.SetBindingExpressionValue(FrameworkElement, DependencyProperty, object)"/>.
    /// This uses reflection and is not trimming/AOT friendly.
    /// </summary>
    BindingExpression,

    /// <summary>
    /// Update the DataItem by casting it to <see cref="ICanvasViewPositionable"/> (trim/AOT friendly).
    /// Bind Canvas.Left/Top to <see cref="ICanvasViewPositionable.X"/>/<see cref="ICanvasViewPositionable.Y"/> for full binding preservation.
    /// </summary>
    DataContextInterface,

    /// <summary>
    /// Set Canvas.Left/Top directly on the dragged element. This is the most compatible approach,
    /// but it may overwrite bindings on Canvas.Left/Top.
    /// </summary>
    CanvasAttachedProperties,
}

/// <summary>
/// Optional interface for drag updates without reflection.
/// Implement this on your item/viewmodel and bind Canvas.Left/Top to <see cref="X"/>/<see cref="Y"/>.
/// </summary>
public interface ICanvasViewPositionable
{
    /// <summary>
    /// The X (Canvas.Left) position in world/canvas coordinates.
    /// </summary>
    double X { get; set; }

    /// <summary>
    /// The Y (Canvas.Top) position in world/canvas coordinates.
    /// </summary>
    double Y { get; set; }
}
