using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class UploadService
{
    /// <summary>
    /// PDF faylni serverga yuklash
    /// </summary>
    /// <param name="pdfFilePath">PDF faylning to'liq yo'li</param>
    /// <param name="branchName">Filial nomi (masalan: "fargona")</param>
    /// <returns>Yuklangan faylning URL manzili yoki null</returns>
    public static async Task<UploadResponse?> UploadPdfAsync(string pdfFilePath, string branchName)
    {
        try
        {
            // 1. Fayl mavjudligini tekshirish
            if (!File.Exists(pdfFilePath))
            {
                throw new FileNotFoundException($"Fayl topilmadi: {pdfFilePath}");
            }

            // 2. Fayl kengaytmasini tekshirish (faqat PDF)
            var extension = Path.GetExtension(pdfFilePath).ToLower();
            if (extension != ".pdf")
            {
                throw new InvalidOperationException($"Faqat PDF fayl yuklash mumkin! Berilgan: {extension}");
            }

            // 3. Faylni o'qish
            var fileBytes = await File.ReadAllBytesAsync(pdfFilePath);
            var fileName = Path.GetFileName(pdfFilePath);

            return await UploadPdfBytesAsync(fileBytes, fileName, branchName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ PDF yuklashda xatolik: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// PDF faylni byte[] sifatida yuklash
    /// </summary>
    /// <param name="pdfBytes">PDF fayl byte massivi</param>
    /// <param name="fileName">Fayl nomi (masalan: "hujjat.pdf")</param>
    /// <param name="branchName">Filial nomi</param>
    /// <returns>Yuklangan faylning URL manzili yoki null</returns>
    public static async Task<UploadResponse?> UploadPdfBytesAsync(byte[] pdfBytes, string fileName, string branchName)
    {
        try
        {
            // 1. Fayl nomini tekshirish
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fayl nomi .pdf bilan tugashi kerak!");
            }

            // 2. Multipart form yaratish
            using var content = new MultipartFormDataContent();

            // PDF faylni qo'shish
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "file", fileName);

            // Branch nomini qo'shish
            content.Add(new StringContent(branchName), "branch");

            // 3. API ga yuborish
            var response = await ApiService.PostMultipartAsync<UploadResponse>("upload", content);

            // 4. Natijani tekshirish
            if (response != null && response.Success)
            {
                Console.WriteLine($"✅ PDF yuklandi: {response.Resoult?.Url}");
                return response;
            }
            else
            {
                Console.WriteLine($"❌ Yuklashda xatolik: {response?.Message ?? "Noma'lum xatolik"}");
                return response;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ PDF yuklashda xatolik: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// PDF faylni base64 formatida upload/base64 ga yuklash (hajm cheklovsiz)
    /// </summary>
    public static async Task<UploadResponse?> UploadPdfBase64Async(string pdfFilePath, int contractId, string clientName)
    {
        try
        {
            var fileBytes = await File.ReadAllBytesAsync(pdfFilePath);
            var base64 = Convert.ToBase64String(fileBytes);

            var request = new Models.UploadBase64Request
            {
                ContractId = contractId,
                ClientName = clientName,
                File       = $"data:application/pdf;base64,{base64}"
            };

            return await ApiService.PostAsync<UploadResponse>("upload/base64", request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Base64 yuklashda xatolik: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Faylning PDF ekanligini tekshirish
    /// </summary>
    public static bool IsPdfFile(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLower();
        return extension == ".pdf";
    }

    /// <summary>
    /// Fayl hajmini MB da olish
    /// </summary>
    public static double GetFileSizeMB(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        var fileInfo = new FileInfo(filePath);
        return fileInfo.Length / (1024.0 * 1024.0);
    }
}
