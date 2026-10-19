# OrbitConvert interaction specification

This document describes the implemented Windows interaction model. It is informed by the supplied
21.45-second, 3840×2160, 60 fps reference video. OrbitConvert reproduces the interaction principles
with original Windows code and visual assets; it does not copy the reference application's branding.

## Video observations

- **00.00–01.50:** a PDF is dragged on the desktop. A four-segment annular menu opens under the
  pointer with DOCX, JPG, PNG, and TXT around a central source preview.
- **01.50–02.50:** moving over JPG highlights the complete wedge before the pointer is released.
- **02.50–03.75:** the wheel disappears, a compact conversion-progress panel appears, and the JPG
  output becomes visible beside the source.
- **03.75–05.50:** dragging the JPG opens a seven-segment contextual wheel: Compress, Metadata,
  Edit, Annotate, Add Background, Crop, and Redact. The center displays the source preview and size.
- **05.50–10.75:** Crop opens as a compact floating editor. The crop rectangle has visible corner and
  edge handles; presets are Free, 1:1, 16:9, 4:3, and 3:4, with numeric dimensions and Reset/Apply.
- **10.75–12.00:** Apply closes the editor and places a new cropped file beside the original.
- **12.00–13.75:** dragging the crop result shows the conversion wheel and then the contextual action
  wheel; Add Background is selected as a wedge.
- **13.75–18.75:** Add Background opens a compact editor with live preview, gradient and solid color
  presets, Padding, Corners, Shadow, and Ratio controls (Auto, 1:1, 16:9, 4:3).
- **18.75–21.45:** Save with Background closes the editor and creates a visibly composited PNG on
  the desktop.

## Primary vertical slice

1. A user selects one or more files in Explorer and invokes **Convert with OrbitConvert**, uses the
   configured shortcut, or drops files on the floating drop target/application.
2. OrbitConvert detects file formats from their signatures, not only their extensions.
3. A DPI-aware radial menu opens around the pointer and is clamped to the monitor work area.
4. Only available conversion routes that can run with default options are displayed.
5. Selecting **JPG** for a PDF starts real PDFium rendering on the background conversion queue.
6. Progress replaces the center of the radial menu without blocking pointer or keyboard input.
7. The output is written beside the original using keep-both naming by default.
8. For a single image result, the success state offers **Edit image** instead of closing immediately.
9. The next radial menu includes contextual **Crop** and **Background** actions.
10. **Crop** opens a compact floating editor. The rectangle can be moved, resized from four handles,
    set numerically, reset, or constrained to 1:1, 16:9, 4:3, or 3:4.
11. **Save & add background** writes a new cropped image and passes that output to the background
    editor. The original is not modified.
12. **Background** provides solid/gradient presets, custom colors, padding, rounded corners, shadow,
    aspect presets, and explicit export dimensions.
13. **Save with background** flattens the result to a new PNG and reveals it in Explorer.

## Radial input behavior

- Hover selects an item and visually enlarges it; click activates it.
- Arrow keys and Tab move selection; Enter/Space activates; Escape dismisses.
- Number keys 1–9 activate the corresponding item.
- Clicking outside closes the menu. A running background conversion continues safely.
- Multi-file selections expose only routes valid for the entire selection.
- Unsupported engines and routes that require additional options do not appear as working choices.

## Window behavior

- Utility editors are borderless, movable, resizable, theme-aware WPF windows.
- Editors are separate from the main window and keep the active image workflow compact.
- Saving is non-destructive. Collisions use numbered names and never overwrite the source.
- Processing is cancellable and occurs off the UI thread.

## Windows platform constraints

Explorer context menu, configurable global shortcut, OLE drop target, and in-app drop are the reliable
entry points. Direct interception of arbitrary Explorer drag operations is not treated as reliable or
universal; the experimental shake/drop-target path is optional and does not replace supported entry
points.

## Reduced motion and accessibility

The radial menu supports keyboard operation and automation names. Animation is short and decorative;
future work should connect it explicitly to the Windows reduced-motion preference for every utility
window transition.
