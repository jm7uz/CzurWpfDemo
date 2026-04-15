using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class GetContractService
{
    // Barcode raqami bo'yicha shartnomani tekshirish
    public static async Task<GetContractResponse?> ValidateAsync(string documentNumber)
        => await ApiService.PostAsync<GetContractResponse>(
            "get/contract", new GetContractRequest { DocumentNumber = documentNumber });

    // constant_details bilan to'liq shartnoma ma'lumotlarini olish
    public static async Task<ContractDetailsResponse?> SearchAllAsync(string documentNumber)
        => await ApiService.PostAsync<ContractDetailsResponse>(
            "get/all?perPage=100&page=1", new GetAllRequest { Search = documentNumber });
}
