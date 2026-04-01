using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CzurWpfDemo.Models;
using CzurWpfDemo.Services;

namespace CzurWpfDemo.Views;

public partial class DocumentsWindow : Window
{
    private readonly ContractItem _contract;

    public DocumentsWindow(ContractItem contract)
    {
        InitializeComponent();
        _contract = contract;

        TxtTitle.Text = $"Hujjatlar — {contract.Name}";
        TxtSubtitle.Text = $"Hujjat raqami: {contract.DocumentNumber}";
        TxtDocNumber.Text = contract.DocumentNumber;
        TxtClientName.Text = contract.Name;
        TxtPhone.Text = contract.TelNumber;
        TxtContractStatus.Text = contract.Status;

        Loaded += DocumentsWindow_Loaded;
    }

    private async void DocumentsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDocumentsAsync();
    }

    private async Task LoadDocumentsAsync()
    {
        TxtStatus.Text = "Yuklanmoqda...";

        try
        {
            var allDocTypes = await ContractDocumentService.GetAllAsync();

            if (allDocTypes == null || allDocTypes.Count == 0)
            {
                TxtStatus.Text = "Hujjat turlari topilmadi";
                return;
            }

            // constant_details va details dan to'ldirilgan hujjat turlarini aniqlash
            var filledDocIds = new HashSet<int>();
            var filledEntries = new Dictionary<int, ContractDetailEntry>();

            if (_contract.ConstantDetails != null)
            {
                foreach (var d in _contract.ConstantDetails)
                {
                    filledDocIds.Add(d.ContractDocumentId);
                    filledEntries[d.ContractDocumentId] = d;
                }
            }

            if (_contract.Details != null)
            {
                foreach (var d in _contract.Details)
                {
                    filledDocIds.Add(d.ContractDocumentId);
                    filledEntries[d.ContractDocumentId] = d;
                }
            }

            CardsPanel.Items.Clear();

            foreach (var docType in allDocTypes)
            {
                bool isFilled = filledDocIds.Contains(docType.Id);
                var card = CreateCard(docType, isFilled, filledEntries.GetValueOrDefault(docType.Id));
                CardsPanel.Items.Add(card);
            }

            int filledCount = allDocTypes.Count(d => filledDocIds.Contains(d.Id));
            TxtStatus.Text = $"Jami: {allDocTypes.Count} ta hujjat turi | To'ldirilgan: {filledCount} | Qolgan: {allDocTypes.Count - filledCount}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Xatolik: {ex.Message}";
        }
    }

    private Border CreateCard(ContractDocumentType docType, bool isFilled, ContractDetailEntry? entry)
    {
        var bgColor = ParseColor(docType.Color);

        var card = new Border
        {
            Width = 190,
            Height = 170,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(bgColor),
            BorderThickness = new Thickness(isFilled ? 3 : 1),
            BorderBrush = new SolidColorBrush(isFilled
                ? Color.FromRgb(16, 185, 129)   // yashil — to'ldirilgan
                : Color.FromRgb(55, 65, 81)),    // kulrang — bo'sh
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12)
        };

        // SVG o'rniga rasm
        try
        {
            var img = new Image
            {
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
                Source = new BitmapImage(new Uri(docType.Svg, UriKind.Absolute))
            };
            stack.Children.Add(img);
        }
        catch
        {
            // rasm yuklanmasa - o'tkazib yuboramiz
        }

        // Hujjat nomi
        var nameText = new TextBlock
        {
            Text = docType.PunktName,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(nameText);

        // Holat
        var statusText = new TextBlock
        {
            Text = isFilled ? "To'ldirilgan" : "To'ldirilmagan",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(isFilled
                ? Color.FromRgb(5, 150, 105)
                : Color.FromRgb(156, 163, 175)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        stack.Children.Add(statusText);

        // To'ldirilgan bo'lsa — mas'ul va sana
        if (isFilled && entry != null)
        {
            var infoText = new TextBlock
            {
                Text = $"{entry.ResponsibleWorker} | {entry.Date}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(infoText);
        }

        card.Child = stack;
        return card;
    }

    /// <summary>
    /// "0xFFFFF0D1" formatdagi rangni WPF Color ga aylantirish
    /// </summary>
    private static Color ParseColor(string colorStr)
    {
        try
        {
            // "0xFF" prefixni olib tashlaymiz
            var hex = colorStr.Replace("0x", "").Replace("0X", "");
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex[..2], 16);
                byte r = Convert.ToByte(hex[2..4], 16);
                byte g = Convert.ToByte(hex[4..6], 16);
                byte b = Convert.ToByte(hex[6..8], 16);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch { }

        return Color.FromRgb(28, 31, 46); // fallback
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
