using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
  private readonly IApplicationDbContext _context;

  public EmployeeRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(Employee employee) => await _context.Employees.AddAsync(employee);

  /// <summary>Numer porządkowy miesiąca — pozwala porównać (rok, miesiąc) jednym wyrażeniem po stronie SQL.</summary>
  private static int MonthOrdinal(DateOnly date) => (date.Year * 12) + date.Month;

  public async Task<Employee?> GetByIdAsync(Guid id)
  {
    return await _context.Employees
        .AsSplitQuery()
        .Include(e => e.Services)
        .Include(e => e.Schedules)
        .ThenInclude(s => s.ScheduleDays)
        .Include(e => e.Overrides)
        .ThenInclude(o => o.ScheduleDay)
        .Include(e => e.Leaves)
        // Bez tego SetMonthPublication widzi pustą kolekcję i zawsze dodaje nowy wiersz,
        // co przy istniejącym wpisie łamie unikalny indeks (employee_id, year, month).
        .Include(e => e.MonthPublications)
        .FirstOrDefaultAsync(e => e.Id == id);
  }

  public async Task<Employee?> GetForAvailabilityAsync(Guid id, DateOnly from, DateOnly to, CancellationToken ct)
  {
    var fromMonth = MonthOrdinal(from);
    var toMonth = MonthOrdinal(to);

    return await _context.Employees
        .AsSplitQuery()
        .Include(e => e.Services)
        .Include(e => e.Schedules)
        .ThenInclude(s => s.ScheduleDays)
        // Override po dacie w zakresie; Leave po przecięciu zakresu (overlap),
        // by urlop obejmujący [from,to] również się załapał.
        .Include(e => e.Overrides.Where(o => o.Date >= from && o.Date <= to))
        .ThenInclude(o => o.ScheduleDay)
        .Include(e => e.Leaves.Where(l => l.DateRange.StartDate <= to && l.DateRange.EndDate >= from))
        .Include(e => e.MonthPublications.Where(p =>
            ((p.Year * 12) + p.Month) >= fromMonth && ((p.Year * 12) + p.Month) <= toMonth))
        .FirstOrDefaultAsync(e => e.Id == id, ct);
  }

  public async Task<List<Employee>> GetManyForAvailabilityAsync(
      IReadOnlyList<Guid> ids, DateOnly from, DateOnly to, CancellationToken ct)
  {
    if (ids.Count == 0)
    {
      return new List<Employee>();
    }

    var fromMonth = MonthOrdinal(from);
    var toMonth = MonthOrdinal(to);

    return await _context.Employees
        .AsNoTracking()
        .AsSplitQuery()
        .Include(e => e.Schedules)
        .ThenInclude(s => s.ScheduleDays)
        .Include(e => e.Overrides.Where(o => o.Date >= from && o.Date <= to))
        .ThenInclude(o => o.ScheduleDay)
        .Include(e => e.Leaves.Where(l => l.DateRange.StartDate <= to && l.DateRange.EndDate >= from))
        .Include(e => e.MonthPublications.Where(p =>
            ((p.Year * 12) + p.Month) >= fromMonth && ((p.Year * 12) + p.Month) <= toMonth))
        .Where(e => ids.Contains(e.Id))
        .ToListAsync(ct);
  }

  public async Task<Employee?> GetByIdWithLeavesAsync(Guid id)
  {
    // AsSplitQuery jak w trzech metodach wyżej — nazwa metody myli, bo `Include(e => e.Leaves)`
    // NIE zawęża tu niczego: kolekcje owned (Services, Schedules→ScheduleDays→WorkRanges/Breaks,
    // Overrides→ScheduleDay→…, MonthPublications) EF ładuje ZAWSZE z rodzicem. Bez rozbicia
    // wszystkie mnożyły się w jednym SELECT — zmierzone na produkcji dla salonu z 21 usługami,
    // 12 urlopami i 33 nadpisaniami: 8 316 wierszy po 551 B (~4,6 MB) na wczytanie JEDNEGO
    // pracownika. Sama baza robi to w 2 ms; koszt to transfer i materializacja po stronie API.
    return await _context.Employees
        .AsSplitQuery()
        .Include(e => e.Leaves)
        .FirstOrDefaultAsync(e => e.Id == id);
  }

  public void Update(Employee employee) => _context.Employees.Update(employee);
  public void Remove(Employee employee) => _context.Employees.Remove(employee);
}