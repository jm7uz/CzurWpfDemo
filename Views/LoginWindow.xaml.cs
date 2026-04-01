using System.Windows;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        TxtPhone.Focus();
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var phone = TxtPhone.Text.Trim();
        var password = TxtPassword.Password;

        if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(password))
        {
            ShowError("Telefon va parolni kiriting.");
            return;
        }

        SetLoading(true);

        try
        {
            var response = await AuthService.LoginAsync(phone, password);

            if (response is { Status: true, Resoult: not null })
            {
                // Foydalanuvchi ma'lumotlarini olish
                var user = await AuthService.GetMeAsync();

                Window nextWindow;
                if (user?.Role == "superadmin")
                    nextWindow = new ReportWindow();
                else
                    nextWindow = new MainWindow();

                nextWindow.Show();
                Close();
            }
            else
            {
                ShowError("Telefon yoki parol noto'g'ri.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Serverga ulanib bo'lmadi: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private void SetLoading(bool loading)
    {
        BtnLogin.IsEnabled = !loading;
        TxtLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        TxtError.Visibility = Visibility.Collapsed;
    }
}
