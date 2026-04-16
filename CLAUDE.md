# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CzurWpfDemo — a dual-purpose WPF application combining (1) document scanning with the CZUR ET24 Pro overhead scanner and (2) contract/document management via a remote API. Uses OpenCV for live camera capture, computer vision-based corner detection, perspective correction, and PDF export. Includes authentication, role-based routing, contract search, and document status tracking.

## Build & Run

```bash
dotnet build CzurWpfDemo.csproj
dotnet run --project CzurWpfDemo.csproj
```

To publish a self-contained `.exe`:
```bash
dotnet publish CzurWpfDemo.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish/CzurWpfDemo
```

Target framework is `net10.0-windows`. Solution file uses the `.slnx` format (VS 2022+). No test projects exist.

## Architecture

### Application Flow

`App.xaml` sets `StartupUri="Views/LoginWindow.xaml"`. `LoginWindow` authenticates via `AuthService` and routes by `user.Role`:
- `"superadmin"` → `AppShell` hosting `ReportPage`
- anything else → `AppShell` hosting `ScannerPage` (standalone mode)
- superadmin navigating to a contract's `ScannerPage` enters **admin mode** (`_isAdminMode = true`): starts in PDF view instead of camera, with a toggle button to switch to scan mode

`AppShell` is a single host window (`ContentControl` named `ContentArea`) with a stack-based navigation system:
- `AppShell.Current.Navigate(page)` — pushes current page onto `_navStack`, shows new page; awaits `ScannerPage.StopCameraAsync()` before switching if scanner is active
- `AppShell.Current.GoBack()` — pops the stack; if returning to `ContractDetailsPage`, calls `RefreshAsync()` to reload data

### Page Navigation

All views are `UserControl` pages hosted inside `AppShell`:
- `ReportPage` — user selection grid (superadmin only); double-click → `Navigate(new ContractDetailsPage(userId, userName))`
- `ContractDetailsPage(userId, userName)` — paginated DataGrid of contracts; double-click → `Navigate(new ScannerPage(contractItem))`; "Scan" button → `Navigate(new BarcodeScanPage())`
- `BarcodeScanPage` — live camera barcode scanner; on successful scan calls `GetContractService.ValidateAsync()` then `SearchAllAsync()`, then → `Navigate(new ScannerPage(contract))`; stops camera via `Unloaded` event (not via `AppShell`)
- `ScannerPage` — scanner (replaces old `MainWindow`); works in standalone mode (no arg) or contract mode (with `ContractItem`)

### Core Modules

**1. Scanner Module (Views/ScannerPage.xaml.cs)**
`UserControl`-based, code-behind design. No MVVM. Supports three modes:
- **Standalone mode** (no contract) — traditional scanner with local PDF export
- **Contract mode** (with `ContractItem`, non-admin) — displays sidebar with document types, uploads each scan directly to API
- **Admin mode** (superadmin + contract) — starts with WebView2 PDF viewer showing the selected card's PDF URL; toggle button switches to camera/scan mode and back

**Threading:** Background `Task` runs `CaptureLoop()`, grabs frames from `VideoCapture` (DirectShow/UVC), converts to frozen `BitmapSource`, dispatches to UI via `Dispatcher.InvokeAsync`. Access to `_lastMat` is guarded by `lock (_matLock)`. `StopCameraAsync()` is awaited by `AppShell` before navigation to guarantee the camera task is fully stopped.

**Camera resolutions** (defined in `_resolutions` array, currently hardcoded to 1920×1080):
- 5696×4272 — 24MP max quality (1-2 fps)
- 4000×3000 — 12MP high quality
- 3072×1728 — presentation mode (~12 fps)
- 1920×1080 — standard HD
- 1536×1152 — scan mode (~20 fps)

**Scanning pipeline:**
1. **Capture** — `VideoCapture` with DirectShow backend reads frames from CZUR camera (USB/UVC). Camera index 0, 1920×1080 are hardcoded; change `_resolutions` index and camera index in `BtnStart_Click` to switch.
2. **Corner detection** — `FindDocumentCorners()`: BilateralFilter → Canny → MorphClose → FindContours → ApproxPolyDP. Falls back to `MinAreaRect` if no 4-point polygon found, then to a 10%-margin rectangle if no contour is large enough.
3. **Manual crop mode** — overlay `Polygon` + four draggable `Thumb` handles on a `Canvas`; auto-reverts to automatic detection mode after capture.
4. **Capture & enhance** — perspective warp via `GetPerspectiveTransform`/`WarpPerspective`, then `EnhanceDocumentClarity()` (Unsharp Mask + CLAHE on LAB L-channel).
5. **Upload or store:**
   - **Contract mode**: encodes Mat as PNG bytes, uploads via `ContractDocumentService.UploadDocumentAsync()` with document_number + contract_document_id
   - **Standalone mode**: stores Mat in `_scannedPages` for later PDF export via `BtnSave_Click`

**Key state:**
- `_contract` — optional `ContractItem` (if null, standalone mode)
- `_selectedDocumentType` — active document type selected from sidebar (contract mode only)
- `_capture` — OpenCV camera handle
- `_scannedPages` (`List<Mat>`) — in-memory page buffer (standalone mode only)
- `_cardImages` (`Dictionary<int, List<Mat>>`) — per-card image buffer (contract mode); keyed by `ContractDocumentType.Id`
- `_cardBorders`, `_cardCountBadges`, `_cardViewBtns`, `_cardReloadBtns` — UI element references for sidebar cards, populated during sidebar build
- `_cropPoints[4]` — current document corner coordinates
- `_isManualCropMode` — auto-detect vs. manual drag toggle
- `_isAdminMode` — true when superadmin views a specific contract
- `_isPdfViewMode` / `_pdfViewerReady` — PDF viewer toggle state (admin mode only)

**Sidebar (contract mode only):**
- `SidebarPanel` — collapsible left panel with document type cards (200px width)
- Cards are color-coded per document type, turn green after successful upload
- Clicking a card selects it as active document type for next scan

**2. Services (Services/)**

**AuthService** — stateless; stores `Token` and `CurrentUser` as static properties; calls `auth/login` and `auth/me` endpoints.

**ApiService** — shared `HttpClient` with snake_case JSON serialization (`JsonNamingPolicy.SnakeCaseLower`), base URL `http://10.100.104.104:9505/api/`, Bearer token header via `SetToken()`. **Note:** `GetAsync`/`PostAsync` have no try/catch — only `PostMultipartAsync` overloads do. Callers must handle exceptions.

**ReportService** — `POST report/by/user` with optional search, userId, and date range.

**ContractService** — `GET report/details/{userId}` with pagination and filters. `ContractItem` has `ConstantDetails` and `Details` (`List<ContractDetailEntry>`).

**ContractDocumentService** — `GET contract-document/all` for document types; `POST contract-document/upload` (multipart: file, document_number, contract_document_id).

**GetContractService** — `POST get/contract` (`ValidateAsync`) to confirm a barcode/document_number exists; `POST get/all?perPage=10&page=1` (`SearchAllAsync`) to fetch the full `ContractItem` with `constant_details`. Used exclusively by `BarcodeScanPage`.

**ConstantDocumentDetailService** — `POST constant-document-detail/store` (`StoreAsync`) and `PUT contract-document-detail/update/{id}` (`UpdateAsync`) — both accept documentNumber, contractDocumentId, filePath, photoCount.

**UploadService** — uploads PDF to `upload` endpoint with file + branch name; returns file URL. Includes `IsPdfFile()` and `GetFileSizeMB()` helpers.

**BranchService** — `POST branchs?perPage=X&page=Y` with `{search: null}`. `GetAllBranchesAsync()` fetches all pages combined.

### UI Layout

**AppShell** — 1300×750 host window (dark `#0F1117`), single `ContentControl` that swaps between pages.

**Global styles** in `App.xaml.Resources` (available in all pages): `PrimaryButton`, `DangerButton`, `SuccessButton`, `ActionButton`, `SecondaryButton`, `PaginationButton`, `FilterInput`, `FilterDatePicker`, `CardBorder`, `LabelText`, `ValueText`.

**ScannerPage** — three-column layout:
- **Left** (200px, collapsible): Sidebar with document type cards (contract mode only)
- **Center**: Camera preview with crop overlay inside `Viewbox > Grid > Canvas` stack
- **Right** (280px): Status panel (FPS, capture count), thumbnail, save button

All UI text is in Uzbek.

## Dependencies

| Package | Purpose |
|---|---|
| OpenCvSharp4 (4.9.0) | Camera capture, image processing |
| OpenCvSharp4.runtime.win | Native Windows OpenCV DLLs |
| OpenCvSharp4.Extensions | Mat ↔ Bitmap conversion |
| PdfSharpCore (1.3.67) | Multi-page PDF generation |
| ZXing.Net (0.16.9) | Barcode decoding (EAN-13, Code128, QR, etc.) |
| Microsoft.Web.WebView2 (1.0.3912.50) | In-app PDF viewer (admin mode) |
| Interop.WIA (1.0.0) | WIA scanner interface (referenced but currently unused) |

No additional HTTP client libraries — uses built-in `System.Net.Http.HttpClient`.

## Notes

- **All code comments and UI text are in Uzbek**
- The CZUR ET24 Pro must be connected via USB for the camera to work
- `MatToBitmapSource` creates `BitmapSource` at 300 DPI (not 96) for higher-quality rendering
- API base URL is hardcoded in `ApiService.cs` — change for different environments
- Models use `Resoult` (typo) instead of `Result` to match API response structure
- `AppShell.Navigate()` / `GoBack()` / `OnClosed()` all await `ScannerPage.StopCameraAsync()` before switching — but only check for `ScannerPage`, not `BarcodeScanPage`; `BarcodeScanPage` handles its own teardown via its `Unloaded` event
- Barcode detection in `BarcodeScanPage` runs every 3rd frame off the UI thread; confirmed after 2 consecutive identical reads (`_barcodeHitCount >= 2`); tries normal + 90° + 270° rotations to handle portrait documents
