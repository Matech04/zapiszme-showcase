using App.Domain.Common;

namespace App.Application.Services.Dtos;

/// <summary>
/// DTO usługi zwracane przez GetById/List. <see cref="EmployeeIds"/> to lista
/// aktualnie przypisanych pracowników — używane przez formularz UI do
/// pre-checkowania multi-selecta i wykrycia zmian przy zapisie.
/// </summary>
public record ServiceDto(
    Guid Id,
    Guid? CategoryId,
    Guid VatRateId,
    string Name,
    Money Price,
    int DurationInMinutes,
    List<Guid> EmployeeIds,
    decimal? MaxAmount = null,
    int? DurationMinMinutes = null,
    int? DurationMaxMinutes = null,
    string? ComboGroup = null,
    bool HidePrice = false,
    int OrderIndex = 0,
    bool IsAddon = false,
    List<Guid>? AddonServiceIds = null,
    string? Description = null,
    List<ServiceImageDto>? Images = null);
