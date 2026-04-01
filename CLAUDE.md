# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CzurWpfDemo — a WPF document scanning application for the CZUR ET24 Pro overhead book scanner. Captures live video via OpenCV (DirectShow/UVC), detects document corners with computer vision, applies perspective warp + image enhancement, and exports multi-page PDFs.

## Build & Run

```bash
dotnet build CzurWpfDemo.csproj
dotnet run --project CzurWpfDemo.csproj
```

Target framework is `net10.0-windows`. Solution file uses the `.slnx` format (VS 2022+). No test projects exist.

## Architecture

Single-window, code-behind app — no MVVM, no ViewModels. All state and logic live in `MainWindow.xaml.cs` (~690 lines).

**Threading:** A background `Task` runs `CaptureLoop()`, grabs frames from `VideoCapture`, converts to `BitmapSource` (frozen), and dispatches to UI via `Dispatcher.InvokeAsync`. Access to `_lastMat` is guarded by `lock (_matLock)`.

**Core pipeline:**
1. **Capture** — `VideoCapture` with DirectShow backend reads frames from the CZUR camera (USB/UVC). Camera index and resolution are hardcoded (index 0, 1920×1080) since the ComboBox selectors were removed.
2. **Corner detection** — `FindDocumentCorners()`: BilateralFilter → Canny → MorphClose → FindContours → ApproxPolyDP. Falls back to `MinAreaRect` if no 4-point polygon found, then to a 10%-margin rectangle if no contour is large enough.
3. **Manual crop mode** — overlay `Polygon` + four draggable `Thumb` handles on a `Canvas` let users adjust corners. After capture, auto-reverts to automatic detection mode.
4. **Capture & enhance** — perspective warp via `GetPerspectiveTransform`/`WarpPerspective`, then `EnhanceDocumentClarity()` (Unsharp Mask + CLAHE on LAB L-channel)
5. **PDF export** — writes each `Mat` as a temp PNG via `Cv2.ImWrite`, loads it with `XImage.FromFile`, draws onto a `PdfSharpCore` page sized to match, then deletes the temp file

**Key state:**
- `_capture` — OpenCV camera handle
- `_scannedPages` (`List<Mat>`) — in-memory page buffer; disposed and cleared on stop or after save
- `_cropPoints[4]` — current document corner coordinates
- `_isManualCropMode` — auto-detect vs. manual drag toggle

## UI Layout (MainWindow.xaml)

Dark-themed 1100×750 window. Two-column layout: left has camera preview with crop overlay inside a `Viewbox > Grid > Canvas` stack, right has status panel (FPS, capture count), thumbnail, and save button. Button styles (`PrimaryButton`, `DangerButton`, `SuccessButton`) defined inline in `Window.Resources`. Camera/resolution ComboBoxes exist in XAML but are commented out.

## Dependencies

| Package | Purpose |
|---|---|
| OpenCvSharp4 (4.9.0) | Camera capture, image processing |
| OpenCvSharp4.runtime.win | Native Windows OpenCV DLLs |
| OpenCvSharp4.Extensions | Mat ↔ Bitmap conversion |
| PdfSharpCore (1.3.67) | Multi-page PDF generation |

## Notes

- Code comments and all UI text are written in **Uzbek**
- The CZUR ET24 Pro must be connected via USB for the camera to work
- `MatToBitmapSource` creates `BitmapSource` at 300 DPI (not 96) for higher-quality rendering
- An older version of `BtnStart_Click` that read from the ComboBoxes is commented out above the current implementation
