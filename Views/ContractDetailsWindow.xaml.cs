using System.Windows;
using System.Windows.Input;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ContractDetailsWindow : Window
{
    private readonly int _userId;
    private readonly string _userName;
    private int _currentPage = 1;
    private int _lastPage = 1;

    public ContractDetailsWindow(int userId, string userName)
    {
        InitializeComponent();
        _userId = userId;
        _userName = userName;

        TxtTitle.Text = $"{userName} — Shartnomalar";
        TxtSubtitle.Text = $"Foydalanuvchi ID: {userId}";

        Loaded += ContractDetailsWindow_Loaded;
    }

    private async void ContractDetailsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
        DpTo.SelectedDate = DateTime.Now;
        await LoadDataAsync();
    }

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
        Close();
    }

    private void DgContracts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgContracts.SelectedItem is ContractItem item)
        {
            var docsWindow = new DocumentsWindow(item);
            docsWindow.Owner = this;
            docsWindow.ShowDialog();
        }
    }
}
