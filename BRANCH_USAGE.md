# BranchService - Filiallar Servisi

## Qisqacha ma'lumot

`BranchService` - tizimda mavjud filiallarni (branches) olish uchun servis. Paginatsiya bilan ishlaydi va qidiruv funksiyasini qo'llab-quvvatlaydi.

## API Endpoint

```
POST branchs?perPage=10&page=1
Content-Type: application/json

Body:
{
    "search": null
}
```

## Response formati

```json
{
    "status": true,
    "resoult": {
        "data": [
            {
                "id": 1,
                "name": "Фаргона",
                "state_id": 1,
                "state_name": "Фаргона",
                "region_id": 177,
                "region_name": "Фаргона шахар",
                "address": "Фаргона шахар Комус кучаси 32 уй",
                "constantDocumentDetail": [...]
            }
        ],
        "links": {
            "first": "...",
            "last": "...",
            "prev": null,
            "next": "..."
        },
        "meta": {
            "current_page": 1,
            "from": 1,
            "last_page": 4,
            "per_page": 10,
            "to": 10,
            "total": 36
        }
    }
}
```

## Foydalanish

### 1. Paginatsiya bilan filiallarni olish

```csharp
using CzurWpfDemo.Services;

// Birinchi sahifadan 10 ta filial
var response = await BranchService.GetAllAsync(
    search: null,
    perPage: 10,
    page: 1
);

if (response != null && response.Status)
{
    var branches = response.Resoult?.Data;
    var meta = response.Resoult?.Meta;

    Console.WriteLine($"Jami: {meta?.Total} ta filial");
    Console.WriteLine($"Sahifa: {meta?.CurrentPage}/{meta?.LastPage}");

    foreach (var branch in branches)
    {
        Console.WriteLine($"{branch.Id}. {branch.Name} - {branch.Address}");
    }
}
```

### 2. Bitta sahifadagi filiallarni olish

```csharp
// Faqat filiallar ro'yxati (paginatsiya ma'lumotlarisiz)
var branches = await BranchService.GetBranchListAsync();

if (branches != null)
{
    foreach (var branch in branches)
    {
        Console.WriteLine($"{branch.Name} ({branch.StateName}, {branch.RegionName})");
    }
}
```

### 3. Barcha filiallarni olish (barcha sahifalardan)

```csharp
// Barcha sahifalarni avtomatik yuklaydi
var allBranches = await BranchService.GetAllBranchesAsync();

Console.WriteLine($"Jami {allBranches.Count} ta filial yuklandi");
```

### 4. Qidiruv bilan

```csharp
// "Фаргона" so'zi bo'yicha qidiruv
var response = await BranchService.GetAllAsync(search: "Фаргона");

if (response?.Resoult?.Data != null)
{
    foreach (var branch in response.Resoult.Data)
    {
        Console.WriteLine(branch.Name);
    }
}
```

## WPF da foydalanish

### ComboBox ga filiallarni yuklash

```csharp
private async void LoadBranches()
{
    CmbBranch.Items.Clear();

    // "Barcha filiallar" opsiyasi
    CmbBranch.Items.Add(new ComboBoxItem
    {
        Content = "Barcha filiallar",
        Tag = null
    });

    // Filiallarni yuklash
    var branches = await BranchService.GetAllBranchesAsync();

    foreach (var branch in branches)
    {
        CmbBranch.Items.Add(new ComboBoxItem
        {
            Content = branch.Name,
            Tag = branch
        });
    }

    CmbBranch.SelectedIndex = 0;
}

private void CmbBranch_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (CmbBranch.SelectedItem is ComboBoxItem item)
    {
        var selectedBranch = item.Tag as BranchItem;

        if (selectedBranch != null)
        {
            MessageBox.Show($"Tanlandi: {selectedBranch.Name}");
        }
    }
}
```

### DataGrid ga filiallarni ko'rsatish

```csharp
private async void LoadBranchesToGrid()
{
    var response = await BranchService.GetAllAsync(perPage: 20, page: 1);

    if (response?.Resoult?.Data != null)
    {
        DgBranches.ItemsSource = response.Resoult.Data;

        // Paginatsiya
        TxtPageInfo.Text = $"Sahifa {response.Resoult.Meta?.CurrentPage}/{response.Resoult.Meta?.LastPage}";
    }
}
```

### Progress bar bilan barcha filiallarni yuklash

```csharp
private async Task LoadAllBranchesWithProgress()
{
    var allBranches = new List<BranchItem>();
    int currentPage = 1;
    int lastPage = 1;

    do
    {
        var response = await BranchService.GetAllAsync(perPage: 50, page: currentPage);

        if (response?.Resoult?.Data == null)
            break;

        allBranches.AddRange(response.Resoult.Data);
        lastPage = response.Resoult.Meta?.LastPage ?? 1;

        // Progress yangilash
        ProgressBar.Value = (currentPage * 100.0) / lastPage;
        TxtStatus.Text = $"Yuklanmoqda: {currentPage}/{lastPage}";

        currentPage++;

    } while (currentPage <= lastPage);

    TxtStatus.Text = $"Tayyor! {allBranches.Count} ta filial yuklandi";
    DgBranches.ItemsSource = allBranches;
}
```

## BranchItem xususiyatlari

```csharp
public class BranchItem
{
    public int Id { get; set; }                    // Filial ID
    public string Name { get; set; }               // Filial nomi
    public int StateId { get; set; }               // Viloyat ID
    public string StateName { get; set; }          // Viloyat nomi
    public int RegionId { get; set; }              // Tuman ID
    public string RegionName { get; set; }         // Tuman nomi
    public string Address { get; set; }            // Manzil
    public List<BranchConstantDocument>? ConstantDocumentDetail { get; set; }
}
```

## Constant Document Detail

Har bir filialda doimiy hujjatlar ro'yxati mavjud:

```csharp
foreach (var doc in branch.ConstantDocumentDetail)
{
    Console.WriteLine($"Hujjat: {doc.PunktName}");
    Console.WriteLine($"Fayl: {doc.File}");
    Console.WriteLine($"Mas'ul: {doc.ResponsibleWorker}");
    Console.WriteLine($"Sana: {doc.Date}");
}
```

## Paginatsiya ma'lumotlari

```csharp
var meta = response.Resoult?.Meta;

Console.WriteLine($"Joriy sahifa: {meta.CurrentPage}");
Console.WriteLine($"Jami sahifalar: {meta.LastPage}");
Console.WriteLine($"Sahifadagi elementlar: {meta.PerPage}");
Console.WriteLine($"Jami elementlar: {meta.Total}");
Console.WriteLine($"Dan: {meta.From}, Gacha: {meta.To}");
```

## Xatoliklarni boshqarish

```csharp
var response = await BranchService.GetAllAsync();

if (response == null)
{
    MessageBox.Show("Serverga ulanib bo'lmadi!");
}
else if (!response.Status)
{
    MessageBox.Show("Ma'lumot topilmadi!");
}
else if (response.Resoult?.Data == null || response.Resoult.Data.Count == 0)
{
    MessageBox.Show("Filiallar ro'yxati bo'sh!");
}
else
{
    // Muvaffaqiyatli yuklandi
}
```

## Eslatmalar

1. **GetAllBranchesAsync()** barcha sahifalarni ketma-ket yuklaydi, shuning uchun katta hajmda ma'lumot bo'lsa biroz vaqt olishi mumkin
2. **Search** parametri ixtiyoriy - `null` berilsa barcha filiallar qaytadi
3. **PerPage** default qiymati 100, lekin kerak bo'lsa o'zgartirilishi mumkin
4. Har bir filialda `ConstantDocumentDetail` ro'yxati mavjud bo'lishi shart emas (null bo'lishi mumkin)
