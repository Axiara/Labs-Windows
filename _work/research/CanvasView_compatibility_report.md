# CanvasView Compatibility Report

## Research Summary

This report documents potential compatibility issues and conflicts for the CanvasView refactoring work.

## Existing Issues/PRs in the Repository

### Issue #212: CanvasView Experiment Tracking
- **Link**: https://github.com/CommunityToolkit/Labs-Windows/issues/212
- **Status**: Open (tracking issue for the experiment)
- **Relevance**: This is the main tracking issue for CanvasView. The issue explicitly mentions:
  > "Needs to be able to handle some scenarios, like sizing Canvas Width/Height to the contents position/size so it can better support being hosted in a ScrollViewer for a Canvas area that can work larger than a screen."
- **Alignment**: Our CanvasViewPanel directly addresses this requirement by computing ContentBounds and reporting extent to parent ScrollViewer.
- **Action**: Reference #212 in PR descriptions when submitting.

### PR #760: Modern Adorners (by michael-hawker)
- **Link**: https://github.com/CommunityToolkit/Labs-Windows/pull/760
- **Status**: Open
- **Relevance**: Mentions "issue with CanvasView and containerization when specifying ContentPresenter within Items property"
- **Potential Conflict**: Our changes modify container handling (PrepareContainerForItemOverride, ClearContainerForItemOverride). May need coordination if both PRs are merged.
- **Action**: Review PR #760 for overlap; coordinate if necessary.

## WinUI 3 XamlReader.Load Limitations

### Issue #2898: Proposal for FrameworkTemplate APIs
- **Link**: https://github.com/microsoft/microsoft-ui-xaml/issues/2898
- **Status**: Open (feature request)
- **Relevance**: The original CanvasView used `XamlReader.Load` to create ItemsPanelTemplate at runtime.
- **Alignment**: Our solution uses compiled XAML (Themes/Generic.xaml) with DefaultStyleKey, which:
  - Avoids XamlReader.Load limitations
  - Works with custom types (CanvasViewPanel) without dummy XAML references
  - Is more performant (no runtime XAML parsing)
- **No action needed**: Our approach is the correct workaround.

### Related Issues (#7206, #4723, #6582)
These confirm that XamlReader.Load is problematic for custom types in WinUI 3. Our compiled XAML approach avoids these issues.

## Uno Platform Compatibility

### Generic.xaml Auto-Discovery Issue
- **Link**: https://github.com/unoplatform/uno/issues/4424
- **Status**: Known limitation
- **Issue**: Uno Platform doesn't automatically discover styles in Themes/Generic.xaml for control libraries on all targets.
- **Our Mitigation**: `EnsureUnoResourcesMerged()` dynamically merges the resource dictionary at runtime when the control is instantiated.
- **Risk Level**: Low - our fallback is the standard workaround.
- **Alternative for Apps**: Apps can explicitly merge the dictionary in App.xaml.

### Control Library Best Practices
- **Link**: https://platform.uno/docs/articles/guides/how-to-create-control-libraries.html
- **Recommendation**: Include `<GenerateLibraryLayout>true</GenerateLibraryLayout>` in csproj.
- **Action**: Verify this is set in toolkit build tooling (likely already handled by ToolkitComponent.SourceProject.props).

## API Surface Considerations

### New Public Types
1. **CanvasViewPanel** (public partial class)
   - Required for XAML instantiation in ItemsPanelTemplate
   - Follows Panel pattern used by other toolkit controls
   - AOT-compatible (partial class with proper WinRT ABI)

2. **ICanvasViewPositionable** (public interface)
   - Optional for consumers
   - Enables trim/AOT-friendly drag updates
   - No breaking changes for existing consumers

3. **CanvasViewDragPositionUpdateMode** (public enum)
   - Auto is the default (preserves existing behavior)
   - Explicit modes for advanced scenarios

### New Public Properties on CanvasView
1. **ContentBounds** (read-only Rect DP)
   - Reports bounding box of all children
   - Useful for ScrollViewer integration and zoom calculations

2. **ContentOffset** (read-only Point DP)
   - Reports offset for negative coordinates
   - Enables proper positioning in ScrollViewer

3. **DragPositionUpdateMode** (read-write property)
   - Controls drag update strategy
   - Default Auto preserves existing reflection-based behavior

## Trimming/AOT Considerations

### Reflection Usage
- `SetBindingExpressionValue` uses reflection (from `FrameworkElementExtensions`)
- Suppressed with `[UnconditionalSuppressMessage]` attribute
- Users targeting trim/AOT should:
  1. Implement `ICanvasViewPositionable` on their view models
  2. Or set `DragPositionUpdateMode = CanvasAttachedProperties`

### WASM Default Behavior
- On `__WASM__`, Auto mode avoids reflection by default
- Falls back to Canvas.SetLeft/SetTop if interface not implemented
- Documented in DragPositionUpdateMode remarks

## Performance Considerations

### InvalidateMeasure Frequency
- Original concern: calling InvalidateMeasure on every PointerMoved could cause performance issues
- Our implementation: CanvasViewPanel only InvalidateMeasure when bounds expand
- InvalidateArrange is always called (required for smooth drag)
- This matches the optimization pattern in the performance commit (d26cb4f6)

### Clip Behavior
- We explicitly set `_itemsHost.Clip = null` in OnApplyTemplate
- This prevents content-anchored clipping issues
- May conflict with templates that intentionally set Clip
- Risk: Low - most scenarios want content to scroll outside initial viewport

## Recommended PR Merge Order

1. **feature/canvasview-panel-extent** (base functionality)
   - Introduces CanvasViewPanel and extent calculation
   - No dependencies

2. **feature/canvasview-drag-rewrite** (drift fix)
   - Depends on panel-extent for TransformToVisual
   - Must merge after #1

3. **feature/canvasview-uno-wasm** (cross-platform)
   - Depends on drag-rewrite for full functionality
   - Must merge after #2

4. **feature/canvasview-sample-tests** (can merge anytime)
   - Independent changes to samples
   - Can merge before or after other branches

## Known Limitations / Future Work

1. **Zoom Transform Detection**: Current implementation assumes CanvasView's visual transform relative to CanvasViewPanel captures zoom. Complex nested transforms may need additional handling.

2. **Negative Coordinate UX**: When items are dragged to negative coordinates, ScrollViewer extent grows but scroll position doesn't auto-adjust. User must scroll manually.

3. **ManipulationMode Property**: The lifted ManipulationMode property is still forwarded but not actively used since we switched to pointer events. Could be cleaned up in future.

## Conclusion

The changes are well-aligned with existing CommunityToolkit patterns and address documented issues. Main risks are:
- Coordination with PR #760 if it modifies container handling
- Ensuring Uno Platform resource merge works across all targets

Recommended testing:
- WinAppSDK desktop
- UWP (if supported)
- Uno WASM
- Uno Android/iOS (if targeted)
