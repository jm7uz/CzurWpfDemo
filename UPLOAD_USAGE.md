# UploadService - PDF Yuklash Servisi

## Qisqacha ma'lumot

`UploadService` - PDF fayllarni API ga yuklash uchun maxsus servis. Faqat PDF formatdagi fayllarni qabul qiladi va serverga `file` + `branch` parametrlari bilan yuboradi.

## API Response formati

```json
{
    "success": true,
    "message": "Fayl yuklandi!",
    "resoult": {
        "url": "http://192.168.10.52:9000/sudfiles/sud-department/constant-file/fargona/mibssga.pdf"
    }
}
```

## Foydalanish

### 1. Fayl yo'li orqali yuklash

```csharp
using CzurWpfDemo.Services;

// PDF faylni yuklash
var response = await UploadService.UploadPdfAsync(
    pdfFilePath: @"C:\Documents\hujjat.pdf",
    branchName: "fargona"
);

if (response != null && response.Success)
{
    var fileUrl = response.Resoult?.Url;
    MessageBox.Show($"Fayl yuklandi!\nURL: {fileUrl}");
}
else
{
    MessageBox.Show($"Xatolik: {response?.Message ?? "Noma'lum xatolik"}");
}
```

### 2. Byte massiv orqali yuklash

```csharp
// PDF ni byte[] sifatida yuklash
byte[] pdfBytes = File.ReadAllBytes(@"C:\Documents\hujjat.pdf");

var response = await UploadService.UploadPdfBytesAsync(
    pdfBytes: pdfBytes,
    fileName: "hujjat.pdf",
    branchName: "fargona"
);

if (response != null && response.Success)
{
    Console.WriteLine($"✅ Yuklandi: {response.Resoult?.Url}");
}
```

### 3. Yordamchi metodlar

```csharp
// Faylning PDF ekanligini tekshirish
bool isPdf = UploadService.IsPdfFile(@"C:\Documents\hujjat.pdf");

// Fayl hajmini MB da olish
double sizeMB = UploadService.GetFileSizeMB(@"C:\Documents\hujjat.pdf");
Console.WriteLine($"Fayl hajmi: {sizeMB:F2} MB");
```

## WPF da foydalanish misoli

### OpenFileDialog bilan PDF tanlash va yuklash

```csharp
private async void BtnUploadPdf_Click(object sender, RoutedEventArgs e)
{
    // 1. PDF faylni tanlash
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = "PDF faylni tanlang",
        Filter = "PDF Fayllar|*.pdf",
        DefaultExt = ".pdf"
    };

    if (dialog.ShowDialog() != true)
        return;

    // 2. Faylni tekshirish
    if (!UploadService.IsPdfFile(dialog.FileName))
    {
        MessageBox.Show("Faqat PDF fayl yuklash mumkin!", "Xatolik",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // 3. Fayl hajmini tekshirish (masalan, 10MB dan oshmasin)
    double sizeMB = UploadService.GetFileSizeMB(dialog.FileName);
    if (sizeMB > 10)
    {
        MessageBox.Show($"Fayl hajmi {sizeMB:F2} MB. Maksimal 10 MB ruxsat etiladi!",
            "Xatolik", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // 4. Yuklash
    TxtStatus.Text = "Yuklanmoqda...";
    BtnUpload.IsEnabled = false;

    try
    {
        var response = await UploadService.UploadPdfAsync(
            dialog.FileName,
            "fargona"  // Filial nomini ComboBox dan olish mumkin
        );

        if (response != null && response.Success)
        {
            TxtStatus.Text = "Muvaffaqiyatli yuklandi!";
            MessageBox.Show(
                $"Fayl yuklandi!\n\nURL:\n{response.Resoult?.Url}",
                "Muvaffaqiyat",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        else
        {
            TxtStatus.Text = "Yuklashda xatolik";
            MessageBox.Show(
                response?.Message ?? "Noma'lum xatolik",
                "Xatolik",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
    catch (Exception ex)
    {
        TxtStatus.Text = "Xatolik yuz berdi";
        MessageBox.Show($"Xatolik: {ex.Message}", "Xatolik",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        BtnUpload.IsEnabled = true;
    }
}
```

### Drag & Drop bilan PDF yuklash

```csharp
// XAML da:
// <Border AllowDrop="True" Drop="Border_Drop" DragOver="Border_DragOver">

private void Border_DragOver(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        e.Effects = DragDropEffects.Copy;
    }
    else
    {
        e.Effects = DragDropEffects.None;
    }
    e.Handled = true;
}

private async void Border_Drop(object sender, DragEventArgs e)
{
    if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        return;

    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
    if (files.Length == 0)
        return;

    string filePath = files[0];

    // PDF tekshirish
    if (!UploadService.IsPdfFile(filePath))
    {
        MessageBox.Show("Faqat PDF fayl yuklash mumkin!");
        return;
    }

    // Yuklash
    var response = await UploadService.UploadPdfAsync(filePath, "fargona");

    if (response != null && response.Success)
    {
        MessageBox.Show($"Yuklandi: {response.Resoult?.Url}");
    }
}
```

## Xatoliklarni boshqarish

### Fayl topilmasa
```csharp
var response = await UploadService.UploadPdfAsync("path/to/missing.pdf", "fargona");
// response = null (Console da "❌ PDF yuklashda xatolik: Fayl topilmadi")
```

### PDF bo'lmagan fayl
```csharp
var response = await UploadService.UploadPdfAsync("document.docx", "fargona");
// response = null (Console da "❌ PDF yuklashda xatolik: Faqat PDF fayl yuklash mumkin!")
```

### Tarmoq xatoligi
```csharp
var response = await UploadService.UploadPdfAsync("document.pdf", "fargona");
// response = null (yoki response.Success = false)
```

## Filial nomlari

Quyidagi filial nomlaridan foydalanish mumkin:
- `fargona`
- `andijon`
- `namangan`
- `samarqand`
- `buxoro`
- va boshqalar...

Filial nomini ComboBox orqali tanlash tavsiya etiladi.

## API Endpoint

```
POST /upload
Content-Type: multipart/form-data

Parameters:
- file: PDF fayl (application/pdf)
- branch: Filial nomi (string)
```

## Eslatmalar

1. **Faqat PDF**: Servis faqat `.pdf` kengaytmali fayllarni qabul qiladi
2. **Fayl hajmi**: Katta hajmli fayllarni yuklashdan oldin hajmni tekshiring
3. **Autentifikatsiya**: Token `ApiService.SetToken()` orqali oldindan sozlanishi kerak
4. **Base URL**: API manzili `ApiService.BaseUrl` da belgilangan
