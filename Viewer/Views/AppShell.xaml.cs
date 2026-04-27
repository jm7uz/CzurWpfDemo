using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CzurWpfDemo.Views;

public partial class AppShell : Window
{
    public static AppShell? Current { get; private set; }

    private readonly Stack<UserControl> _navStack = new();

    public AppShell()
    {
        InitializeComponent();
        Current = this;
    }

    public void Navigate(UserControl page)
    {
        if (ContentArea.Content is UserControl current)
            _navStack.Push(current);

        ContentArea.Content = page;
    }

    public void NavigateReplace(UserControl page)
    {
        ContentArea.Content = page;
    }

    public void GoBack()
    {
        if (_navStack.Count > 0)
        {
            var prev = _navStack.Pop();
            ContentArea.Content = prev;

            if (prev is ContractDetailsPage detailsPage)
                _ = detailsPage.RefreshAsync();
        }
    }

    public bool CanGoBack => _navStack.Count > 0;

    protected override void OnClosed(EventArgs e)
    {
        Current = null;
        base.OnClosed(e);
    }
}
