using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ContractDetailsPage : UserControl
{
    private int? _userId;
    private string _userName;
    private int _currentPage = 1;
    private int _lastPage = 1;
    private bool _initialized = false;
    private bool _isAdmin;

    private List<BranchItem> _allBranches = [];
    private List<UserItem> _allUsers = [];
    private BranchItem? _selectedBranch = null;
    private UserItem? _selectedUser = null;

    private static readonly Color NormalBorderColor   = Color.FromRgb(55, 65, 81);
    private static readonly Color ActiveBorderColor   = Color.FromRgb(37, 99, 235);
    private static readonly Color HoverBg             = Color.FromRgb(38, 42, 62);
    private static readonly Color SelectedBg          = Color.FromRgb(29, 78, 216);

    public ContractDetailsPage(int userId = 0, string userName = "")
    {
        InitializeComponent();
        _userId = userId == 0 ? null : userId;
        _userName = userName;
        var role = AuthService.CurrentUser?.Role;
        _isAdmin = role == "superadmin" || role == "admin";

        TxtTitle.Text = string.IsNullOrEmpty(userName) ? "Shartnomalar" : $"{userName} — Shartnomalar";

        Loaded += ContractDetailsPage_Loaded;
    }

    private async void ContractDetailsPage_Loaded(object sender, RoutedEventArgs e)
    {
        BtnBack.Visibility = AppShell.Current?.CanGoBack == true
            ? Visibility.Visible : Visibility.Collapsed;

        if (_isAdmin)
        {
            BtnScan.Visibility = Visibility.Visible;
            ColDownload.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnDownloadAll.Visibility = Visibility.Visible;
            ColDownload.Visibility = Visibility.Visible;
        }

        UserFilterPanel.Visibility = Visibility.Visible;

        if (!_initialized)
        {
            _initialized = true;
            DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
            DpTo.SelectedDate   = DateTime.Now;

            await Task.WhenAll(LoadBranchesAsync(), LoadUsersAsync());
        }

        await LoadDataAsync();
    }

    // ─── Branch dropdown ─────────────────────────────────────────────

    private async Task LoadBranchesAsync()
    {
        try { _allBranches = await BranchService.GetAllBranchesAsync(); }
        catch { }
    }

    private void BranchDropdown_Click(object sender, MouseButtonEventArgs e)
    {
        if (BranchPopup.IsOpen) { CloseBranchPopup(); return; }
        TxtBranchSearch.Text = "";
        PopulateBranchList(_allBranches);
        BranchDropdownTrigger.BorderBrush = new SolidColorBrush(ActiveBorderColor);
        BranchPopup.IsOpen = true;
        e.Handled = true;
    }

    private void BranchPopup_Opened(object sender, EventArgs e) => TxtBranchSearch.Focus();

    private void TxtBranchSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TxtBranchSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _allBranches
            : _allBranches.Where(b =>
                b.Name.ToLower().Contains(q) ||
                (b.RegionName ?? "").ToLower().Contains(q) ||
                (b.StateName ?? "").ToLower().Contains(q)).ToList();
        PopulateBranchList(filtered);
    }

    private void PopulateBranchList(List<BranchItem> branches)
    {
        BranchListPanel.Children.Clear();
        BranchListPanel.Children.Add(CreateBranchItem(null));
        foreach (var b in branches)
            BranchListPanel.Children.Add(CreateBranchItem(b));
    }

    private Border CreateBranchItem(BranchItem? branch)
    {
        bool isSel = branch == null ? _selectedBranch == null : branch.Id == _selectedBranch?.Id;
        var normalBg   = new SolidColorBrush(Colors.Transparent);
        var hoverBg    = new SolidColorBrush(HoverBg);
        var selectedBg = new SolidColorBrush(SelectedBg);

        var item = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(10, 7, 10, 7),
            Cursor       = Cursors.Hand,
            Background   = isSel ? selectedBg : normalBg,
            Tag          = branch
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text       = branch?.Name ?? "Barchasi",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize   = 13,
            Foreground = new SolidColorBrush(isSel ? Colors.White : Color.FromRgb(229, 231, 235))
        });

        if (branch != null && (!string.IsNullOrEmpty(branch.RegionName) || !string.IsNullOrEmpty(branch.StateName)))
        {
            content.Children.Add(new TextBlock
            {
                Text       = $"{branch.RegionName}  •  {branch.StateName}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 11,
                Foreground = new SolidColorBrush(isSel
                    ? Color.FromArgb(180, 255, 255, 255)
                    : Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        item.Child = content;
        item.MouseEnter += (_, _) => item.Background = isSel ? selectedBg : hoverBg;
        item.MouseLeave += (_, _) => item.Background = isSel ? selectedBg : normalBg;
        item.MouseLeftButtonDown += (_, _) => SelectBranch(branch);
        return item;
    }

    private void SelectBranch(BranchItem? branch)
    {
        _selectedBranch = branch;
        TxtSelectedBranch.Text = branch?.Name ?? "Barchasi";
        TxtSelectedBranch.Foreground = new SolidColorBrush(branch != null
            ? Color.FromRgb(243, 244, 246)
            : Color.FromRgb(156, 163, 175));
        CloseBranchPopup();
        _currentPage = 1;
        _ = LoadDataAsync();
    }

    private void CloseBranchPopup()
    {
        BranchPopup.IsOpen = false;
        BranchDropdownTrigger.BorderBrush = new SolidColorBrush(NormalBorderColor);
    }

    // ─── User dropdown ───────────────────────────────────────────────

    private async Task LoadUsersAsync()
    {
        try { _allUsers = await UserService.GetAllUsersAsync(); }
        catch { }
    }

    private void UserDropdown_Click(object sender, MouseButtonEventArgs e)
    {
        if (UserPopup.IsOpen) { CloseUserPopup(); return; }
        TxtUserSearch.Text = "";
        PopulateUserList(_allUsers);
        UserDropdownTrigger.BorderBrush = new SolidColorBrush(ActiveBorderColor);
        UserPopup.IsOpen = true;
        e.Handled = true;
    }

    private void UserPopup_Opened(object sender, EventArgs e) => TxtUserSearch.Focus();

    private void TxtUserSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TxtUserSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _allUsers
            : _allUsers.Where(u => u.Name.ToLower().Contains(q) ||
                                   u.Phone.ToLower().Contains(q)).ToList();
        PopulateUserList(filtered);
    }

    private void PopulateUserList(List<UserItem> users)
    {
        UserListPanel.Children.Clear();
        UserListPanel.Children.Add(CreateUserItem(null));
        foreach (var u in users)
            UserListPanel.Children.Add(CreateUserItem(u));
    }

    private Border CreateUserItem(UserItem? user)
    {
        bool isSel = user == null ? _selectedUser == null : user.Id == _selectedUser?.Id;
        var normalBg   = new SolidColorBrush(Colors.Transparent);
        var hoverBg    = new SolidColorBrush(HoverBg);
        var selectedBg = new SolidColorBrush(SelectedBg);

        var item = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(10, 7, 10, 7),
            Cursor       = Cursors.Hand,
            Background   = isSel ? selectedBg : normalBg
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text       = user?.Name ?? "Barchasi",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize   = 13,
            Foreground = new SolidColorBrush(isSel ? Colors.White : Color.FromRgb(229, 231, 235))
        });

        if (user != null && !string.IsNullOrEmpty(user.Phone))
        {
            content.Children.Add(new TextBlock
            {
                Text       = user.Phone,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 11,
                Foreground = new SolidColorBrush(isSel
                    ? Color.FromArgb(180, 255, 255, 255)
                    : Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        item.Child = content;
        item.MouseEnter += (_, _) => item.Background = isSel ? selectedBg : hoverBg;
        item.MouseLeave += (_, _) => item.Background = isSel ? selectedBg : normalBg;
        item.MouseLeftButtonDown += (_, _) => SelectUser(user);
        return item;
    }

    private void SelectUser(UserItem? user)
    {
        _selectedUser = user;
        _userId = user?.Id;
        TxtSelectedUser.Text = user?.Name ?? "Barchasi";
        TxtSelectedUser.Foreground = new SolidColorBrush(user != null
            ? Color.FromRgb(243, 244, 246)
            : Color.FromRgb(156, 163, 175));
        TxtTitle.Text = user != null ? $"{user.Name} — Shartnomalar" : "Shartnomalar";
        CloseUserPopup();
        _currentPage = 1;
        _ = LoadDataAsync();
    }

    private void CloseUserPopup()
    {
        UserPopup.IsOpen = false;
        UserDropdownTrigger.BorderBrush = new SolidColorBrush(NormalBorderColor);
    }

    // ─── Popuplarni tashqaridan yopish ───────────────────────────────

    private void Page_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (src == null) return;

        if (BranchPopup.IsOpen &&
            !BranchPopup.IsMouseOver &&
            !IsDescendantOf(BranchDropdownTrigger, src))
            CloseBranchPopup();

        if (UserPopup.IsOpen &&
            !UserPopup.IsMouseOver &&
            !IsDescendantOf(UserDropdownTrigger, src))
            CloseUserPopup();
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject child)
    {
        var cur = child;
        while (cur != null)
        {
            if (cur == parent) return true;
            cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur) as DependencyObject;
        }
        return false;
    }

    // ─── AppShell tomonidan qaytilganda ──────────────────────────────

    public Task RefreshAsync() => LoadDataAsync();

    // ─── Qidiruv / Reset ─────────────────────────────────────────────

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
        SelectBranch(null);
        SelectUser(null);
    }

    // ─── Sahifalash ──────────────────────────────────────────────────

    private async void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1) { _currentPage--; await LoadDataAsync(); }
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _lastPage) { _currentPage++; await LoadDataAsync(); }
    }

    // ─── Ma'lumot yuklash ────────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        TxtStatus.Text = "Yuklanmoqda...";
        DgContracts.ItemsSource = null;

        try
        {
            string? search    = string.IsNullOrWhiteSpace(TxtSearch.Text) ? null : TxtSearch.Text.Trim();
            string? dateFrom  = DpFrom.SelectedDate?.ToString("dd-MM-yyyy");
            string? dateTo    = DpTo.SelectedDate?.ToString("dd-MM-yyyy");
            string? branchGuid = _selectedBranch?.Guid;

            var response = await ContractService.GetDetailsAsync(_userId, search, dateFrom, dateTo, _currentPage, branchGuid);

            if (response is { Status: true, Resoult: not null })
            {
                DgContracts.ItemsSource = response.Resoult.Data;

                if (response.Resoult.Meta is { } meta)
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

    // ─── Yuklash ─────────────────────────────────────────────────────

    private async void BtnDownloadAll_Click(object sender, RoutedEventArgs e)
    {
        if (DgContracts.ItemsSource is not List<ContractItem> items || items.Count == 0)
        {
            TxtStatus.Text = "Yuklanadigan ma'lumot yo'q";
            return;
        }

        BtnDownloadAll.IsEnabled = false;
        int ok = 0, skip = 0;

        foreach (var item in items)
        {
            TxtStatus.Text = $"Yuklanmoqda: {item.DocumentNumber}...";
            var result = await DownloadContractAsync(item);
            if (result) ok++; else skip++;
        }

        BtnDownloadAll.IsEnabled = true;
        TxtStatus.Text = $"Yuklandi: {ok} ta, o'tkazib yuborildi: {skip} ta";
    }

    private void BtnDownloadRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ContractItem item)
        {
            btn.IsEnabled = false;
            _ = DownloadContractAsync(item).ContinueWith(_ =>
                Dispatcher.InvokeAsync(() => btn.IsEnabled = true));
        }
    }

    private async Task<bool> DownloadContractAsync(ContractItem item)
    {
        var allFiles = new List<(string Url, string Name)>();

        if (item.ConstantDetails != null)
            foreach (var d in item.ConstantDetails)
                if (!string.IsNullOrWhiteSpace(d.File))
                    allFiles.Add((d.File, d.PunktName));

        if (item.Details != null)
            foreach (var d in item.Details)
                if (!string.IsNullOrWhiteSpace(d.File))
                    allFiles.Add((d.File, d.PunktName));

        if (allFiles.Count == 0) return false;

        var branchFolder = SanitizeName(_selectedBranch?.Name ?? "Umumiy");
        var contractFolder = SanitizeName($"{item.DocumentNumber} - {item.Name}");
        var dirPath = Path.Combine(@"C:\Shartnomalar", branchFolder, contractFolder);

        if (Directory.Exists(dirPath))
            Directory.Delete(dirPath, recursive: true);
        Directory.CreateDirectory(dirPath);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var nameCount = new Dictionary<string, int>();

        foreach (var (url, rawName) in allFiles)
        {
            try
            {
                var baseName = SanitizeName(string.IsNullOrWhiteSpace(rawName) ? "fayl" : rawName);
                nameCount.TryGetValue(baseName, out int cnt);
                nameCount[baseName] = cnt + 1;
                var fileName = cnt == 0 ? $"{baseName}.pdf" : $"{baseName}_{cnt}.pdf";
                var filePath = Path.Combine(dirPath, fileName);

                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
            }
            catch { }
        }

        return true;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    // ─── Navigatsiya ─────────────────────────────────────────────────

    private void BtnBack_Click(object sender, RoutedEventArgs e) => AppShell.Current?.GoBack();

    private void BtnScan_Click(object sender, RoutedEventArgs e) =>
        AppShell.Current?.Navigate(new BarcodeScanPage());

    private void DgContracts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_isAdmin) return;
        if (DgContracts.SelectedItem is ContractItem item)
            AppShell.Current?.Navigate(new ScannerPage(item));
    }
}
