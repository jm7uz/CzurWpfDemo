using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ContractDetailsPage : UserControl
{
    private readonly int _userId;
    private readonly string _userName;
    private int _currentPage = 1;
    private int _lastPage = 1;
    private bool _initialized = false;

    public ContractDetailsPage(int userId, string userName)
    {
        InitializeComponent();
        _userId = userId;
        _userName = userName;

        TxtTitle.Text = $"{userName} — Shartnomalar";
        //TxtSubtitle.Text = $"Foydalanuvchi ID: {userId}";

        Loaded += ContractDetailsPage_Loaded;
    }

    private async void ContractDetailsPage_Loaded(object sender, RoutedEventArgs e)
    {
        BtnBack.Visibility = AppShell.Current?.CanGoBack == true
            ? Visibility.Visible : Visibility.Collapsed;

        // Birinchi yuklanishda default sanalar, keyingi safar (orqaga qaytganda) eslab qoladi
        if (!_initialized)
        {
            _initialized = true;
            DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
            DpTo.SelectedDate   = DateTime.Now;
        }

        await LoadDataAsync();
    }

    // AppShell tomonidan qaytilganda chaqiriladi
    public Task RefreshAsync() => LoadDataAsync();

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        await LoadDataAsync();
    }

    private async void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "";
        DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
        DpTo.SelectedDate = DateTime.Now;
        _currentPage = 1;
        await LoadDataAsync();
    }

    private async void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            await LoadDataAsync();
        }
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _lastPage)
        {
            _currentPage++;
            await LoadDataAsync();
        }
    }

    private async Task LoadDataAsync()
    {
        TxtStatus.Text = "Yuklanmoqda...";
        DgContracts.ItemsSource = null;

        try
        {
            string? search = string.IsNullOrWhiteSpace(TxtSearch.Text) ? null : TxtSearch.Text.Trim();
            string? dateFrom = DpFrom.SelectedDate?.ToString("dd-MM-yyyy");
            string? dateTo = DpTo.SelectedDate?.ToString("dd-MM-yyyy");

            var response = await ContractService.GetDetailsAsync(_userId, search, dateFrom, dateTo, _currentPage);

            if (response is { Status: true, Resoult: not null })
            {
                var data = response.Resoult.Data;
                var meta = response.Resoult.Meta;

                DgContracts.ItemsSource = data;

                if (meta != null)
                {
                    _lastPage = meta.LastPage;
                    TxtPageInfo.Text = $"Sahifa {meta.CurrentPage}/{meta.LastPage} (Jami: {meta.Total})";
                    BtnPrev.IsEnabled = meta.CurrentPage > 1;
                    BtnNext.IsEnabled = meta.CurrentPage < meta.LastPage;
                }

                TxtStatus.Text = "Tayyor";
            }
            else
            {
                TxtStatus.Text = "Ma'lumot topilmadi";
                TxtPageInfo.Text = "";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xatolik: {ex.Message}";
            TxtPageInfo.Text = "";
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        AppShell.Current?.GoBack();
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        AppShell.Current?.Navigate(new BarcodeScanPage());
    }

    private void DgContracts_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = ((_currentPage - 1) * 30 + e.Row.GetIndex() + 1).ToString();
    }

    private void DgContracts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Yuklash tugmasiga double-click bo'lsa navigatsiya qilmasin
        if (e.OriginalSource is FrameworkElement fe && HasButtonAncestor(fe)) return;

        if (DgContracts.SelectedItem is ContractItem item)
            AppShell.Current?.Navigate(new ScannerPage(item));
    }

    // ─── Barcha shartnomalarni yuklash ───────────────────────────────────────

    private async void BtnDownloadAll_Click(object sender, RoutedEventArgs e)
    {
        BtnDownloadAll.IsEnabled  = false;
        PbDownloadAll.Visibility  = Visibility.Visible;
        try
        {
            string? search   = string.IsNullOrWhiteSpace(TxtSearch.Text) ? null : TxtSearch.Text.Trim();
            string? dateFrom = DpFrom.SelectedDate?.ToString("dd-MM-yyyy");
            string? dateTo   = DpTo.SelectedDate?.ToString("dd-MM-yyyy");

            // Barcha sahifalarni yuklash (100 ta sahifada)
            TxtStatus.Text = "Shartnomalar ro'yxati olinmoqda...";
            var first = await ContractService.GetDetailsAsync(_userId, search, dateFrom, dateTo, 1, 100);
            if (first?.Resoult?.Data == null) { TxtStatus.Text = "Ma'lumot topilmadi"; return; }

            var allContracts = new List<ContractItem>(first.Resoult.Data);
            int totalPages = first.Resoult.Meta?.LastPage ?? 1;

            for (int pg = 2; pg <= totalPages; pg++)
            {
                var pageData = await ContractService.GetDetailsAsync(_userId, search, dateFrom, dateTo, pg, 100);
                if (pageData?.Resoult?.Data != null)
                    allContracts.AddRange(pageData.Resoult.Data);
            }

            int total     = allContracts.Count;
            int totalSaved = 0;

            for (int i = 0; i < allContracts.Count; i++)
            {
                var item = allContracts[i];
                TxtStatus.Text = $"Yuklanmoqda {i + 1}/{total}: {item.DocumentNumber}";

                try
                {
                    var resp = await GetContractService.SearchAllAsync(item.DocumentNumber);
                    var full = resp?.Resoult?.Data?
                        .FirstOrDefault(c => c.DocumentNumber == item.DocumentNumber);

                    var entries = new List<ContractDetailEntry>();
                    if (full?.ConstantDetails?.Count > 0) entries.AddRange(full.ConstantDetails);
                    if (full?.Details?.Count > 0)         entries.AddRange(full.Details);

                    var pdfEntries = entries.Where(en => !string.IsNullOrWhiteSpace(en.File)).ToList();
                    if (pdfEntries.Count == 0) continue;

                    var folderPath = Path.Combine(@"C:\shartnomalar",
                        SanitizeName($"{item.DocumentNumber}_{item.Name}"));
                    if (Directory.Exists(folderPath))
                        Directory.Delete(folderPath, recursive: true);
                    Directory.CreateDirectory(folderPath);

                    foreach (var entry in pdfEntries)
                    {
                        var bytes = await ApiService.DownloadFileAsync(entry.File!);
                        if (bytes == null || bytes.Length == 0) continue;

                        var pdfName  = SanitizeName(string.IsNullOrWhiteSpace(entry.PunktName) ? "hujjat" : entry.PunktName);
                        var filePath = Path.Combine(folderPath, pdfName + ".pdf");
                        await File.WriteAllBytesAsync(filePath, bytes);
                        totalSaved++;
                    }
                }
                catch { /* Bu shartnomani o'tkazib yuborish */ }
            }

            TxtStatus.Text = $"✓ {total} ta shartnomadan {totalSaved} ta PDF saqlandi → C:\\shartnomalar";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xatolik: {ex.Message}";
        }
        finally
        {
            BtnDownloadAll.IsEnabled = true;
            PbDownloadAll.Visibility = Visibility.Collapsed;
        }
    }

    // ─── Yuklash tugmasi ──────────────────────────────────────────────────────

    private void BtnDownload_PreviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ContractItem item) return;

        btn.IsEnabled = false;
        SetRowLoadingState(btn, loading: true);
        TxtStatus.Text = "PDFlar yuklanmoqda...";

        try
        {
            // To'liq shartnoma ma'lumotlarini olish (ConstantDetails bilan)
            var response = await GetContractService.SearchAllAsync(item.DocumentNumber);
            var fullItem = response?.Resoult?.Data?
                .FirstOrDefault(c => c.DocumentNumber == item.DocumentNumber);

            var entries = new List<ContractDetailEntry>();
            if (fullItem?.ConstantDetails?.Count > 0) entries.AddRange(fullItem.ConstantDetails);
            if (fullItem?.Details?.Count > 0)         entries.AddRange(fullItem.Details);

            var pdfEntries = entries.Where(en => !string.IsNullOrWhiteSpace(en.File)).ToList();

            if (pdfEntries.Count == 0)
            {
                TxtStatus.Text = "Yuklash uchun PDF topilmadi";
                return;
            }

            // C:\shartnomalar\{DocumentNumber}_{Name}\
            var folderName = SanitizeName($"{item.DocumentNumber}_{item.Name}");
            var folderPath = Path.Combine(@"C:\shartnomalar", folderName);
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
            Directory.CreateDirectory(folderPath);

            int saved = 0;
            foreach (var entry in pdfEntries)
            {
                var bytes = await ApiService.DownloadFileAsync(entry.File!);
                if (bytes == null || bytes.Length == 0) continue;

                var pdfName = SanitizeName(
                    string.IsNullOrWhiteSpace(entry.PunktName) ? "hujjat" : entry.PunktName);
                var filePath = Path.Combine(folderPath, pdfName + ".pdf");
                await File.WriteAllBytesAsync(filePath, bytes);
                saved++;
            }

            TxtStatus.Text = saved > 0
                ? $"✓ {saved} ta PDF saqlandi → {folderPath}"
                : "Hech qanday fayl saqlanmadi";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xatolik: {ex.Message}";
        }
        finally
        {
            SetRowLoadingState(btn, loading: false);
            btn.IsEnabled = true;
        }
    }

    // ─── Yordamchi metodlar ───────────────────────────────────────────────────

    // Button ichidagi Grid dan arrow va progressbar ni topib holatini almashtiradi
    private static void SetRowLoadingState(Button btn, bool loading)
    {
        if (btn.Content is not Grid grid) return;
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb)
                tb.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
            else if (child is ProgressBar pb)
                pb.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "fayl" : result;
    }

    private static string GetUniquePath(string folder, string baseName)
    {
        var path = Path.Combine(folder, baseName + ".pdf");
        if (!File.Exists(path)) return path;

        int i = 1;
        while (File.Exists(Path.Combine(folder, $"{baseName}_{i}.pdf")))
            i++;

        return Path.Combine(folder, $"{baseName}_{i}.pdf");
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is Button) return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}
