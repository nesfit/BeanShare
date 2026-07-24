using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Application.Features.Consumption.Dtos;

namespace BeanShare.Maui.Services;

public interface IConsumptionService
{
    Task<GetRecentConsumptionsResult?> GetRecentConsumptionsAsync(Guid spaceId, Guid? forUserId = null);
    Task<bool> RecordConsumptionAsync(Guid spaceId, int grams, DateTime consumedAt);
    Task<bool> RecordConsumptionAsync(Guid spaceId, string productName, string productBrand, string productType, int grams, DateTime consumedAt, string? presetName = null, Guid? forUserId = null, Guid? stockLotId = null);
    Task<ConsumptionHistoryDto?> GetUserHistoryAsync(
        Guid? spaceId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? billingPeriodId = null,
        int pageNumber = 1,
        int pageSize = 20,
        Guid? memberUserId = null);
    Task<ImportConsumptionCsvResult?> ImportCsvAsync(Guid spaceId, IReadOnlyList<ImportConsumptionCsvRow> rows);
    Task<bool> UpdateConsumptionAsync(Guid id, Guid spaceId, string productName, string productBrand, string productType, decimal quantityGrams, DateTime consumedAt);
    Task<bool> DeleteConsumptionAsync(Guid id, Guid spaceId);
}
