using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class ConstantDocumentDetailService
{
    public static async Task<ConstantDocumentDetailStoreResponse?> StoreAsync(
        long documentNumber, int contractDocumentId, string filePath, int photoCount)
    {
        var request = new ConstantDocumentDetailStoreRequest
        {
            DocumentNumber = documentNumber,
            ContractDocumentId = contractDocumentId,
            File = filePath,
            PhotoCount = photoCount
        };
        return await ApiService.PostAsync<ConstantDocumentDetailStoreResponse>(
            "contract-document-detail/store", request);
    }

    // Mavjud yozuvni yangilash (contract-document-detail/update/{id})
    public static async Task<ConstantDocumentDetailUpdateResponse?> UpdateAsync(
        int detailId, long documentNumber, int contractDocumentId, string filePath, int photoCount)
    {
        var request = new ConstantDocumentDetailStoreRequest
        {
            DocumentNumber = documentNumber,
            ContractDocumentId = contractDocumentId,
            File = filePath,
            PhotoCount = photoCount
        };
        return await ApiService.PostAsync<ConstantDocumentDetailUpdateResponse>(
            $"contract-document-detail/update/{detailId}", request);
    }
}
