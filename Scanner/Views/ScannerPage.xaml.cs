using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ScannerPage : UserControl
{
    // ─── Shartnoma ma'lumotlari ──────────────────────────────────
    private readonly ContractItem? _contract;
    private ContractDocumentType? _selectedDocumentType;
    private List<ContractDocumentType>? _allDocumentTypes;
    private List<BranchItem>? _allBranches;
    private BranchItem? _selectedBranch;
    private List<ContractDetailEntry>? _fetchedConstantDetails; // get/all dan yangilangan constant_details
    private List<ContractDetailEntry>? _fetchedDetails;         // get/all dan yangilangan details

    // ─── Kamera holati ───────────────────────────────────────────
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private volatile bool _isRunning;
    private bool _isManualCropMode;
    private int _isStopping; // 0 = free, 1 = stopping (Interlocked uchun)
    private Mat? _lastMat;
    private readonly object _matLock = new();

    // ─── Saqlangan rasmlar ───────────────────────────────────────
    private readonly List<Mat> _scannedPages = new();                       // standalone rejim
    private readonly Dictionary<int, List<Mat>> _cardImages = new();        // contract rejim: card_id → rasmlar
    private readonly Dictionary<int, System.Windows.Shapes.Rectangle> _cardFillRects = new(); // animatsiya uchun fill rect
    private readonly Dictionary<int, Border> _cardBorders = new();          // border referenslari
    private readonly Dictionary<int, TextBlock> _cardCountBadges = new();   // rasm soni badge lari
    private readonly Dictionary<int, Button> _cardViewBtns = new();         // ko'z tugmasi referenslari
    private readonly HashSet<int> _initiallyUploadedCardIds = new();        // dastlab yuklangan card IDlar
    private int _captureCount;
    private readonly System.Windows.Point[] _cropPoints = new System.Windows.Point[4];

    // ─── Scan-line animatsiyasi ──────────────────────────────────
    private double _scanLineMinY, _scanLineMaxY;
    private bool   _scanAnimStarted;

    // ─── FPS hisoblash ───────────────────────────────────────────
    private readonly Stopwatch _fpsStopwatch = new();
    private int _frameCount;
    private double _currentFps;

    // ─── Razresheniyalar ─────────────────────────────────────────
    private readonly (int Width, int Height)[] _resolutions =
    {
        (5696, 4272),   // 24MP ET24 Pro (Maksimal Sifat)
        (4000, 3000),   // 12MP (Yuqori Sifat)
        (3072, 1728),   // CZUR Taqdimot rejimi
        (1920, 1080),   // Standart HD
        (1536, 1152),   // CZUR Skan rejimi
    };

    public ScannerPage(ContractItem? contract = null)
    {
        InitializeComponent();
        _contract = contract;
        Loaded += ScannerPage_Loaded;
        Unloaded += ScannerPage_Unloaded;
    }

    private async void ScannerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_contract != null)
        {
            // Shartnoma rejimi
            TxtContractInfo.Text = $"Shartnoma: {_contract.DocumentNumber} | Mijoz: {_contract.Name} | Tel: {_contract.TelNumber}";
            TxtContractInfo.Visibility = Visibility.Visible;
            BtnBack.Visibility = Visibility.Visible;
            SidebarPanel.Visibility = Visibility.Visible;
            BtnToggleSidebar.Visibility = Visibility.Visible;
            BranchFilterPanel.Visibility = Visibility.Visible;

            await LoadBranchesAsync();
            await LoadDocumentTypesAsync();
            Log($"Shartnoma yuklandi: {_contract.DocumentNumber} - {_contract.Name}");
        }
        else
        {
            // Mustaqil rejim
            BtnLogout.Visibility = Visibility.Visible;
        }

        // Kamerani avtomatik boshlash
        await StartCameraAsync();
    }

    // Kamerani boshlash logikasi (avval BtnStart_Click ichida edi)
    private async Task StartCameraAsync()
    {
        int cameraIndex = 0;
        var (width, height) = _resolutions[3]; // 1920×1080

        SetStatus("Ulanmoqda...", "#F59E0B");
        Log($"Kamera {cameraIndex} ochilmoqda ({width}×{height})...");
        BtnManualCrop.IsEnabled = false;
        BtnCapture.IsEnabled = false;

        bool success = await Task.Run(() => InitCamera(cameraIndex, width, height));

        if (success)
        {
            _isRunning = true;
            _cts = new CancellationTokenSource();

            PlaceholderPanel.Visibility = Visibility.Collapsed;
            CameraImage.Visibility = Visibility.Visible;
            BtnManualCrop.IsEnabled = true;
            _isManualCropMode = false;
            BtnCapture.IsEnabled = true;
            LiveCropCanvas.Visibility = Visibility.Visible;

            SetStatus("Jonli efir", "#10B981");
            Log($"✅ Kamera ulandi. {width}×{height} @ UVC rejimi.");

            _fpsStopwatch.Restart();
            _frameCount = 0;
            foreach (var mat in _scannedPages) mat.Dispose();
            _scannedPages.Clear();
            _captureCount = 0;
            TxtCaptureCount.Text = "0";
            BtnSave.IsEnabled = false;
            CapturedImage.Source = null;

            foreach (var (_, mats) in _cardImages) foreach (var m in mats) m.Dispose();
            _cardImages.Clear();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
        }
        else
        {
            SetStatus("Kamera topilmadi", "#EF4444");
            Log("❌ Kamera ulanmadi. USB ulanganligi va qurilma menejerini tekshiring.");
            PlaceholderPanel.Visibility = Visibility.Visible;
        }
    }

    private void SetCardCountBadge(int cardId, int count)
    {
        if (!_cardCountBadges.TryGetValue(cardId, out var badge)) return;
        if (count > 0)
        {
            badge.Text    = $"📷 {count}";
            badge.Opacity = 1.0;
        }
        else
        {
            badge.Opacity = 0.0;
        }
    }

    private void ScannerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Kamera AppShell tomonidan StopCameraAsync() da to'xtatilgan bo'lishi kerak.
        // Bu yerda faqat manage qilinadigan resurslarni tozalaymiz.
        foreach (var fill in _cardFillRects.Values)
            ((System.Windows.Media.ScaleTransform)fill.RenderTransform)
                .BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        foreach (var (_, mats) in _cardImages)
            foreach (var m in mats) m.Dispose();
        _cardImages.Clear();
        lock (_matLock) { _lastMat?.Dispose(); _lastMat = null; }
    }

    // AppShell navigatsiyadan oldin await bilan chaqiradi
    public async Task StopCameraAsync()
    {
        // Bir vaqtda ikki marta chaqirilishiga yo'l qo'ymaslik
        if (Interlocked.CompareExchange(ref _isStopping, 1, 0) != 0)
            return;

        try
        {
            _isRunning = false;
            _cts?.Cancel();

            if (_captureTask != null)
            {
                try
                {
                    // Maksimum 3 soniya kutamiz — undan keyin o'zimiz tugatamiz
                    await Task.WhenAny(_captureTask, Task.Delay(3000));
                }
                catch { }
                _captureTask = null;
            }

            // Release() va Dispose() IKKALASINI chaqirmang —
            // OpenCvSharp da Dispose() ichida Release() ham chaqiriladi → double-free crash
            var cap = _capture;
            _capture = null;
            try { cap?.Dispose(); } catch { }

            await Dispatcher.InvokeAsync(StopScanLineAnimation);
        }
        finally
        {
            Interlocked.Exchange(ref _isStopping, 0);
        }
    }

    // Tashqi qurilma tugmasi yoki boshqa manbadan rasmga olishni ishga tushirish
    public void TriggerCapture()
    {
        if (BtnCapture.IsEnabled)
            BtnCapture_Click(this, new RoutedEventArgs());
    }

    // Tezkor sinxron to'xtatish (BtnStop_Click da ishlatiladi)
    public void StopCamera()
    {
        _isRunning = false;
        _cts?.Cancel();
        // Capture ni bu yerda dispose qilmang — CaptureLoop hali ishlayotgan bo'lishi mumkin
    }

    // ─── Filiallarni Yuklash ──────────────────────────────────────
    private async Task LoadBranchesAsync()
    {
        try
        {
            _allBranches = await BranchService.GetAllBranchesAsync();
            if (_allBranches?.Count > 0)
                Log($"✅ {_allBranches.Count} ta filial yuklandi");
            else
                Log("⚠ Filiallar yuklanmadi");
        }
        catch (Exception ex) { Log($"❌ Filiallar xatosi: {ex.Message}"); }
    }

    // ─── Dropdown ochish/yopish ───────────────────────────────────
    private void BranchDropdown_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (BranchPopup.IsOpen)
        {
            BranchPopup.IsOpen = false;
            BranchDropdownTrigger.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
            return;
        }
        TxtBranchSearch.Text = "";
        PopulateBranchList(_allBranches ?? new List<BranchItem>());
        BranchDropdownTrigger.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        BranchPopup.IsOpen = true;
        e.Handled = true;
    }

    private void Page_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!BranchPopup.IsOpen) return;
        var source = e.OriginalSource as DependencyObject;
        if (source != null && !IsDescendantOf(BranchDropdownTrigger, source))
        {
            BranchPopup.IsOpen = false;
            BranchDropdownTrigger.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
        }
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject child)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void BranchPopup_Opened(object sender, EventArgs e)
    {
        TxtBranchSearch.Focus();
    }

    // ─── Filtr matnini o'zgartirganda ────────────────────────────
    private void TxtBranchSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allBranches == null) return;
        var q = TxtBranchSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _allBranches
            : _allBranches.Where(b =>
                (b.Name ?? "").ToLower().Contains(q) ||
                (b.StateName ?? "").ToLower().Contains(q) ||
                (b.RegionName ?? "").ToLower().Contains(q) ||
                (b.Address ?? "").ToLower().Contains(q)).ToList();
        PopulateBranchList(filtered);
    }

    // ─── Ro'yxatni to'ldirish ─────────────────────────────────────
    private void PopulateBranchList(List<BranchItem> branches)
    {
        BranchListPanel.Children.Clear();

        // "Barcha filiallar" birinchi element
        BranchListPanel.Children.Add(CreateBranchItem(null));

        foreach (var b in branches)
            BranchListPanel.Children.Add(CreateBranchItem(b));
    }

    private Border CreateBranchItem(BranchItem? branch)
    {
        bool isSel = branch == null ? _selectedBranch == null : branch.Id == _selectedBranch?.Id;

        var normalBg   = new SolidColorBrush(Colors.Transparent);
        var hoverBg    = new SolidColorBrush(Color.FromRgb(38, 42, 62));
        var selectedBg = new SolidColorBrush(Color.FromRgb(29, 78, 216));

        var item = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(10, 7, 10, 7),
            Cursor       = System.Windows.Input.Cursors.Hand,
            Background   = isSel ? selectedBg : normalBg,
            Tag          = branch
        };

        var content = new StackPanel();

        // Asosiy nomi
        content.Children.Add(new TextBlock
        {
            Text       = branch?.Name ?? "Barcha filiallar",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize   = 13,
            Foreground = new SolidColorBrush(isSel
                ? Colors.White
                : Color.FromRgb(229, 231, 235))
        });

        // Qo'shimcha: viloyat • shahar
        if (branch != null && (!string.IsNullOrEmpty(branch.RegionName) || !string.IsNullOrEmpty(branch.StateName)))
        {
            content.Children.Add(new TextBlock
            {
                Text       = $"{branch.RegionName ?? ""}  •  {branch.StateName ?? ""}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 11,
                Foreground = new SolidColorBrush(isSel
                    ? Color.FromArgb(180, 255, 255, 255)
                    : Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        item.Child = content;

        item.MouseEnter += (s, _) =>
            item.Background = isSel ? selectedBg : hoverBg;
        item.MouseLeave += (s, _) =>
            item.Background = isSel ? selectedBg : normalBg;

        item.MouseLeftButtonDown += (s, _) => SelectBranch(branch);
        return item;
    }

    private void SelectBranch(BranchItem? branch)
    {
        _selectedBranch = branch;

        TxtSelectedBranch.Text = branch?.Name ?? "Barcha filiallar";
        TxtSelectedBranch.Foreground = new SolidColorBrush(branch != null
            ? Color.FromRgb(243, 244, 246)
            : Color.FromRgb(156, 163, 175));

        // Trigger chegarasini qaytarish
        BranchDropdownTrigger.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));

        BranchPopup.IsOpen = false;
        Log(branch != null ? $"📍 Filial tanlandi: {branch.Name}" : "📍 Barcha filiallar");
    }

    // ─── Hujjat Turlarini Yuklash ─────────────────────────────────
    private async Task LoadDocumentTypesAsync()
    {
        // Loading holati
        SidebarLoadingPanel.Visibility = Visibility.Visible;
        SidebarScroll.Visibility = Visibility.Collapsed;
        TxtSidebarCheck.Visibility = Visibility.Collapsed;

        try
        {
            // Cardlar turi va constant_details ni parallel yuklash
            var docTypesTask = ContractDocumentService.GetAllAsync();
            var allTask = _contract != null
                ? GetContractService.SearchAllAsync(_contract.DocumentNumber)
                : Task.FromResult<ContractDetailsResponse?>(null);

            await Task.WhenAll(docTypesTask, allTask);

            _allDocumentTypes = await docTypesTask;
            var firstContract = (await allTask)?.Resoult?.Data?.FirstOrDefault();
            _fetchedConstantDetails = firstContract?.ConstantDetails;
            _fetchedDetails         = firstContract?.Details;

            if (_allDocumentTypes?.Count > 0)
            {
                PopulateSidebarCards();
                SidebarLoadingPanel.Visibility = Visibility.Collapsed;
                SidebarScroll.Visibility = Visibility.Visible;
                TxtSidebarCheck.Visibility = Visibility.Visible;
                Log($"✅ {_allDocumentTypes.Count} ta hujjat turi yuklandi");
                Log($"✅ constant_details: {_fetchedConstantDetails?.Count ?? 0}, details: {_fetchedDetails?.Count ?? 0}");
            }
            else
            {
                Log("⚠ Hujjat turlari yuklanmadi");
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Hujjat turlari xatosi: {ex.Message}");
        }
    }

    private void PopulateSidebarCards()
    {
        if (_allDocumentTypes == null) return;
        SidebarCardsPanel.Children.Clear();
        _cardFillRects.Clear();
        _cardBorders.Clear();
        _cardCountBadges.Clear();
        _cardViewBtns.Clear();
        _initiallyUploadedCardIds.Clear();

        foreach (var docType in _allDocumentTypes)
            SidebarCardsPanel.Children.Add(CreateDocumentCard(docType));

        // constant_details + details — ikkalasini birlashtirib card holatini belgilaymiz
        // card.Id = ContractDocumentType.Id  ↔  [].contract_document_id
        var allFetched = (_fetchedConstantDetails ?? new List<ContractDetailEntry>())
            .Concat(_fetchedDetails ?? new List<ContractDetailEntry>())
            .ToList();

        if (allFetched.Count > 0)
        {
            var grouped = allFetched.GroupBy(d => d.ContractDocumentId);

            foreach (var g in grouped)
            {
                var cardId = g.Key;
                if (!_cardFillRects.ContainsKey(cardId)) continue;

                _initiallyUploadedCardIds.Add(cardId);
                SetCardState(cardId, "done");

                var totalPhotos = g.Sum(d => int.TryParse(d.PhotoCount, out var pc) ? pc : 0);
                SetCardCountBadge(cardId, totalPhotos);
            }
        }
    }

    private Border CreateDocumentCard(ContractDocumentType docType)
    {
        var accent = ParseColor(docType.Color);

        var card = new Border
        {
            Height          = 68,
            CornerRadius    = new CornerRadius(10),
            Margin          = new Thickness(0, 0, 0, 6),
            Cursor          = System.Windows.Input.Cursors.Hand,
            Background      = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1.5),
            BorderBrush     = new SolidColorBrush(accent),
            Tag             = docType,
            ClipToBounds    = true
        };

        var outer = new Grid();
        outer.SizeChanged += (s, e) =>
        {
            outer.Clip = new RectangleGeometry
            {
                Rect = new System.Windows.Rect(0, 0, outer.ActualWidth, outer.ActualHeight),
                RadiusX = card.CornerRadius.TopLeft,
                RadiusY = card.CornerRadius.TopLeft
            };
        };
        // ─── Fill layer: pastdan yuqoriga ScaleY orqali to'ladi ──────
        // ScaleTransform(1, 0) → ScaleY:0..1, RenderTransformOrigin=(0.5,1) = pastdan o'sadi.
        // Stretch=Fill → border ichki maydoniga to'liq yopishadi, overflow yo'q.
        var fillRect = new System.Windows.Shapes.Rectangle
        {
            Fill                  = new SolidColorBrush(Color.FromArgb(170, accent.R, accent.G, accent.B)),
            VerticalAlignment     = VerticalAlignment.Stretch,
            HorizontalAlignment   = HorizontalAlignment.Stretch,
            RenderTransformOrigin = new System.Windows.Point(0.5, 1.0),
            RenderTransform       = new System.Windows.Media.ScaleTransform(1.0, 0.0),
        };
        outer.Children.Add(fillRect);

        // ─── Rasm soni badge (yuqori o'ng burchak) ───────────────
        var countBadge = new TextBlock
        {
            FontFamily          = new FontFamily("Segoe UI"),
            FontSize            = 10,
            Foreground          = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin              = new Thickness(0, 5, 7, 0),
            Opacity             = 0.0
        };
        outer.Children.Add(countBadge);
        _cardCountBadges[docType.Id] = countBadge;

        // ─── Content layer ───────────────────────────────────────
        var dock = new DockPanel { Margin = new Thickness(14, 0, 10, 0), LastChildFill = true };

        //// O'ng: holat ikonkasi (✓ ✗ ⋯)
        //var iconText = new TextBlock
        //{
        //    Width               = 22,
        //    FontSize            = 17,
        //    VerticalAlignment   = VerticalAlignment.Center,
        //    HorizontalAlignment = HorizontalAlignment.Center,
        //    Text                = "",
        //    Tag                 = "icon"
        //};
        //DockPanel.SetDock(iconText, Dock.Right);
        //dock.Children.Add(iconText);

        // O'ng: ko'z tugmasi (PDF ochish)
        var eyeBtn = new Button
        {
            Content             = "\uE890",   // Segoe MDL2 Assets – View/Eye
            FontFamily          = new FontFamily("Segoe MDL2 Assets"),
            FontSize            = 15,
            Width               = 30,
            Background          = Brushes.Transparent,
            BorderThickness     = new Thickness(0),
            Cursor              = System.Windows.Input.Cursors.Hand,
            Foreground          = Brushes.White,
            Visibility          = Visibility.Collapsed,
            Padding             = new Thickness(0),
            Margin = new Thickness(30, 15, 7, 5),
            VerticalAlignment   = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip             = "PDF ni brauzerda ochish",
            FocusVisualStyle = null
        };
        eyeBtn.MouseEnter += (s, e) =>
        {
            eyeBtn.Background = Brushes.Transparent;
            eyeBtn.Foreground = Brushes.Black;
        };

        eyeBtn.MouseLeave += (s, e) =>
        {
            eyeBtn.Background = Brushes.Transparent;
            eyeBtn.Foreground = Brushes.White;
        };
        eyeBtn.Click += (s, e) => { e.Handled = true; OpenCardPdf(docType.Id); };
        outer.Children.Add(eyeBtn);
        // Chap: hujjat nomi
        var nameText = new TextBlock
        {
            Text              = docType.PunktName,
            FontFamily        = new FontFamily("Segoe UI"),
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = new SolidColorBrush(Colors.White),
            TextWrapping      = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        dock.Children.Add(nameText);

        outer.Children.Add(dock);
        card.Child = outer;

        // Referenslarni saqlash
        _cardFillRects[docType.Id] = fillRect;
        _cardBorders[docType.Id]   = card;
        _cardViewBtns[docType.Id]  = eyeBtn;

        card.MouseLeftButtonDown += (s, e) => SelectDocumentType(docType, card);
        return card;
    }

    private void SelectDocumentType(ContractDocumentType docType, Border card)
    {
        _selectedDocumentType = docType;

        // Barcha kartalar selection qalinligini reset (rang holati o'zgarmaydi)
        foreach (var b in _cardBorders.Values)
            b.BorderThickness = new Thickness(1.5);

        // Tanlangan kartada qalin chegara
        card.BorderThickness = new Thickness(2.5);

        var count = _cardImages.TryGetValue(docType.Id, out var imgs) ? imgs.Count : 0;
        BtnSave.IsEnabled    = count > 0;
        TxtCaptureCount.Text = count.ToString();
        Log($"📋 Tanlandi: {docType.PunktName}");
    }

    private static System.Windows.Media.Color ParseColor(string colorStr)
    {
        try
        {
            var hex = colorStr.Replace("0x", "").Replace("0X", "");
            if (hex.Length == 8)
                return System.Windows.Media.Color.FromArgb(
                    Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16), Convert.ToByte(hex[6..8], 16));
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(28, 31, 46);
    }

    // ─── Sidebar Ochish/Yopish ────────────────────────────────────
    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarPanel.Visibility == Visibility.Visible)
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
            BtnToggleSidebar.Content = "▶";
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Visible;
            BtnToggleSidebar.Content = "◀";
        }
    }

    // ─── Kamerani Boshlash (zaxira — XAML da yashirilgan) ────────────
    private async void BtnStart_Click(object sender, RoutedEventArgs e)
        => await StartCameraAsync();


    // ─── Kamerani To'xtatish ─────────────────────────────────────
    private async void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _isRunning = false;
        _cts?.Cancel();
        if (_captureTask != null) await _captureTask;

        _capture?.Release();
        _capture?.Dispose();
        _capture = null;

        CameraImage.Visibility = Visibility.Collapsed;
        LiveCropCanvas.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Visible;
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        BtnManualCrop.IsEnabled = false;
        _isManualCropMode = false;
        BtnManualCrop.Content = "Qo'lda tahrirlash";
        BtnCapture.IsEnabled = false;
        TxtFps.Text = "—";
        SetStatus("To'xtatildi", "#9CA3AF");
        foreach (var mat in _scannedPages) mat.Dispose();
        _scannedPages.Clear();
        _captureCount = 0;
        TxtCaptureCount.Text = "0";
        CapturedImage.Source = null;
        BtnSave.IsEnabled = false;
        Log("⏹ Kamera to'xtatildi.");
    }

    // ─── Kamera inizializatsiyasi ─────────────────────────────────
    private bool InitCamera(int index, int width, int height)
    {
        try
        {
            _capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened()) return false;
            _capture.Set(VideoCaptureProperties.FrameWidth, width);
            _capture.Set(VideoCaptureProperties.FrameHeight, height);
            _capture.Set(VideoCaptureProperties.Fps, width >= 4000 ? 2 : 20);
            _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            _capture.BufferSize = 1;
            return _capture.IsOpened();
        }
        catch { return false; }
    }

    // ─── Asosiy Kadr Olish Loop ───────────────────────────────────
    private void CaptureLoop(CancellationToken token)
    {
        using var mat = new Mat();
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                // _capture ni lokal o'zgaruvchiga olamiz — dispose race ni oldini olish uchun
                var cap = _capture;
                if (cap == null || !cap.Read(mat) || mat.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }

                lock (_matLock)
                {
                    _lastMat?.Dispose();
                    _lastMat = mat.Clone();
                }

                _frameCount++;
                if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    _currentFps = _frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
                }

                var bitmapSource = MatToBitmapSource(mat);
                bitmapSource.Freeze();

                // mat.Width/Height ni dispatcher lambdasiga kirguzmay oldindan nusxalaymiz
                // (lambda ishlayotganda mat keyingi frame bilan almashgan bo'lishi mumkin)
                var frameWidth  = mat.Width;
                var frameHeight = mat.Height;

                System.Windows.Point[]? liveCorners = null;
                if (!_isManualCropMode)
                {
                    var corners = FindDocumentCorners(mat);
                    liveCorners = corners.Select(c => new System.Windows.Point(c.X, c.Y)).ToArray();
                }

                Dispatcher.InvokeAsync(() =>
                {
                    CameraImage.Source = bitmapSource;
                    if (frameWidth > 0 && frameHeight > 0)
                    {
                        LiveCropCanvas.Width  = frameWidth;
                        LiveCropCanvas.Height = frameHeight;
                        CameraGrid.Width      = frameWidth;
                        CameraGrid.Height     = frameHeight;
                    }
                    if (liveCorners != null)
                    {
                        for (int i = 0; i < 4; i++) _cropPoints[i] = liveCorners[i];
                        UpdatePolygon();
                    }
                    TxtFps.Text = $"{_currentFps:F1} fps";
                });
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Dispatcher.InvokeAsync(() => Log($"⚠ Kadr xatosi: {ex.Message}"));
                Thread.Sleep(100);
            }
        }
    }

    // ─── Rasm Olish ───────────────────────────────────────────────
    private async void BtnCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_contract != null && _selectedDocumentType == null)
        {
            MessageBox.Show("Iltimos, sidebar dan hujjat turini tanlang!",
                "Hujjat turi tanlanmagan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mat captureMat;
        lock (_matLock)
        {
            if (_lastMat == null || _lastMat.Empty()) return;
            captureMat = _lastMat.Clone();
        }

        var srcPts = new[]
        {
            new Point2f((float)_cropPoints[0].X, (float)_cropPoints[0].Y),
            new Point2f((float)_cropPoints[1].X, (float)_cropPoints[1].Y),
            new Point2f((float)_cropPoints[2].X, (float)_cropPoints[2].Y),
            new Point2f((float)_cropPoints[3].X, (float)_cropPoints[3].Y)
        };

        double wTop = Math.Sqrt(Math.Pow(srcPts[1].X - srcPts[0].X, 2) + Math.Pow(srcPts[1].Y - srcPts[0].Y, 2));
        double wBot = Math.Sqrt(Math.Pow(srcPts[2].X - srcPts[3].X, 2) + Math.Pow(srcPts[2].Y - srcPts[3].Y, 2));
        int maxW = Math.Max((int)wTop, (int)wBot);
        double hLeft = Math.Sqrt(Math.Pow(srcPts[3].X - srcPts[0].X, 2) + Math.Pow(srcPts[3].Y - srcPts[0].Y, 2));
        double hRight = Math.Sqrt(Math.Pow(srcPts[2].X - srcPts[1].X, 2) + Math.Pow(srcPts[2].Y - srcPts[1].Y, 2));
        int maxH = Math.Max((int)hLeft, (int)hRight);

        if (maxW <= 0 || maxH <= 0) { captureMat.Dispose(); return; }

        var dstPts = new[]
        {
            new Point2f(0, 0), new Point2f(maxW - 1, 0),
            new Point2f(maxW - 1, maxH - 1), new Point2f(0, maxH - 1)
        };

        using var transform = Cv2.GetPerspectiveTransform(srcPts, dstPts);
        using var warped = new Mat();
        Cv2.WarpPerspective(captureMat, warped, transform, new OpenCvSharp.Size(maxW, maxH));

        using var enhanced = EnhanceDocumentClarity(warped);

        if (_contract != null)
        {
            // Contract rejim: rasmni localda card ID bilan saqlash
            var cardId = _selectedDocumentType!.Id;
            if (!_cardImages.ContainsKey(cardId))
                _cardImages[cardId] = new List<Mat>();
            _cardImages[cardId].Add(enhanced.Clone());

            _captureCount++;
            var cardCount = _cardImages[cardId].Count;
            TxtCaptureCount.Text = cardCount.ToString();
            BtnSave.IsEnabled = true;
            Log($"📸 {_selectedDocumentType.PunktName}: {cardCount}-rasm saqlandi (local).");
        }
        else
        {
            // Standalone rejim
            _scannedPages.Add(enhanced.Clone());
            _captureCount++;
            TxtCaptureCount.Text = _captureCount.ToString();
            BtnSave.IsEnabled = true;
            Log($"📸 Skan #{_captureCount} xotiraga qo'shildi.");
        }

        var bmp = MatToBitmapSource(enhanced);
        bmp.Freeze();
        CapturedImage.Source = bmp;
        TxtCaptureCount.Text = _captureCount.ToString();
        captureMat.Dispose();

        if (_isManualCropMode) BtnManualCrop_Click(this, new RoutedEventArgs());
    }

    // ─── Manual Crop ─────────────────────────────────────────────
    private void BtnManualCrop_Click(object sender, RoutedEventArgs e)
    {
        if (!_isManualCropMode)
        {
            _isManualCropMode = true;
            BtnManualCrop.Content = "Avtomatik topish";
            BtnManualCrop.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(245, 158, 11));

            ThumbTL.Visibility = ThumbTR.Visibility = ThumbBR.Visibility = ThumbBL.Visibility = Visibility.Visible;
            SetThumbPosition(ThumbTL, 0); SetThumbPosition(ThumbTR, 1);
            SetThumbPosition(ThumbBR, 2); SetThumbPosition(ThumbBL, 3);
            Log("✋ Qo'lda tahrirlash yoqildi.");
        }
        else
        {
            _isManualCropMode = false;
            BtnManualCrop.Content = "Qo'lda tahrirlash";
            BtnManualCrop.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(139, 92, 246));
            ThumbTL.Visibility = ThumbTR.Visibility = ThumbBR.Visibility = ThumbBL.Visibility = Visibility.Collapsed;
            Log("🤖 Avto rejim yoqildi.");
        }
    }

    private void SetThumbPosition(Thumb thumb, int index)
    {
        Canvas.SetLeft(thumb, _cropPoints[index].X - thumb.Width / 2);
        Canvas.SetTop(thumb, _cropPoints[index].Y - thumb.Height / 2);
    }

    private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb thumb && int.TryParse(thumb.Tag?.ToString(), out int idx))
        {
            double nx = Math.Max(0, Math.Min(_cropPoints[idx].X + e.HorizontalChange, LiveCropCanvas.Width));
            double ny = Math.Max(0, Math.Min(_cropPoints[idx].Y + e.VerticalChange, LiveCropCanvas.Height));
            _cropPoints[idx] = new System.Windows.Point(nx, ny);
            Canvas.SetLeft(thumb, nx - thumb.Width / 2);
            Canvas.SetTop(thumb, ny - thumb.Height / 2);
            UpdatePolygon();
        }
    }

    private void UpdatePolygon()
    {
        LiveCropPolygon.Points.Clear();
        foreach (var p in _cropPoints) LiveCropPolygon.Points.Add(p);
        UpdateScanOverlay();
    }

    // ─── Skanerlash overlay: qorartirish + burchaklar + scan-line ─
    private void UpdateScanOverlay()
    {
        var w = LiveCropCanvas.Width;
        var h = LiveCropCanvas.Height;
        if (w <= 0 || h <= 0) return;

        // 1. Qoraytirilgan overlay: to'liq kadr MINUS hujjat poligoni
        var outer = new RectangleGeometry(new System.Windows.Rect(0, 0, w, h));
        var inner = new PathGeometry();
        var fig   = new PathFigure { IsClosed = true, StartPoint = _cropPoints[0] };
        for (int i = 1; i < 4; i++)
            fig.Segments.Add(new LineSegment(_cropPoints[i], true));
        inner.Figures.Add(fig);
        OverlayDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);

        // 2. Burchak L-belgilari (har birida 28px uzunlikda ikki chiziq)
        const double CL = 28;
        var corners = new PathGeometry();
        AddCornerL(corners, _cropPoints[0], _cropPoints[1], _cropPoints[3], CL); // TL
        AddCornerL(corners, _cropPoints[1], _cropPoints[0], _cropPoints[2], CL); // TR
        AddCornerL(corners, _cropPoints[2], _cropPoints[3], _cropPoints[1], CL); // BR
        AddCornerL(corners, _cropPoints[3], _cropPoints[2], _cropPoints[0], CL); // BL
        CornersPath.Data = corners;

        // 3. Scan-line: poligon bounding-box ichida
        var minX = _cropPoints.Min(p => p.X);
        var maxX = _cropPoints.Max(p => p.X);
        _scanLineMinY = _cropPoints.Min(p => p.Y);
        _scanLineMaxY = _cropPoints.Max(p => p.Y);
        ScanLine.Width = Math.Max(1, maxX - minX);
        Canvas.SetLeft(ScanLine, minX);

        if (!_scanAnimStarted && _isRunning)
            StartScanLineAnimation();
    }

    // L-shakl burchak: corner nuqtasidan neighborA va neighborB tomonga CL uzunlikda
    private static void AddCornerL(PathGeometry pg,
        System.Windows.Point corner,
        System.Windows.Point neighborA,
        System.Windows.Point neighborB,
        double len)
    {
        var dA = NormalizeVec(neighborA - corner);
        var dB = NormalizeVec(neighborB - corner);
        var ptA = new System.Windows.Point(corner.X + dA.X * len, corner.Y + dA.Y * len);
        var ptB = new System.Windows.Point(corner.X + dB.X * len, corner.Y + dB.Y * len);

        var figure = new PathFigure { IsClosed = false, StartPoint = ptA };
        figure.Segments.Add(new LineSegment(corner, true));
        figure.Segments.Add(new LineSegment(ptB,    true));
        pg.Figures.Add(figure);
    }

    private static System.Windows.Vector NormalizeVec(System.Windows.Vector v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return len > 0 ? new System.Windows.Vector(v.X / len, v.Y / len)
                       : new System.Windows.Vector(1, 0);
    }

    private void StartScanLineAnimation()
    {
        _scanAnimStarted = true;
        ScanLine.Opacity  = 1;
        var anim = new DoubleAnimation
        {
            From           = _scanLineMinY,
            To             = _scanLineMaxY,
            Duration       = TimeSpan.FromSeconds(2.2),
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        ScanLine.BeginAnimation(Canvas.TopProperty, anim);
    }

    private void StopScanLineAnimation()
    {
        _scanAnimStarted = false;
        ScanLine.BeginAnimation(Canvas.TopProperty, null);
        ScanLine.Opacity = 0;
    }

    // ─── Tasvir Tiniqligini Oshirish ─────────────────────────────
    private static Mat EnhanceDocumentClarity(Mat src)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(src, blurred, new OpenCvSharp.Size(0, 0), 3);
        var sharpened = new Mat();
        Cv2.AddWeighted(src, 1.5, blurred, -0.5, 0, sharpened);

        using var lab = new Mat();
        Cv2.CvtColor(sharpened, lab, ColorConversionCodes.BGR2Lab);
        var labChannels = Cv2.Split(lab);
        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
        clahe.Apply(labChannels[0], labChannels[0]);
        Cv2.Merge(labChannels, lab);
        Cv2.CvtColor(lab, sharpened, ColorConversionCodes.Lab2BGR);
        foreach (var ch in labChannels) ch.Dispose();
        return sharpened;
    }

    // ─── Hujjat Burchaklarini Topish ─────────────────────────────
    private static Point2f[] FindDocumentCorners(Mat img)
    {
        using var gray = new Mat();
        Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.BilateralFilter(gray, blurred, 9, 75, 75);
        using var edged = new Mat();
        Cv2.Canny(blurred, edged, 30, 100);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(15, 15));
        using var closed = new Mat();
        Cv2.MorphologyEx(edged, closed, MorphTypes.Close, kernel);
        Cv2.FindContours(closed, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length > 0)
        {
            var sorted = contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(5);
            foreach (var c in sorted)
            {
                var peri = Cv2.ArcLength(c, true);
                var approx = Cv2.ApproxPolyDP(c, 0.04 * peri, true);
                if (approx.Length == 4 && Cv2.ContourArea(approx) > img.Width * img.Height * 0.1)
                    return OrderPoints(approx.Select(p => new Point2f(p.X, p.Y)).ToArray());
            }
            var largest = sorted.FirstOrDefault();
            if (largest != null && Cv2.ContourArea(largest) > img.Width * img.Height * 0.1)
                return OrderPoints(Cv2.MinAreaRect(largest).Points());
        }

        int mx = (int)(img.Width * 0.1), my = (int)(img.Height * 0.1);
        return new[] {
            new Point2f(mx, my), new Point2f(img.Width - mx, my),
            new Point2f(img.Width - mx, img.Height - my), new Point2f(mx, img.Height - my)
        };
    }

    private static Point2f[] OrderPoints(Point2f[] pts)
    {
        var ordered = new Point2f[4];
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        ordered[0] = pts[Array.IndexOf(sum, sum.Min())];
        ordered[2] = pts[Array.IndexOf(sum, sum.Max())];
        var diff = pts.Select(p => p.Y - p.X).ToArray();
        ordered[1] = pts[Array.IndexOf(diff, diff.Min())];
        ordered[3] = pts[Array.IndexOf(diff, diff.Max())];
        return ordered;
    }

    // ─── PDF Saqlash ─────────────────────────────────────────────
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_contract != null)
        {
            // Contract rejim: tanlangan karta rasmlarini PDF qilib yuklamoq
            if (_selectedDocumentType == null)
            {
                MessageBox.Show("Iltimos, sidebar dan hujjat turini tanlang!",
                    "Karta tanlanmagan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_selectedBranch == null)
            {
                MessageBox.Show("Iltimos, filialni tanlang!",
                    "Filial tanlanmagan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var cardId = _selectedDocumentType.Id;
            if (!_cardImages.TryGetValue(cardId, out var imgs) || imgs.Count == 0)
            {
                MessageBox.Show("Bu karta uchun hali rasm olinmagan!",
                    "Rasm yo'q", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Rasmlarni nusxalab async yuklaymiz (foydalanuvchi boshqa kartalar bilan ishlashi uchun)
            var imagesCopy = imgs.Select(m => m.Clone()).ToList();
            var branchName = _selectedBranch.Name ?? "";
            var docTypeId = _selectedDocumentType.Id;
            Log($"📤 '{_selectedDocumentType.PunktName}' kartasi PDF ga o'girilmoqda ({imgs.Count} rasm)...");
            _ = SaveCardAsync(docTypeId, imagesCopy, branchName);
        }
        else
        {
            // Standalone rejim: faylga saqlash
            if (_scannedPages.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "PDF qilib saqlash",
                Filter = "PDF hujjat|*.pdf",
                FileName = $"CZUR_Document_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".pdf"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                CreatePdf(_scannedPages, dlg.FileName);
                MessageBox.Show($"PDF muvaffaqiyatli saqlandi!\nJami sahifalar: {_scannedPages.Count}",
                    "Saqlandi", MessageBoxButton.OK, MessageBoxImage.Information);
                Log($"💾 PDF saqlandi: {dlg.FileName}");
                foreach (var mat in _scannedPages) mat.Dispose();
                _scannedPages.Clear();
                _captureCount = 0;
                TxtCaptureCount.Text = "0";
                BtnSave.IsEnabled = false;
                CapturedImage.Source = null;
            }
            catch (Exception ex) { Log($"❌ Saqlash xatosi: {ex.Message}"); }
        }
    }

    // ─── Karta async saqlash (PDF → upload → store) ───────────────
    private async Task SaveCardAsync(int cardId, List<Mat> images, string branchName)
    {
        await Dispatcher.InvokeAsync(() => SetCardState(cardId, "loading"));

        string? pdfPath = null;
        try
        {
            // 1. PDF yaratish (temp faylga)
            pdfPath = Path.Combine(Path.GetTempPath(), $"czur_{cardId}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
            await Task.Run(() => CreatePdf(images, pdfPath));

            // 2. PDF ni serverga yuklash (base64 orqali — hajm cheklovi yo'q)
            var uploadResult = await UploadService.UploadPdfBase64Async(pdfPath, _contract!.Id, _contract.Name);
            if (uploadResult?.Success != true || string.IsNullOrEmpty(uploadResult.Resoult?.Url))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SetCardState(cardId, "failed");
                    Log($"❌ PDF yuklashda xatolik: {uploadResult?.Message ?? "server javobi yo'q"}");
                });
                return;
            }

            // 3. To'liq URL ni ishlatish
            var filePath = uploadResult.Resoult.Url;

            // 4. Mavjud yozuv bo'lsa UPDATE, bo'lmasa STORE
            // constant_details va details ikkalasidan qidiramiz, eng oxirgi ID ni olamiz
            bool savedOk = false;
            var existingDetail = _initiallyUploadedCardIds.Contains(cardId)
                ? (_fetchedConstantDetails ?? new List<ContractDetailEntry>())
                    .Concat(_fetchedDetails ?? new List<ContractDetailEntry>())
                    .Where(d => d.ContractDocumentId == cardId)
                    .OrderByDescending(d => d.Id)
                    .FirstOrDefault()
                : null;

            if (existingDetail != null)
            {
                var updateResult = await ConstantDocumentDetailService.UpdateAsync(
                    existingDetail.Id, long.Parse(_contract!.DocumentNumber), cardId, filePath, images.Count);
                savedOk = updateResult?.Status == true && updateResult?.Resoult != null;
                if (!savedOk)
                    await Dispatcher.InvokeAsync(() =>
                        Log($"❌ Update xatosi (id={existingDetail.Id}): status={updateResult?.Status}, resoult={updateResult?.Resoult?.Id.ToString() ?? "null"}"));
            }
            else
            {
                var storeResult = await ConstantDocumentDetailService.StoreAsync(
                    long.Parse(_contract!.DocumentNumber), cardId, filePath, images.Count);
                savedOk = storeResult?.Status == true && storeResult?.Resoult != null;
                if (!savedOk)
                    await Dispatcher.InvokeAsync(() =>
                        Log($"❌ Store xatosi: status={storeResult?.Status}, resoult={(storeResult?.Resoult?.Id.ToString() ?? "null")}, message={storeResult?.Message ?? "server javobi yo'q"}"));
            }

            if (savedOk)
            {
                // Keyingi saqlashda to'g'ri UPDATE bo'lishi uchun ikkalasini yangilash
                try
                {
                    var refreshed = await GetContractService.SearchAllAsync(_contract!.DocumentNumber);
                    var refreshedContract = refreshed?.Resoult?.Data?.FirstOrDefault();
                    _fetchedConstantDetails = refreshedContract?.ConstantDetails;
                    _fetchedDetails         = refreshedContract?.Details;
                }
                catch { /* yangilash bo'lmasa ham davom etamiz */ }

                await Dispatcher.InvokeAsync(() =>
                {
                    SetCardState(cardId, "done");
                    SetCardCountBadge(cardId, images.Count);
                    _initiallyUploadedCardIds.Add(cardId); // keyingi saqlashtda UPDATE ishlatilsin
                    if (_cardImages.TryGetValue(cardId, out var stored))
                    {
                        foreach (var m in stored) m.Dispose();
                        _cardImages.Remove(cardId);
                    }
                    BtnSave.IsEnabled = false;
                    TxtCaptureCount.Text = "0";
                    var action = existingDetail != null ? "Yangilandi" : "Saqlandi";
                    Log($"✅ Karta {action}! (rasm: {images.Count}, filial: {branchName})");
                });
            }
            else
            {
                await Dispatcher.InvokeAsync(() => SetCardState(cardId, "failed"));
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SetCardState(cardId, "failed");
                Log($"❌ Saqlash xatosi: {ex.Message}");
            });
        }
        finally
        {
            foreach (var m in images) m.Dispose();
            if (pdfPath != null && File.Exists(pdfPath))
                try { File.Delete(pdfPath); } catch { }
        }
    }

    // ─── Karta holati + fill animatsiyasi ─────────────────────────
    // state: "loading" | "done" | "failed" | "normal"
    private void SetCardState(int cardId, string state)
    {
        if (!_cardFillRects.TryGetValue(cardId, out var fill)) return;
        if (!_cardBorders.TryGetValue(cardId, out var card)) return;

        var scale = (System.Windows.Media.ScaleTransform)fill.RenderTransform;
        _cardViewBtns.TryGetValue(cardId, out var eyeBtn);

        switch (state)
        {
            case "loading":
            {
                if (eyeBtn != null) eyeBtn.Visibility = Visibility.Collapsed;
                var accent = GetCardAccent(cardId);
                fill.Fill        = new SolidColorBrush(Color.FromArgb(190, accent.R, accent.G, accent.B));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation
                {
                    From           = 0,
                    To             = 0.70,
                    Duration       = TimeSpan.FromMilliseconds(1600),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                SetCardIcon(cardId, "⋯");
                break;
            }
            case "done":
            {
                if (eyeBtn != null) eyeBtn.Visibility = Visibility.Visible;
                fill.Fill        = new SolidColorBrush(Color.FromRgb(36, 185, 129));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation
                {
                    To             = 1.0,
                    Duration       = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                SetCardIcon(cardId, "✓");
                break;
            }
            case "failed":
            {
                if (eyeBtn != null) eyeBtn.Visibility = Visibility.Collapsed;
                fill.Fill        = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation
                {
                    To             = 1.0,
                    Duration       = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                SetCardIcon(cardId, "✗");
                break;
            }
            default: // "normal"
            {
                if (eyeBtn != null) eyeBtn.Visibility = Visibility.Collapsed;
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                scale.ScaleY     = 0;
                var accent       = GetCardAccent(cardId);
                fill.Fill        = new SolidColorBrush(Color.FromArgb(170, accent.R, accent.G, accent.B));
                card.BorderBrush = new SolidColorBrush(accent);
                SetCardIcon(cardId, "");
                break;
            }
        }
    }

    // ─── Ko'z tugmasi: PDF ni brauzerda ochish ────────────────────
    private void OpenCardPdf(int cardId)
    {
        var url = (_fetchedConstantDetails ?? new List<ContractDetailEntry>())
            .Concat(_fetchedDetails ?? new List<ContractDetailEntry>())
            .Where(d => d.ContractDocumentId == cardId)
            .OrderByDescending(d => d.Id)
            .Select(d => d.File)
            .FirstOrDefault(f => !string.IsNullOrEmpty(f));

        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Bu karta uchun PDF fayl manzili topilmadi.",
                "Fayl topilmadi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Brauzerlar: (nom, yo'l)
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var progX86  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var prog64   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var browsers = new[]
        {
            ("Yandex Browser", Path.Combine(localApp, @"Yandex\YandexBrowser\Application\browser.exe")),
            ("Google Chrome",  Path.Combine(localApp, @"Google\Chrome\Application\chrome.exe")),
            ("Google Chrome",  Path.Combine(prog64,   @"Google\Chrome\Application\chrome.exe")),
            ("Microsoft Edge", Path.Combine(progX86,  @"Microsoft\Edge\Application\msedge.exe")),
            ("Microsoft Edge", Path.Combine(prog64,   @"Microsoft\Edge\Application\msedge.exe")),
            ("Mozilla Firefox",Path.Combine(prog64,   @"Mozilla Firefox\firefox.exe")),
            ("Mozilla Firefox",Path.Combine(progX86,  @"Mozilla Firefox\firefox.exe")),
        };

        foreach (var (_, path) in browsers)
        {
            if (!File.Exists(path)) continue;
            try
            {
                Process.Start(new ProcessStartInfo(path, $"\"{url}\"")
                    { UseShellExecute = false });
                return;
            }
            catch { }
        }

        // Hech qaysi brauzer topilmadi
        MessageBox.Show(
            "PDF ochish uchun qurilmangizda brauzer topilmadi.\n\n" +
            "Quyidagilardan birini o'rnating:\n" +
            "  • Yandex Browser\n" +
            "  • Google Chrome\n" +
            "  • Microsoft Edge\n" +
            "  • Mozilla Firefox",
            "Brauzer topilmadi",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private Color GetCardAccent(int cardId)
    {
        var dt = _allDocumentTypes?.Find(d => d.Id == cardId);
        return dt != null ? ParseColor(dt.Color) : Color.FromRgb(59, 130, 246);
    }

    private void SetCardIcon(int cardId, string icon)
    {
        if (!_cardBorders.TryGetValue(cardId, out var card)) return;
        if (card.Child is not Grid g) return;
        foreach (var ch in g.Children)
            if (ch is DockPanel dp)
                foreach (var dch in dp.Children)
                    if (dch is TextBlock tb && tb.Tag?.ToString() == "icon")
                    {
                        tb.Text     = icon;
                        tb.Foreground = new SolidColorBrush(icon switch
                        {
                            "✓"  => Color.FromRgb(16, 185, 129),
                            "✗"  => Color.FromRgb(239, 68, 68),
                            "⋯"  => Color.FromRgb(245, 158, 11),
                            _    => Colors.Transparent
                        });
                    }
    }

    // ─── PDF yaratish yordamchi metodi ───────────────────────────
    private static void CreatePdf(List<Mat> images, string outputPath)
    {
        using var document = new PdfDocument();
        foreach (var mat in images)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                Cv2.ImWrite(tempFile, mat);
                using var xImage = XImage.FromFile(tempFile);
                var page = document.AddPage();
                page.Width  = XUnit.FromPoint(xImage.PointWidth  * (72.0 / xImage.HorizontalResolution));
                page.Height = XUnit.FromPoint(xImage.PointHeight * (72.0 / xImage.VerticalResolution));
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
        document.Save(outputPath);
    }

    // ─── Navigatsiya ─────────────────────────────────────────────
    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        AppShell.Current?.GoBack();
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        loginWindow.Show();
        AppShell.Current?.Close();
    }

    // ─── Yordamchi metodlar ───────────────────────────────────────
    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
        return BitmapSource.Create(rgb.Cols, rgb.Rows, 300, 300,
            System.Windows.Media.PixelFormats.Rgb24, null,
            rgb.Data, rgb.Rows * rgb.Cols * 3, rgb.Cols * 3);
    }

    private void SetStatus(string text, string colorHex)
    {
        TxtStatus.Text = text;
        TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
    }

    private void Log(string message)
    {
        if (TxtLog != null)
            TxtLog.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
