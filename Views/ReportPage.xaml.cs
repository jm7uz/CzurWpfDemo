using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class ReportPage : UserControl
{
    private bool _initialized = false;
    private bool _isUserPasswordVisible = false;
    private bool _isFormattingUserPhone = false;
    private int? _editingUserId = null;   // null = yangi yaratish, qiymat = tahrirlash

    // ─── Telefon formatlash yordamchisi ──────────────────────────────────────

    private static string FormatPhoneDigits(string digits) => digits.Length switch
    {
        0 => "",
        1 => $"({digits[0]}",
        2 => $"({digits[..2]}",
        3 => $"({digits[..2]}) {digits[2]}",
        4 => $"({digits[..2]}) {digits[2..4]}",
        5 => $"({digits[..2]}) {digits[2..5]}",
        6 => $"({digits[..2]}) {digits[2..5]}-{digits[5]}",
        7 => $"({digits[..2]}) {digits[2..5]}-{digits[5..7]}",
        8 => $"({digits[..2]}) {digits[2..5]}-{digits[5..7]}-{digits[7]}",
        _ => $"({digits[..2]}) {digits[2..5]}-{digits[5..7]}-{digits[7..9]}"
    };

    private string GetRawUserPhone() => "998" + Regex.Replace(TxtUserPhone.Text, @"\D", "");

    // Telefon raqamni (998xxxxxxxxx) forma uchun formatlash
    private void SetPhoneField(string rawPhone)
    {
        var digits = Regex.Replace(rawPhone, @"\D", "");
        if (digits.StartsWith("998") && digits.Length >= 12) digits = digits[3..];
        if (digits.Length > 9) digits = digits[..9];
        _isFormattingUserPhone = true;
        TxtUserPhone.Text = FormatPhoneDigits(digits);
        _isFormattingUserPhone = false;
        HintUserPhone.Visibility = string.IsNullOrEmpty(TxtUserPhone.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public ReportPage()
    {
        InitializeComponent();
        Loaded += ReportPage_Loaded;
    }

    private async void ReportPage_Loaded(object sender, RoutedEventArgs e)
    {
        var user = AuthService.CurrentUser;
        if (user != null)
            //TxtUserInfo.Text = $"{user.Name} — {user.Role}";

        // Birinchi yuklanishda default sanalar, keyingi safar eslab qoladi
        if (!_initialized)
        {
            _initialized = true;
            DpFrom.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
            DpTo.SelectedDate   = DateTime.Now;
        }

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
        if (e.OriginalSource is FrameworkElement fe && HasButtonAncestor(fe)) return;
        if (DgReport.SelectedItem is ReportItem item)
            AppShell.Current?.Navigate(new ContractDetailsPage(item.UserId, item.UserName));
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        var cur = element;
        while (cur != null)
        {
            if (cur is Button) return true;
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
        }
        return false;
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        loginWindow.Show();
        AppShell.Current?.Close();
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        AppShell.Current?.Navigate(new BarcodeScanPage());
    }

    private void DgReport_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    // ─── Unduruvchi tahrirlash ────────────────────────────────────────────────

    private void BtnEdit_PreviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ReportItem item) return;

        _editingUserId = item.UserId;

        // Overlay ni tahrirlash rejimida ochish
        TxtOverlayTitle.Text    = "Hodimni tahrirlash";
        TxtOverlaySubtitle.Text = "Ma'lumotlarni yangilang";
        HintUserPassword.Text   = "Bo'sh qoldirsa o'zgarmaydi";

        TxtUserName.Text = item.UserName;
        SetPhoneField(item.UserPhone);

        PbUserPassword.Password       = "";
        TxtUserPasswordVisible.Text   = "";
        CbUserRole.SelectedIndex      = 0;
        ChkIsActive.IsChecked         = true;
        ErrorPanel.Visibility         = Visibility.Collapsed;
        BtnSaveUser.IsEnabled         = true;

        _isUserPasswordVisible            = false;
        PbUserPassword.Visibility         = Visibility.Visible;
        TxtUserPasswordVisible.Visibility = Visibility.Collapsed;
        HintUserName.Visibility           = Visibility.Collapsed;
        HintUserPassword.Visibility       = Visibility.Visible;
        UserPhoneBorder.BorderBrush       = new SolidColorBrush(Color.FromRgb(55, 65, 100));

        CreateUserOverlay.Visibility = Visibility.Visible;
        TxtUserName.Focus();
    }

    // ─── Unduruvchi yaratish ──────────────────────────────────────────────────

    private void BtnCreateUser_Click(object sender, RoutedEventArgs e)
    {
        _editingUserId = null;

        TxtOverlayTitle.Text    = "Yangi hodim";
        TxtOverlaySubtitle.Text = "Ma'lumotlarni to'ldiring";
        HintUserPassword.Text   = "Kamida 6 ta belgi";

        TxtUserName.Text              = "";
        TxtUserPhone.Text             = "";
        PbUserPassword.Password       = "";
        TxtUserPasswordVisible.Text   = "";
        CbUserRole.SelectedIndex      = 0;
        ChkIsActive.IsChecked         = true;
        ErrorPanel.Visibility         = Visibility.Collapsed;
        BtnSaveUser.IsEnabled         = true;

        _isUserPasswordVisible            = false;
        PbUserPassword.Visibility         = Visibility.Visible;
        TxtUserPasswordVisible.Visibility = Visibility.Collapsed;

        HintUserName.Visibility     = Visibility.Visible;
        HintUserPhone.Visibility    = Visibility.Visible;
        HintUserPassword.Visibility = Visibility.Visible;

        UserPhoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 100));

        CreateUserOverlay.Visibility = Visibility.Visible;
        TxtUserName.Focus();
    }

    private void BtnCancelCreate_Click(object sender, RoutedEventArgs e)
    {
        CreateUserOverlay.Visibility = Visibility.Collapsed;
    }

    private async void BtnSaveUser_Click(object sender, RoutedEventArgs e)
    {
        var name     = TxtUserName.Text.Trim();
        var rawPhone = GetRawUserPhone();
        var password = _isUserPasswordVisible ? TxtUserPasswordVisible.Text : PbUserPassword.Password;
        var role     = (CbUserRole.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "user";
        var isActive = ChkIsActive.IsChecked == true;

        if (string.IsNullOrWhiteSpace(name))  { ShowCreateError("Ism familya kiriting"); return; }
        if (rawPhone.Length < 12)              { ShowCreateError("Telefon raqam to'liq kiriting"); return; }
        // Yangi yaratishda parol majburiy, tahrirlashda ixtiyoriy
        if (_editingUserId == null && string.IsNullOrWhiteSpace(password))
            { ShowCreateError("Parol kiriting"); return; }

        BtnSaveUser.IsEnabled = false;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            bool success;
            string? errorMessage;

            if (_editingUserId is int uid)
            {
                // ── Tahrirlash ──
                var req = new UserUpdateRequest
                {
                    Name     = name,
                    Phone    = rawPhone,
                    Password = string.IsNullOrWhiteSpace(password) ? null : password,
                    IsActive = isActive,
                    Role     = role
                };
                var result = await UserService.UpdateAsync(uid, req);
                success      = result?.Status == true;
                errorMessage = result?.Resoult;
            }
            else
            {
                // ── Yangi yaratish ──
                var req = new UserStoreRequest
                {
                    Name     = name,
                    Phone    = rawPhone,
                    Password = password,
                    IsActive = isActive,
                    Role     = role
                };
                var result   = await UserService.StoreAsync(req);
                success      = result?.Status == true;
                errorMessage = result?.Message;
            }

            if (success)
            {
                CreateUserOverlay.Visibility = Visibility.Collapsed;
                await LoadReportAsync();
            }
            else
            {
                ShowCreateError(errorMessage ?? "Saqlashda xatolik yuz berdi");
            }
        }
        catch (Exception ex)
        {
            ShowCreateError($"Xatolik: {ex.Message}");
        }
        finally
        {
            BtnSaveUser.IsEnabled = true;
        }
    }

    // ─── Ism maydoni hint ────────────────────────────────────────────────────

    private void TxtUserName_TextChanged(object sender, TextChangedEventArgs e)
    {
        HintUserName.Visibility = string.IsNullOrEmpty(TxtUserName.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Parol hint (PasswordBox) ─────────────────────────────────────────────

    private void PbUserPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUserPasswordVisible)
            HintUserPassword.Visibility = string.IsNullOrEmpty(PbUserPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Telefon maydoni ─────────────────────────────────────────────────────

    private void TxtUserPhone_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormattingUserPhone) return;
        _isFormattingUserPhone = true;

        var digits = Regex.Replace(TxtUserPhone.Text, @"\D", "");
        if (digits.Length > 9) digits = digits[..9];
        var formatted = FormatPhoneDigits(digits);

        TxtUserPhone.Text = formatted;
        TxtUserPhone.CaretIndex = formatted.Length;

        HintUserPhone.Visibility = string.IsNullOrEmpty(formatted) ? Visibility.Visible : Visibility.Collapsed;

        _isFormattingUserPhone = false;
    }

    private void TxtUserPhone_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back) return;

        var tb = (TextBox)sender;
        if (tb.CaretIndex == 0) return;

        var ch = tb.Text[tb.CaretIndex - 1];
        if (ch == ' ' || ch == '-' || ch == '(' || ch == ')')
        {
            e.Handled = true;
            _isFormattingUserPhone = true;
            var pos = tb.CaretIndex - 2;
            if (pos < 0) pos = 0;
            tb.Text = tb.Text[..pos] + tb.Text[(tb.CaretIndex)..];
            var digits = Regex.Replace(tb.Text, @"\D", "");
            if (digits.Length > 9) digits = digits[..9];
            var formatted = FormatPhoneDigits(digits);
            tb.Text = formatted;
            tb.CaretIndex = Math.Max(0, formatted.Length > 0 ? formatted.Length : 0);
            HintUserPhone.Visibility = string.IsNullOrEmpty(formatted) ? Visibility.Visible : Visibility.Collapsed;
            _isFormattingUserPhone = false;
        }
    }

    private void TxtUserPhone_GotFocus(object sender, RoutedEventArgs e)
    {
        UserPhoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        HintUserPhone.Visibility = Visibility.Collapsed;
    }

    private void TxtUserPhone_LostFocus(object sender, RoutedEventArgs e)
    {
        UserPhoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 100));
        HintUserPhone.Visibility = string.IsNullOrEmpty(TxtUserPhone.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Parol ko'rinishi ─────────────────────────────────────────────────────

    private void BtnToggleUserPassword_Click(object sender, RoutedEventArgs e)
    {
        _isUserPasswordVisible = !_isUserPasswordVisible;
        if (_isUserPasswordVisible)
        {
            TxtUserPasswordVisible.Text       = PbUserPassword.Password;
            TxtUserPasswordVisible.Visibility = Visibility.Visible;
            PbUserPassword.Visibility         = Visibility.Collapsed;
            HintUserPassword.Visibility       = Visibility.Collapsed;
        }
        else
        {
            PbUserPassword.Password           = TxtUserPasswordVisible.Text;
            PbUserPassword.Visibility         = Visibility.Visible;
            TxtUserPasswordVisible.Visibility = Visibility.Collapsed;
            HintUserPassword.Visibility       = string.IsNullOrEmpty(PbUserPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowCreateError(string message)
    {
        TxtCreateError.Text   = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
