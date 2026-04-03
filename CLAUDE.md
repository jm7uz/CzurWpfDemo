# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CzurWpfDemo — a dual-purpose WPF application combining (1) document scanning with the CZUR ET24 Pro overhead scanner and (2) contract/document management via a remote API. Uses OpenCV for live camera capture, computer vision-based corner detection, perspective correction, and PDF export. Includes authentication, role-based routing, contract search, and document status tracking.

## Build & Run

```bash
dotnet build CzurWpfDemo.csproj
dotnet run --project CzurWpfDemo.csproj
```

Target framework is `net10.0-windows`. Solution file uses the `.slnx` format (VS 2022+). No test projects exist.

## Architecture

### Application Flow

The application starts at `LoginWindow` (Views/LoginWindow.xaml.cs), authenticates via `AuthService`, and routes users based on role:
- **Superadmin** → `ReportWindow` (contract reporting/search interface)
- **Regular users** → `MainWindow` (CZUR scanner interface in standalone mode)

From `ReportWindow`, users can drill down:
- Select user → `ContractDetailsWindow` (list of contracts with search/date filters and pagination)
- Double-click contract → `MainWindow` (scanner with contract context, displays collapsible sidebar with document type cards)

### Core Modules

**1. Scanner Module (MainWindow.xaml.cs)**
Single-window, code-behind design. No MVVM. All state and logic in `MainWindow.xaml.cs`. Supports two modes:
- **Standalone mode** (no contract) — traditional scanner with local PDF export
- **Contract mode** (with ContractItem) — displays sidebar with document types, uploads each scan directly to API

**Threading:** Background `Task` runs `CaptureLoop()`, grabs frames from `VideoCapture` (DirectShow/UVC), converts to frozen `BitmapSource`, dispatches to UI via `Dispatcher.InvokeAsync`. Access to `_lastMat` is guarded by `lock (_matLock)`.

**Scanning pipeline:**
1. **Capture** — `VideoCapture` with DirectShow backend reads frames from CZUR camera (USB/UVC). Camera index and resolution are hardcoded (index 0, 1920×1080) since ComboBox selectors were removed.
2. **Corner detection** — `FindDocumentCorners()`: BilateralFilter → Canny → MorphClose → FindContours → ApproxPolyDP. Falls back to `MinAreaRect` if no 4-point polygon found, then to a 10%-margin rectangle if no contour is large enough.
3. **Manual crop mode** — overlay `Polygon` + four draggable `Thumb` handles on a `Canvas` let users adjust corners. After capture, auto-reverts to automatic detection mode.
4. **Capture & enhance** — perspective warp via `GetPerspectiveTransform`/`WarpPerspective`, then `EnhanceDocumentClarity()` (Unsharp Mask + CLAHE on LAB L-channel).
5. **Upload or store:**
   - **Contract mode**: encodes Mat as PNG bytes, uploads via `ContractDocumentService.UploadDocumentAsync()` with document_number + contract_document_id
   - **Standalone mode**: stores Mat in `_scannedPages` for later PDF export via `BtnSave_Click`

**Key state:**
- `_contract` — optional ContractItem (if null, standalone mode)
- `_selectedDocumentType` — active document type selected from sidebar (contract mode only)
- `_allDocumentTypes` — list of all document types loaded via API
- `_capture` — OpenCV camera handle
- `_scannedPages` (`List<Mat>`) — in-memory page buffer (standalone mode only)
- `_cropPoints[4]` — current document corner coordinates
- `_isManualCropMode` — auto-detect vs. manual drag toggle

**Sidebar (contract mode only):**
- `SidebarPanel` — collapsible left panel with document type cards (200px width)
- `BtnToggleSidebar` — toggle button (▶/◀) to show/hide sidebar
- Cards are color-coded per document type, turn green after successful upload
- Clicking a card selects it as active document type for next scan

**Branch search & filter (contract mode only):**
- `TxtBranchSearch` — TextBox in top-right header for live search (180px width)
- `CmbBranch` — ComboBox in top-right header (200px width) populated with all branches via `BranchService.GetAllBranchesAsync()`
- Search filters by branch name, state name, region name, or address
- First option is always "Barcha filiallar" (all branches)
- `TxtBranchSearch_TextChanged` dynamically filters ComboBox items as user types
- Selection stored in `_selectedBranch` (currently for display only, not used in upload logic)

**2. Contract/Document Management (Views/)**

**AuthService** (Services/AuthService.cs) — stateless authentication, stores `Token` and `CurrentUser` as static properties, calls `auth/login` and `auth/me` endpoints.

**ApiService** (Services/ApiService.cs) — shared HTTP client with snake_case JSON serialization, base URL `http://10.100.104.104:9505/api/`, Bearer token header injection via `SetToken()`.

**ContractService** (Services/ContractService.cs) — fetches paginated contract details for a user via `report/details/{userId}`.

**ContractDocumentService** (Services/ContractDocumentService.cs) — retrieves all document types via `contract-document/all`, uploads scanned documents via `contract-document/upload` (multipart form with file, document_number, contract_document_id).

**UploadService** (Services/UploadService.cs) — uploads PDF files to `upload` endpoint with `file` (PDF only) + `branch` name. Returns JSON with uploaded file URL. Includes validation helpers: `IsPdfFile()`, `GetFileSizeMB()`. See UPLOAD_USAGE.md for examples.

**BranchService** (Services/BranchService.cs) — retrieves branches/filials via `branchs?perPage=X&page=Y` with POST body `{search: null}`. Methods: `GetAllAsync()` (paginated), `GetBranchListAsync()` (single page), `GetAllBranchesAsync()` (all pages combined). Returns branch details including name, address, state, region, and constant document details.

**Window hierarchy:**
- `LoginWindow` → authenticates, routes by role
- `ReportWindow` → user selection grid (superadmin only)
- `ContractDetailsWindow` → DataGrid of contracts with search, date range filters, pagination
- `MainWindow` (contract mode) → scanner window with sidebar showing document type cards; double-clicked from ContractDetailsWindow

### UI Layout

**MainWindow.xaml** — Dark-themed 1100×750 window. Three-column layout:
- **Left** (200px, collapsible): Sidebar with document type cards (visible only in contract mode)
- **Center**: Camera preview with crop overlay inside `Viewbox > Grid > Canvas` stack
- **Right** (280px): Status panel (FPS, capture count), thumbnail, save button

Header shows contract info (document number, client name, phone) and branch filter ComboBox when in contract mode.

Button styles (`PrimaryButton`, `DangerButton`, `SuccessButton`) defined inline in `Window.Resources`. Camera/resolution ComboBoxes are commented out in code but referenced in comments.

All other windows follow similar dark theme with card-based layouts, inline button styles, and Uzbek UI text.

## Dependencies

| Package | Purpose |
|---|---|
| OpenCvSharp4 (4.9.0) | Camera capture, image processing |
| OpenCvSharp4.runtime.win | Native Windows OpenCV DLLs |
| OpenCvSharp4.Extensions | Mat ↔ Bitmap conversion |
| PdfSharpCore (1.3.67) | Multi-page PDF generation |

No additional HTTP client libraries — uses built-in `System.Net.Http.HttpClient`.

## Notes

- **All code comments and UI text are in Uzbek**
- The CZUR ET24 Pro must be connected via USB for the camera to work
- `MatToBitmapSource` creates `BitmapSource` at 300 DPI (not 96) for higher-quality rendering
- An older version of `BtnStart_Click` that read from ComboBoxes is commented out above the current implementation
- API base URL is hardcoded in `ApiService.cs` — change for different environments
- No validation/error handling for missing API responses in some places
- Models use `Resoult` (typo) instead of `Result` to match API response structure
