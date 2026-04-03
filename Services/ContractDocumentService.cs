using System.Collections.Generic;
using System.Net.Http;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class ContractDocumentService
{
    public static async Task<List<ContractDocumentType>?> GetAllAsync()
    {
        var response = await ApiService.GetAsync<ContractDocumentAllResponse>("contract-document/all");
        return response?.Resoult?.Data;
    }

    public static async Task<bool> UploadDocumentAsync(string documentNumber, int contractDocumentId, byte[] fileData, string fileName)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            // Fayl ma'lumotlarini qo'shish
            var fileContent = new ByteArrayContent(fileData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", fileName);

            // Qo'shimcha ma'lumotlar
            content.Add(new StringContent(documentNumber), "document_number");
            content.Add(new StringContent(contractDocumentId.ToString()), "contract_document_id");

            var response = await ApiService.PostMultipartAsync("contract-document/upload", content);
            return response;
        }
        catch
        {
            return false;
        }
    }
}
