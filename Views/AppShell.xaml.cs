using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class AppShell : Window
{
    public static AppShell? Current { get; private set; }

    private readonly Stack<UserControl> _navStack    = new();
    private readonly HidButtonService   _hidService  = new();

    public AppShell()
    {
        InitializeComponent();
        Current = this;
        try { System.IO.File.AppendAllText(@"C:\czur_wia_debug.txt",
            $"[{DateTime.Now:HH:mm:ss.fff}] [AppShell] constructor{Environment.NewLine}"); } catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        try { System.IO.File.AppendAllText(@"C:\czur_wia_debug.txt",
            $"[{DateTime.Now:HH:mm:ss.fff}] [AppShell] OnSourceInitialized kirdi{Environment.NewLine}"); } catch { }
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // CZUR "Scan" tugmasi → rasmga olish
        _hidService.ButtonPressed += () =>
            Dispatcher.Invoke(() =>
            {
                if (ContentArea.Content is ScannerPage scanner)
                    scanner.TriggerCapture();
            });
        _hidService.Start(hwnd);
    }

    // Yangi sahifaga o'tish
    public async void Navigate(UserControl page)
    {
        if (ContentArea.Content is ScannerPage scanner)
            await scanner.StopCameraAsync();

        if (ContentArea.Content is UserControl current)
            _navStack.Push(current);

        ContentArea.Content = page;
    }

    // Joriy sahifani stack ga qo'shmasdan almashtirish (BarcodeScanPage → ScannerPage uchun)
    public async void NavigateReplace(UserControl page)
    {
        if (ContentArea.Content is ScannerPage scanner)
            await scanner.StopCameraAsync();
        if (ContentArea.Content is BarcodeScanPage bsp)
            await bsp.StopCameraAsync();

        ContentArea.Content = page;
    }

    // Orqaga qaytish
    public async void GoBack()
    {
        if (ContentArea.Content is ScannerPage scanner)
            await scanner.StopCameraAsync();

        if (_navStack.Count > 0)
        {
            var prev = _navStack.Pop();
            ContentArea.Content = prev;

            if (prev is ContractDetailsPage detailsPage)
                _ = detailsPage.RefreshAsync();
        }
    }

    public bool CanGoBack => _navStack.Count > 0;

    protected override async void OnClosed(EventArgs e)
    {
        _hidService.Stop();
        if (ContentArea.Content is ScannerPage scanner)
            await scanner.StopCameraAsync();
        Current = null;
        base.OnClosed(e);
    }
}
