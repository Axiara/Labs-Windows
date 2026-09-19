# Implementation plan

## Problem statement
Separate the existing CanvasView changes into three clean, focused PRs: (1) drag drift fix, (2) ScrollViewer support, and (3) WebAssembly/Uno support, based on diffs against upstream/main.

## Goals and success criteria
- Identify which edits belong to each PR with minimal overlap.
- Note any dependencies between PRs.
- Produce a concrete split strategy (branches/commits) suitable for the CommunityToolkit PR guidance.

## Constraints and assumptions
- No code changes or history rewrites until the user confirms the plan.
- Use upstream/main as the baseline for comparison.
- Keep PRs focused; avoid unrelated changes (e.g., editor settings).

## High-level design / split strategy
- PR 1: Drag drift fix (pointer-based drag with stable delta calculation).
- PR 2: ScrollViewer support (new items host panel, extent calculations, content bounds/offset, and sample).
- PR 3: WebAssembly/Uno support (drag update mode, AOT-safe interface, Uno resource loading, Uno/WASM defines).
- If any changes are currently interleaved, split by editing CanvasView.cs into distinct commits per PR.

## Planned interfaces / surface changes per PR
- PR 1: CanvasView drag input handling in `CanvasView.cs` only (no new public types).
- PR 2: New `CanvasViewPanel` type, `ContentBounds`/`ContentOffset` properties, default style in `Themes/Generic.xaml`, sample update.
- PR 3: New `CanvasViewDragPositionUpdateMode` enum + `ICanvasViewPositionable` interface in `CanvasViewPositioning.cs`, UNO resource merge fallback, UNO/WASM constants, and drag update routing.

## Observed changes vs upstream/main
- Files changed: `src/CanvasView.cs`, `src/CanvasViewPanel.cs` (new), `src/CanvasViewPositioning.cs` (new), `src/Themes/Generic.xaml` (new), `src/CommunityToolkit.WinUI.Controls.CanvasView.csproj`, `samples/CanvasViewDragSample.xaml`, `tests/ExampleCanvasViewTestClass.cs`, `.vscode/settings.json`.
- Current commits are interleaved: drag fix, ScrollViewer support, and WASM/Uno support all touch `src/CanvasView.cs`.

## CanvasView.cs split map and dependencies
- PR 1 (drift fix):
  - Replace `ManipulationDelta` with pointer capture + start-delta drag (PointerPressed/Moved/Released).
  - Keep binding-source updates (`SetBindingExpressionValue`) and avoid new Uno/WASM logic.
- PR 2 (ScrollViewer support):
  - `ContentBounds`/`ContentOffset` properties and `_itemsHost` wiring.
  - `OnApplyTemplate` to locate `CanvasViewPanel`, sync bounds, and remove panel clip.
  - `CanvasViewPanel.SetPositioningElement` call in `TrySetupChildBinding`.
  - Sample change for ScrollViewer and the `CanvasViewPanel` test.
- PR 3 (WASM/Uno support):
  - `DragPositionUpdateMode` property, `CanvasViewDragPositionUpdateMode` + `ICanvasViewPositionable`.
  - Uno resource merge fallback and LayoutUpdated-based binding setup.
  - `__WASM__` fast path to avoid reflection in drag updates.
- Dependency note: PR 3 assumes PR 2 if it references `CanvasViewPanel` (for `SetPositioningElement` and default style). If PR 3 must stand alone, those calls should stay in PR 2 or be guarded.

## Proposed split strategy (high level)
- Create three new topic branches from `upstream/main`.
- Use `git cherry-pick -n` (no-commit) from your existing commits, then stage only the relevant hunks per PR (`git add -p`).
- Keep PRs focused by removing unrelated file changes before committing.

## Changes to exclude or relocate
- Exclude `.vscode/settings.json` from all PRs.
- Keep the sample change (`samples/CanvasViewDragSample.xaml`) and the new test in the ScrollViewer PR only.
- The CanvasViewPanel performance tweak (InvalidateArrange + conditional InvalidateMeasure) should live with ScrollViewer support.
