using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ContractDetailsPage : UserControl
{
    private int _userId;
    private string _userName;
    private int _currentPage = 1;
    private int _lastPage = 1;
    private bool _initialized = false;
    private bool _isAdmin;
    private bool _cmbUserLoading = false;

    public ContractDetailsPage(int userId, string userName)
    {
        InitializeComponent();
        _userId = userId;
        _userName = userName;
        _isAdmin = AuthService.CurrentUser?.Role == "superadmin";

        TxtTitle.Text = $"{userName} — Shartnomalar";

        Loaded += ContractDetailsPage_Loaded;
    }

    private async void ContractDetailsPage_Loaded(object sender, RoutedEventArgs e)
    {
        BtnBack.Visibility = AppShell.Current?.CanGoBack == true
            ? Visibility.Visible : Visibility.Collapsed;

        UserFilterPanel.Visibility = Visibility.Visible;

        if (!_initialized)
        {
            _initialized = true;
            DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
            DpTo.SelectedDate   = DateTime.Now;

            await LoadUsersAsync();
        }

        await LoadDataAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await UserService.GetAllUsersAsync();
            _cmbUserLoading = true;
            CmbUser.ItemsSource = users;
            CmbUser.SelectedItem = users.FirstOrDefault(u => u.Id == _userId);
            _cmbUserLoading = false;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xodimlar yuklanmadi: {ex.Message}";
            _cmbUserLoading = false;
        }
    }

    private void CmbUser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cmbUserLoading) return;
        if (CmbUser.SelectedItem is not UserItem user) return;

        _userId = user.Id;
        _userName = user.Name;
        TxtTitle.Text = $"{user.Name} — Shartnomalar";
        _currentPage = 1;
        _ = LoadDataAsync();
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

    private void BtnScan_Click(object sender, RoutedEventArgs e) { }

    private void DgContracts_MouseDoubleClick(object sender, MouseButtonEventArgs e) { }
}
