using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();
        Loaded += ReportWindow_Loaded;
    }

    private async void ReportWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var user = AuthService.CurrentUser;
        if (user != null)
            TxtUserInfo.Text = $"{user.Name} — {user.Role}";

        // Default sanalar: 1-yanvar dan bugunga
        DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
        DpTo.SelectedDate = DateTime.Now;

        await LoadReportAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        await LoadReportAsync();
    }

    private async void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "";
        DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
        DpTo.SelectedDate = DateTime.Now;
        await LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        TxtStatus.Text = "Yuklanmoqda...";
        DgReport.ItemsSource = null;

        try
        {
            string? search = string.IsNullOrWhiteSpace(TxtSearch.Text) ? null : TxtSearch.Text.Trim();
            string? dateFrom = DpFrom.SelectedDate?.ToString("dd-MM-yyyy");
            string? dateTo = DpTo.SelectedDate?.ToString("dd-MM-yyyy");

            var data = await ReportService.GetByUserAsync(search, null, dateFrom, dateTo);

            if (data != null)
            {
                DgReport.ItemsSource = data;
                TxtCount.Text = $"Jami: {data.Count} ta";
                TxtStatus.Text = "Tayyor";
            }
            else
            {
                TxtStatus.Text = "Ma'lumot topilmadi";
                TxtCount.Text = "";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xatolik: {ex.Message}";
            TxtCount.Text = "";
        }
    }

    private void DgReport_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgReport.SelectedItem is ReportItem item)
        {
            var detailsWindow = new ContractDetailsWindow(item.UserId, item.UserName);
            detailsWindow.Owner = this;
            detailsWindow.ShowDialog();
        }
    }
}
