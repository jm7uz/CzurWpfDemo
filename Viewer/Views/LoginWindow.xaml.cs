using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class LoginWindow : Window
{
    private bool _isPasswordVisible = false;
    private bool _isFormatting = false;

    public LoginWindow()
    {
        InitializeComponent();
        TxtPhone.Focus();
        UpdatePhoneBorderFocus();
    }

    // ──────────────────────────────────────────────
    //  Telefon raqam real-time formatlash
    //  Foydalanuvchi: (93) 737-33-22
    //  Backend: 998937373322
    // ──────────────────────────────────────────────

    private void TxtPhone_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormatting) return;
        _isFormatting = true;

        // Faqat raqamlarni olish (9 ta)
        string digits = Regex.Replace(TxtPhone.Text, @"\D", "");
        if (digits.Length > 9) digits = digits[..9];

        // Formatlash: (xx) xxx-xx-xx
        string formatted = FormatPhoneDigits(digits);

        int caretOffset = formatted.Length - TxtPhone.Text.Length;
        TxtPhone.Text = formatted;

        // Kursorni oxiriga qo'yish
        TxtPhone.CaretIndex = formatted.Length;

        _isFormatting = false;
    }

    private static string FormatPhoneDigits(string digits)
    {
        // digits = max 9 ta son
        return digits.Length switch
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
    }

    // Backspace bilan formatlash belgilarini o'tkazib yuborish
    private void TxtPhone_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && TxtPhone.CaretIndex > 0 && TxtPhone.SelectionLength == 0)
        {
            int pos = TxtPhone.CaretIndex;
            char prev = TxtPhone.Text[pos - 1];
            // Format belgilari: '(', ')', ' ', '-'
            if (prev == ')' || prev == ' ' || prev == '-' || prev == '(')
            {
                // Ikki belgi orqaga ketish
                if (pos >= 2)
                {
                    TxtPhone.Text = TxtPhone.Text[..(pos - 2)];
                    TxtPhone.CaretIndex = TxtPhone.Text.Length;
                }
                else
                {
                    TxtPhone.Text = "";
                }
                e.Handled = true;
            }
        }
    }

    // Backend uchun raqam: 998 + 9 ta raqam
    private string GetRawPhone()
    {
        string digits = Regex.Replace(TxtPhone.Text, @"\D", "");
        return "998" + digits;
    }

    // ──────────────────────────────────────────────
    //  Parol ko'rish/yashirish
    // ──────────────────────────────────────────────

    private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            TxtPasswordVisible.Text = TxtPassword.Password;
            TxtPasswordVisible.Visibility = Visibility.Visible;
            TxtPassword.Visibility = Visibility.Collapsed;
            TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
            TxtPasswordVisible.Focus();
        }
        else
        {
            TxtPassword.Password = TxtPasswordVisible.Text;
            TxtPassword.Visibility = Visibility.Visible;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            TxtPassword.Focus();
        }
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) _ = LoginAsync();
    }

    private void TxtPasswordVisible_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) _ = LoginAsync();
    }

    // ──────────────────────────────────────────────
    //  PhoneBorder fokus ko'rinishi (PhoneBorder border rengi)
    // ──────────────────────────────────────────────

    private void UpdatePhoneBorderFocus()
    {
        TxtPhone.GotFocus  += (_, _) => PhoneBorder.BorderBrush =
            new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
        TxtPhone.LostFocus += (_, _) => PhoneBorder.BorderBrush =
            new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252B3B"));
    }

    // ──────────────────────────────────────────────
    //  Login
    // ──────────────────────────────────────────────

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        => await LoginAsync();

    private async Task LoginAsync()
    {
        string digits = Regex.Replace(TxtPhone.Text, @"\D", "");
        string password = _isPasswordVisible ? TxtPasswordVisible.Text : TxtPassword.Password;

        if (digits.Length < 9)
        {
            ShowError("To'liq telefon raqam kiriting.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Parolni kiriting.");
            return;
        }

        SetLoading(true);

        try
        {
            var phone = GetRawPhone(); // "998XXXXXXXXX"
            var response = await AuthService.LoginAsync(phone, password);

            if (response is { Status: true, Resoult: not null })
            {
                var user = await AuthService.GetMeAsync();

                var shell = new AppShell();

                if (user?.Role == "superadmin")
                    shell.ContentArea.Content = new ReportPage();
                else if (user?.Role == "user")
                    shell.ContentArea.Content = new ContractDetailsPage(user.Id, user.Name);
                else
                    shell.ContentArea.Content = new ContractDetailsPage(user?.Id ?? 0, user?.Name ?? "");

                shell.Show();
                Close();
            }
            else
            {
                ShowError("Telefon yoki parol noto'g'ri.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Serverga ulanib bo'lmadi.");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void SetLoading(bool loading)
    {
        BtnLogin.IsEnabled = !loading;

        // BtnLogin ichidagi elementlarni topish
        if (BtnLogin.Template.FindName("TxtBtnLabel", BtnLogin) is TextBlock lbl)
            lbl.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        if (BtnLogin.Template.FindName("TxtLoading", BtnLogin) is TextBlock ldg)
            ldg.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
