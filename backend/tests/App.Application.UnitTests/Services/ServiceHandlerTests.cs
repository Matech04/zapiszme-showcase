using App.Application.Common.Interfaces;
using App.Application.Services.Commands;
using App.Application.ServiceCategories.Commands.CreateServiceCategory;
using MediatR;
using App.Application.ServiceCategories.Commands.DeleteServiceCategory;
using App.Application.ServiceCategories.Commands.UpdateServiceCategory;
using App.Application.Services.Commands.CreateService;
using App.Application.Services.Commands.DeleteService;
using App.Application.Services.Commands.ReorderServices;
using App.Application.Services.Commands.SetServiceEmployees;
using App.Application.Services.Commands.UpdateService;
using App.Application.ServiceCategories.Commands.ReorderServiceCategories;
using App.Application.Services.Queries.GetServiceById;
using App.Application.Services.Queries.GetServiceEmployees;
using App.Application.Services.Queries.GetServices;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Services;

/// <summary>
/// APP-SVC-001..018 — handlerowe testy Service / ServiceCategory.
/// </summary>
public sealed class ServiceHandlerTests
{
  // ── SVC-001 / SVC-003: Service CRUD ────────────────────────────────────────────────────

  [Fact]
  public async Task CreateService_persists_and_returns_guid()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var id = await handler.Handle(
      new CreateServiceCommand(categoryId, vatRateId, "Strzyżenie", 80m, "PLN", 30),
      ct);

    Assert.NotEqual(Guid.Empty, id);
    var saved = await db.Services.FindAsync(new object[] { id }, ct);
    Assert.NotNull(saved);
    Assert.Equal("Strzyżenie", saved!.Name);
    Assert.Equal(tenantId, saved.TenantId);
  }

  [Fact]
  public async Task UpdateService_persists_changes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Old", 50m, 30);

    var handler = BuildUpdateHandler(db, tenantId);
    await handler.Handle(
      new UpdateServiceCommand(service.Id, categoryId, vatRateId, "New", 120m, "PLN", 45),
      ct);

    var updated = await db.Services.FindAsync(new object[] { service.Id }, ct);
    Assert.Equal("New", updated!.Name);
    Assert.Equal(120m, updated.Price.Amount);
    Assert.Equal(45, updated.DurationInMinutes);
  }

  [Fact]
  public async Task UpdateService_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = BuildUpdateHandler(db, tenantId);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateServiceCommand(Guid.NewGuid(), categoryId, vatRateId, "X", 10m, "PLN", 10), ct));
  }

  [Fact]
  public async Task UpdateService_throws_NotFound_for_cross_tenant_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, Guid.NewGuid(), categoryId, vatRateId, "X", 10m, 10);

    var handler = BuildUpdateHandler(db, tenantId);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateServiceCommand(service.Id, categoryId, vatRateId, "Y", 10m, "PLN", 10), ct));
  }

  [Fact]
  public async Task DeleteService_soft_deletes_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "ToDelete", 10m, 10);

    var handler = new DeleteServiceHandler(new ServiceRepository(db), new FakeDeletionService(), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new DeleteServiceCommand(service.Id), ct);

    var deleted = await db.Services.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(s => s.Id == service.Id, ct);
    Assert.False(deleted.IsActive);
  }

  [Fact]
  public async Task DeleteService_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new DeleteServiceHandler(new ServiceRepository(db), new FakeDeletionService(), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteServiceCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task DeleteService_throws_NotFound_for_cross_tenant_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, Guid.NewGuid(), categoryId, vatRateId, "Cross", 10m, 10);

    var handler = new DeleteServiceHandler(new ServiceRepository(db), new FakeDeletionService(), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteServiceCommand(service.Id), ct));
  }

  // ── Widełki cen + przedział czasu (Create/Update) ──────────────────────────────────────

  [Fact]
  public async Task CreateService_persists_price_and_duration_ranges()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var id = await handler.Handle(
      new CreateServiceCommand(categoryId, vatRateId, "Przedłużanie", 100m, "PLN", 75,
        EmployeeIds: null, MaxAmount: 150m, DurationMinMinutes: 60, DurationMaxMinutes: 90),
      ct);

    var saved = await db.Services.FindAsync(new object[] { id }, ct);
    Assert.Equal(150m, saved!.PriceMaxAmount);
    Assert.Equal(60, saved.DurationMinMinutes);
    Assert.Equal(90, saved.DurationMaxMinutes);
    Assert.Equal(75, saved.DurationInMinutes);
  }

  [Fact]
  public async Task UpdateService_persists_ranges_and_GetById_projects_them()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Old", 50m, 60);

    var handler = BuildUpdateHandler(db, tenantId);
    await handler.Handle(
      new UpdateServiceCommand(service.Id, categoryId, vatRateId, "New", 100m, "PLN", 75,
        EmployeeIds: null, MaxAmount: 200m, DurationMinMinutes: 60, DurationMaxMinutes: 120),
      ct);

    var query = new GetServiceByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var dto = await query.Handle(new GetServiceByIdQuery(service.Id), ct);

    Assert.Equal(200m, dto.MaxAmount);
    Assert.Equal(60, dto.DurationMinMinutes);
    Assert.Equal(120, dto.DurationMaxMinutes);
  }

  [Fact]
  public async Task UpdateService_without_ranges_leaves_them_null()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Fixed", 50m, 60);

    var handler = BuildUpdateHandler(db, tenantId);
    await handler.Handle(new UpdateServiceCommand(service.Id, categoryId, vatRateId, "Fixed", 50m, "PLN", 60), ct);

    var saved = await db.Services.FindAsync(new object[] { service.Id }, ct);
    Assert.Null(saved!.PriceMaxAmount);
    Assert.Null(saved.DurationMinMinutes);
    Assert.Null(saved.DurationMaxMinutes);
  }

  // ── Media: opis + galeria zdjęć (#5) ───────────────────────────────────────────────────

  [Fact]
  public async Task CreateService_persists_description_and_images()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var id = await handler.Handle(
      new CreateServiceCommand(categoryId, vatRateId, "Manicure", 100m, "PLN", 60,
        Description: "Pełna stylizacja",
        Images: new List<ServiceImageInput>
        {
          new("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
          new("https://cdn/b.webp", "https://cdn/b-t.webp", "services/b.webp"),
        }),
      ct);

    var saved = await db.Services.Include(s => s.Images).FirstAsync(s => s.Id == id, ct);
    Assert.Equal("Pełna stylizacja", saved.Description);
    var ordered = saved.Images.OrderBy(i => i.OrderIndex).ToList();
    Assert.Equal(2, ordered.Count);
    Assert.Equal("services/a.webp", ordered[0].StorageKey); // cover
  }

  [Fact]
  public async Task UpdateService_reconciles_gallery_and_cleans_storage_for_removed()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Svc", 50m, 60);
    // Wyjściowa galeria: a + b.
    service.SetImages(new[]
    {
      new ServiceImageData("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
      new ServiceImageData("https://cdn/b.webp", "https://cdn/b-t.webp", "services/b.webp"),
    });
    db.SaveChanges();

    var storage = new FakeFileStorage();
    var handler = BuildUpdateHandler(db, tenantId, storage);
    // Reconcile: zostaw 'a', dołóż 'c' → 'b' usunięte z galerii i ze storage.
    await handler.Handle(
      new UpdateServiceCommand(service.Id, categoryId, vatRateId, "Svc", 50m, "PLN", 60,
        Images: new List<ServiceImageInput>
        {
          new("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
          new("https://cdn/c.webp", "https://cdn/c-t.webp", "services/c.webp"),
        }),
      ct);

    var saved = await db.Services.Include(s => s.Images).FirstAsync(s => s.Id == service.Id, ct);
    Assert.Equal(2, saved.Images.Count);
    Assert.DoesNotContain(saved.Images, i => i.StorageKey == "services/b.webp");
    Assert.Equal(new[] { "services/b.webp" }, storage.DeletedKeys);
  }

  [Fact]
  public async Task UpdateService_with_null_images_leaves_gallery_untouched()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Svc", 50m, 60);
    service.SetImages(new[] { new ServiceImageData("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp") });
    db.SaveChanges();

    var handler = BuildUpdateHandler(db, tenantId);
    // Images = null (domyślne) → galeria nietknięta (back-compat).
    await handler.Handle(new UpdateServiceCommand(service.Id, categoryId, vatRateId, "Svc2", 50m, "PLN", 60), ct);

    var saved = await db.Services.Include(s => s.Images).FirstAsync(s => s.Id == service.Id, ct);
    Assert.Single(saved.Images);
  }

  // ── SVC-002 / SVC-004: ServiceCategory CRUD ────────────────────────────────────────────

  [Fact]
  public async Task CreateServiceCategory_persists_and_returns_guid()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new CreateServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));

    var id = await handler.Handle(new CreateServiceCategoryCommand("Strzyżenia", 1), ct);

    Assert.NotEqual(Guid.Empty, id);
    var saved = await db.ServiceCategories.FindAsync(new object[] { id }, ct);
    Assert.Equal("Strzyżenia", saved!.Name);
  }

  [Fact]
  public async Task UpdateServiceCategory_persists_changes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, _) = SetupDb();

    var handler = new UpdateServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new UpdateServiceCategoryCommand(categoryId, "Renamed", 9), ct);

    var updated = await db.ServiceCategories.FindAsync(new object[] { categoryId }, ct);
    Assert.Equal("Renamed", updated!.Name);
    Assert.Equal(9, updated.OrderIndex);
  }

  [Fact]
  public async Task UpdateServiceCategory_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new UpdateServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateServiceCategoryCommand(Guid.NewGuid(), "x", 1), ct));
  }

  [Fact]
  public async Task UpdateServiceCategory_throws_NotFound_for_cross_tenant_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var otherCategory = new ServiceCategory(Guid.NewGuid(), "Other", 1);
    db.ServiceCategories.Add(otherCategory);
    db.SaveChanges();

    var handler = new UpdateServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateServiceCategoryCommand(otherCategory.Id, "x", 1), ct));
  }

  [Fact]
  public async Task DeleteServiceCategory_calls_deactivate_and_persists()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, _) = SetupDb();

    var handler = new DeleteServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new DeleteServiceCategoryCommand(categoryId), ct);

    var category = await db.ServiceCategories.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(c => c.Id == categoryId, ct);
    Assert.False(category.IsActive);
  }

  [Fact]
  public async Task DeleteServiceCategory_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new DeleteServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteServiceCategoryCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task DeleteServiceCategory_throws_NotFound_for_cross_tenant_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var otherCategory = new ServiceCategory(Guid.NewGuid(), "Other", 1);
    db.ServiceCategories.Add(otherCategory);
    db.SaveChanges();

    var handler = new DeleteServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteServiceCategoryCommand(otherCategory.Id), ct));
  }

  [Fact]
  public async Task DeleteServiceCategory_detaches_services_to_orphan_and_keeps_them_active()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var s1 = SeedService(db, tenantId, categoryId, vatRateId, "S1", 10m, 10);
    var s2 = SeedService(db, tenantId, categoryId, vatRateId, "S2", 20m, 20);

    var handler = new DeleteServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new DeleteServiceCategoryCommand(categoryId), ct);

    var reloaded1 = await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == s1.Id, ct);
    var reloaded2 = await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == s2.Id, ct);

    Assert.Null(reloaded1.CategoryId);
    Assert.True(reloaded1.IsActive);
    Assert.Null(reloaded2.CategoryId);
    Assert.True(reloaded2.IsActive);

    var category = await db.ServiceCategories.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == categoryId, ct);
    Assert.False(category.IsActive);
  }

  [Fact]
  public async Task DeleteServiceCategory_does_not_touch_services_of_other_categories()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryAId, vatRateId) = SetupDb();
    var categoryB = new ServiceCategory(tenantId, "B", 1);
    db.ServiceCategories.Add(categoryB);
    db.SaveChanges();

    var inA = SeedService(db, tenantId, categoryAId, vatRateId, "InA", 10m, 10);
    var inB = SeedService(db, tenantId, categoryB.Id, vatRateId, "InB", 10m, 10);

    var handler = new DeleteServiceCategoryHandler(new ServiceCategoryRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new DeleteServiceCategoryCommand(categoryAId), ct);

    var reloadedA = await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == inA.Id, ct);
    var reloadedB = await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == inB.Id, ct);

    Assert.Null(reloadedA.CategoryId);
    Assert.Equal(categoryB.Id, reloadedB.CategoryId);
  }

  // ── SVC-005: GetServices filter by category ─────────────────────────────────────────────

  [Fact]
  public async Task GetServices_filters_by_categoryId_when_provided()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryAId, vatRateId) = SetupDb();
    var categoryB = new ServiceCategory(tenantId, "Manicure", 2);
    db.ServiceCategories.Add(categoryB);
    db.SaveChanges();

    SeedService(db, tenantId, categoryAId, vatRateId, "A1", 10m, 10);
    SeedService(db, tenantId, categoryAId, vatRateId, "A2", 10m, 10);
    SeedService(db, tenantId, categoryB.Id, vatRateId, "B1", 10m, 10);

    var handler = new GetServicesHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetServicesQuery(categoryB.Id), ct);

    Assert.Single(result);
    Assert.Equal("B1", result[0].Name);
  }

  [Fact]
  public async Task GetServices_returns_all_when_categoryId_is_null()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryAId, vatRateId) = SetupDb();
    SeedService(db, tenantId, categoryAId, vatRateId, "A1", 10m, 10);
    SeedService(db, tenantId, categoryAId, vatRateId, "A2", 10m, 10);

    var handler = new GetServicesHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetServicesQuery(null), ct);

    Assert.Equal(2, result.Count);
  }

  // ── SVC-018: ServiceDto projection completeness ─────────────────────────────────────────
  // Regresja: ServiceDto musi nieść WSZYSTKIE pola wymagane przez UpdateServiceCommand,
  // żeby formularz edycji mógł odesłać payload z powrotem bez utraty informacji.
  // Wcześniej brakowało VatRateId — frontowy required() blokował zapis (niedostępny VatRate).

  [Fact]
  public async Task GetServiceById_projects_VatRateId()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "X", 10m, 10);

    var handler = new GetServiceByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var dto = await handler.Handle(new GetServiceByIdQuery(service.Id), ct);

    Assert.Equal(vatRateId, dto.VatRateId);
  }

  [Fact]
  public async Task GetServiceById_returns_all_fields_required_by_UpdateServiceCommand()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Strzyżenie", 80m, 30);

    var handler = new GetServiceByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var dto = await handler.Handle(new GetServiceByIdQuery(service.Id), ct);

    Assert.Equal(service.Id, dto.Id);
    Assert.Equal(categoryId, dto.CategoryId);
    Assert.Equal(vatRateId, dto.VatRateId);
    Assert.Equal("Strzyżenie", dto.Name);
    Assert.Equal(80m, dto.Price.Amount);
    Assert.Equal("PLN", dto.Price.Currency);
    Assert.Equal(30, dto.DurationInMinutes);
  }

  [Fact]
  public async Task GetServices_projects_VatRateId()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    SeedService(db, tenantId, categoryId, vatRateId, "A1", 10m, 10);

    var handler = new GetServicesHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetServicesQuery(null), ct);

    Assert.Single(result);
    Assert.Equal(vatRateId, result[0].VatRateId);
  }

  // ── SVC-012: same-name edge cases ───────────────────────────────────────────────────────

  [Fact]
  public async Task CreateService_allows_two_services_with_same_name_in_same_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var first = await handler.Handle(new CreateServiceCommand(categoryId, vatRateId, "Strzyżenie", 80m, "PLN", 30), ct);
    var second = await handler.Handle(new CreateServiceCommand(categoryId, vatRateId, "Strzyżenie", 80m, "PLN", 30), ct);

    Assert.NotEqual(first, second);
    Assert.Equal(2, await db.Services.CountAsync(ct));
  }

  [Fact]
  public async Task CreateService_allows_same_name_across_different_tenants()
  {
    var ct = TestContext.Current.CancellationToken;
    var (dbA, tenantA, categoryA, vatRateA) = SetupDb();

    var handlerA = new CreateServiceHandler(new ServiceRepository(dbA), dbA, dbA, new FakeCurrentTenantService(tenantA), NoOpSender.Instance);
    var idA = await handlerA.Handle(new CreateServiceCommand(categoryA, vatRateA, "Strzyżenie", 80m, "PLN", 30), ct);

    var (dbB, tenantB, categoryB, vatRateB) = SetupDb();
    var handlerB = new CreateServiceHandler(new ServiceRepository(dbB), dbB, dbB, new FakeCurrentTenantService(tenantB), NoOpSender.Instance);
    var idB = await handlerB.Handle(new CreateServiceCommand(categoryB, vatRateB, "Strzyżenie", 80m, "PLN", 30), ct);

    Assert.NotEqual(idA, idB);
    var aCount = await dbA.Services.IgnoreQueryFilters().CountAsync(s => s.Name == "Strzyżenie", ct);
    var bCount = await dbB.Services.IgnoreQueryFilters().CountAsync(s => s.Name == "Strzyżenie", ct);
    Assert.Equal(1, aCount);
    Assert.Equal(1, bCount);
  }

  // ── SVC-019: GetServiceEmployees ───────────────────────────────────────────────────────

  [Fact(Skip="EF InMemory provider nie supportuje owned types z FK (EmployeeService.CustomPrice#Money). Logika handler-a verifikowana w integration tests Testcontainers.")]
  public async Task GetServiceEmployees_returns_ids_of_assigned_employees_only()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Manicure", 50m, 30);
    var assigned1 = SeedEmployeeWithService(db, tenantId, service);
    var assigned2 = SeedEmployeeWithService(db, tenantId, service);
    var unassigned = SeedEmployee(db, tenantId, "Free", "Agent");

    var handler = new GetServiceEmployeesHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetServiceEmployeesQuery(service.Id), ct);

    Assert.Equal(2, result.Count);
    Assert.Contains(assigned1.Id, result);
    Assert.Contains(assigned2.Id, result);
    Assert.DoesNotContain(unassigned.Id, result);
  }

  [Fact]
  public async Task GetServiceEmployees_throws_NotFound_for_unknown_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new GetServiceEmployeesHandler(db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new GetServiceEmployeesQuery(Guid.NewGuid()), ct));
  }

  // ── SVC-020: SetServiceEmployees ───────────────────────────────────────────────────────

  [Fact(Skip="EF InMemory provider nie supportuje owned types z FK (EmployeeService.CustomPrice#Money). Logika handler-a verifikowana w integration tests Testcontainers.")]
  public async Task SetServiceEmployees_assigns_new_employees_inheriting_catalog()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Brwi", 80m, 45);
    var emp1 = SeedEmployee(db, tenantId, "Ann", "Smith");
    var emp2 = SeedEmployee(db, tenantId, "Bob", "Jones");

    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new SetServiceEmployeesCommand(service.Id, new List<Guid> { emp1.Id, emp2.Id }), ct);

    var reloaded = await db.Employees.Include(e => e.Services).ToListAsync(ct);
    foreach (var e in reloaded)
    {
      var link = Assert.Single(e.Services);
      Assert.Equal(service.Id, link.ServiceId);
      // Przypisanie z formularza usługi NIE jest override'em — dziedziczy czas i cenę z katalogu (null).
      Assert.Null(link.CustomDuration);
      Assert.Null(link.CustomPrice);
    }
  }

  [Fact(Skip="EF InMemory provider nie supportuje owned types z FK (EmployeeService.CustomPrice#Money). Logika handler-a verifikowana w integration tests Testcontainers.")]
  public async Task SetServiceEmployees_unassigns_employees_not_in_list()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "X", 10m, 10);
    var keep = SeedEmployeeWithService(db, tenantId, service);
    var drop = SeedEmployeeWithService(db, tenantId, service);

    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new SetServiceEmployeesCommand(service.Id, new List<Guid> { keep.Id }), ct);

    var keepLinks = await db.Set<EmployeeService>().Where(s => s.EmployeeId == keep.Id).CountAsync(ct);
    var dropLinks = await db.Set<EmployeeService>().Where(s => s.EmployeeId == drop.Id).CountAsync(ct);
    Assert.Equal(1, keepLinks);
    Assert.Equal(0, dropLinks);
  }

  [Fact(Skip="EF InMemory provider nie supportuje owned types z FK (EmployeeService.CustomPrice#Money). Logika handler-a verifikowana w integration tests Testcontainers.")]
  public async Task SetServiceEmployees_preserves_custom_values_for_existing_assignments()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Y", 100m, 60);
    var emp = SeedEmployee(db, tenantId, "Cara", "Lee");
    emp.AssignService(tenantId, service.Id, customDuration: 30, customPrice: new Money(150m, "PLN"));
    db.SaveChanges();

    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new SetServiceEmployeesCommand(service.Id, new List<Guid> { emp.Id }), ct);

    var link = await db.Set<EmployeeService>().SingleAsync(s => s.EmployeeId == emp.Id, ct);
    Assert.Equal(30, link.CustomDuration);
    Assert.Equal(150m, link.CustomPrice!.Amount);
  }

  [Fact(Skip="EF InMemory provider nie supportuje owned types z FK (EmployeeService.CustomPrice#Money). Logika handler-a verifikowana w integration tests Testcontainers.")]
  public async Task SetServiceEmployees_with_empty_list_unassigns_all()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Z", 10m, 10);
    SeedEmployeeWithService(db, tenantId, service);
    SeedEmployeeWithService(db, tenantId, service);

    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new SetServiceEmployeesCommand(service.Id, new List<Guid>()), ct);

    var remaining = await db.Set<EmployeeService>().CountAsync(s => s.ServiceId == service.Id, ct);
    Assert.Equal(0, remaining);
  }

  [Fact]
  public async Task SetServiceEmployees_throws_NotFound_for_unknown_employee()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "X", 10m, 10);

    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetServiceEmployeesCommand(service.Id, new List<Guid> { Guid.NewGuid() }), ct));
  }

  [Fact]
  public async Task SetServiceEmployees_throws_NotFound_for_unknown_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupDb();
    var handler = new SetServiceEmployeesHandler(db, db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetServiceEmployeesCommand(Guid.NewGuid(), new List<Guid>()), ct));
  }

  // ── HidePrice (Create/Update/projection) ───────────────────────────────────────────────

  [Fact]
  public async Task CreateService_persists_HidePrice_flag()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var id = await handler.Handle(
      new CreateServiceCommand(categoryId, vatRateId, "Konsultacja", 0m, "PLN", 15, HidePrice: true),
      ct);

    var saved = await db.Services.FindAsync(new object[] { id }, ct);
    Assert.True(saved!.HidePrice);
    // Cena 0 nadal dozwolona (GreaterThanOrEqualTo(0)).
    Assert.Equal(0m, saved.Price.Amount);
  }

  [Fact]
  public async Task CreateService_defaults_HidePrice_false()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var handler = new CreateServiceHandler(new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance);

    var id = await handler.Handle(new CreateServiceCommand(categoryId, vatRateId, "Strzyżenie", 80m, "PLN", 30), ct);

    var saved = await db.Services.FindAsync(new object[] { id }, ct);
    Assert.False(saved!.HidePrice);
  }

  [Fact]
  public async Task UpdateService_persists_HidePrice_and_GetById_projects_it()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var service = SeedService(db, tenantId, categoryId, vatRateId, "Old", 50m, 30);

    var handler = BuildUpdateHandler(db, tenantId);
    await handler.Handle(
      new UpdateServiceCommand(service.Id, categoryId, vatRateId, "New", 60m, "PLN", 30, HidePrice: true),
      ct);

    var query = new App.Application.Services.Queries.GetServiceById.GetServiceByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var dto = await query.Handle(new App.Application.Services.Queries.GetServiceById.GetServiceByIdQuery(service.Id), ct);
    Assert.True(dto.HidePrice);
  }

  // ── OrderIndex + Reorder (services) ─────────────────────────────────────────────────────

  [Fact]
  public async Task ReorderServices_assigns_OrderIndex_by_list_position()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var a = SeedService(db, tenantId, categoryId, vatRateId, "A", 10m, 10);
    var b = SeedService(db, tenantId, categoryId, vatRateId, "B", 10m, 10);
    var c = SeedService(db, tenantId, categoryId, vatRateId, "C", 10m, 10);

    var handler = new ReorderServicesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new ReorderServicesCommand(new List<Guid> { c.Id, a.Id, b.Id }, categoryId), ct);

    Assert.Equal(1, (await db.Services.FindAsync(new object[] { a.Id }, ct))!.OrderIndex);
    Assert.Equal(2, (await db.Services.FindAsync(new object[] { b.Id }, ct))!.OrderIndex);
    Assert.Equal(0, (await db.Services.FindAsync(new object[] { c.Id }, ct))!.OrderIndex);
  }

  [Fact]
  public async Task GetServices_orders_by_OrderIndex_then_name()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var a = SeedService(db, tenantId, categoryId, vatRateId, "Alpha", 10m, 10);
    var z = SeedService(db, tenantId, categoryId, vatRateId, "Zeta", 10m, 10);

    // Zeta ma niższy OrderIndex → powinna być pierwsza mimo nazwy.
    new ReorderServicesHandler(db, db, new FakeCurrentTenantService(tenantId))
      .Handle(new ReorderServicesCommand(new List<Guid> { z.Id, a.Id }, categoryId), ct).GetAwaiter().GetResult();

    var result = await new GetServicesHandler(db, new FakeCurrentTenantService(tenantId))
      .Handle(new GetServicesQuery(null), ct);

    Assert.Equal("Zeta", result[0].Name);
    Assert.Equal("Alpha", result[1].Name);
  }

  [Fact]
  public async Task ReorderServices_rejects_id_from_another_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var mine = SeedService(db, tenantId, categoryId, vatRateId, "Mine", 10m, 10);
    // Usługa innego tenanta — globalny query filter ją wyciska, więc handler jej nie znajdzie → NotFound.
    var other = SeedService(db, Guid.NewGuid(), categoryId, vatRateId, "Other", 10m, 10);

    var handler = new ReorderServicesHandler(db, db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ReorderServicesCommand(new List<Guid> { mine.Id, other.Id }, categoryId), ct));
  }

  [Fact]
  public async Task ReorderServices_rejects_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, vatRateId) = SetupDb();
    var mine = SeedService(db, tenantId, categoryId, vatRateId, "Mine", 10m, 10);

    var handler = new ReorderServicesHandler(db, db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ReorderServicesCommand(new List<Guid> { mine.Id, Guid.NewGuid() }, categoryId), ct));
  }

  // ── Reorder (categories) ────────────────────────────────────────────────────────────────

  [Fact]
  public async Task ReorderServiceCategories_assigns_OrderIndex_and_preserves_name()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, firstCategoryId, _) = SetupDb();
    var second = new ServiceCategory(tenantId, "Druga", 5);
    var third = new ServiceCategory(tenantId, "Trzecia", 9);
    db.ServiceCategories.AddRange(second, third);
    db.SaveChanges();

    var handler = new ReorderServiceCategoriesHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new ReorderServiceCategoriesCommand(new List<Guid> { third.Id, firstCategoryId, second.Id }), ct);

    var reloadedThird = await db.ServiceCategories.FindAsync(new object[] { third.Id }, ct);
    Assert.Equal(0, reloadedThird!.OrderIndex);
    Assert.Equal("Trzecia", reloadedThird.Name); // nazwa zachowana
    Assert.Equal(1, (await db.ServiceCategories.FindAsync(new object[] { firstCategoryId }, ct))!.OrderIndex);
    Assert.Equal(2, (await db.ServiceCategories.FindAsync(new object[] { second.Id }, ct))!.OrderIndex);
  }

  [Fact]
  public async Task ReorderServiceCategories_rejects_id_from_another_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, categoryId, _) = SetupDb();
    var other = new ServiceCategory(Guid.NewGuid(), "Obca", 0);
    db.ServiceCategories.Add(other);
    db.SaveChanges();

    var handler = new ReorderServiceCategoriesHandler(db, db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ReorderServiceCategoriesCommand(new List<Guid> { categoryId, other.Id }), ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static (ApplicationDbContext db, Guid tenantId, Guid categoryId, Guid vatRateId) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var category = new ServiceCategory(tenantId, "Cat", 0);
    var vat = new VatRate(tenantId, "VAT", 0.23m);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.SaveChanges();
    return (db, tenantId, category.Id, vat.Id);
  }

  private static Service SeedService(
    ApplicationDbContext db,
    Guid tenantId,
    Guid categoryId,
    Guid vatRateId,
    string name,
    decimal price,
    int duration)
  {
    var s = new Service(tenantId, categoryId, vatRateId, name, new Money(price, "PLN"), duration);
    db.Services.Add(s);
    db.SaveChanges();
    return s;
  }

  private static Employee SeedEmployee(ApplicationDbContext db, Guid tenantId, string firstName, string lastName)
  {
    var e = new Employee(tenantId, null, firstName, lastName, $"{firstName}.{lastName}@svc.local".ToLowerInvariant());
    db.Employees.Add(e);
    db.SaveChanges();
    return e;
  }

  private static Employee SeedEmployeeWithService(ApplicationDbContext db, Guid tenantId, Service service)
  {
    var e = SeedEmployee(db, tenantId, $"Emp{Guid.NewGuid():N}".Substring(0, 8), "Test");
    e.AssignService(tenantId, service.Id, service.DurationInMinutes, service.Price);
    db.SaveChanges();
    return e;
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  /// <summary>Zbiera klucze, które handler usuwa ze storage (best-effort cleanup zdjęć usługi).</summary>
  private sealed class FakeFileStorage : IFileStorage
  {
    public List<string> DeletedKeys { get; } = new();
    public Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default)
      => Task.FromResult($"https://cdn.test/{key}");
    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
      DeletedKeys.Add(key);
      return Task.CompletedTask;
    }
    public string BuildPublicUrl(string key) => $"https://cdn.test/{key}";
  }

  private static UpdateServiceHandler BuildUpdateHandler(
    ApplicationDbContext db, Guid tenantId, IFileStorage? storage = null)
    => new(
      new ServiceRepository(db), db, db, new FakeCurrentTenantService(tenantId), NoOpSender.Instance,
      storage ?? new FakeFileStorage(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateServiceHandler>.Instance);

  private sealed class FakeDeletionService : IDeletionService
  {
    public Task DeleteAsync<TEntity>(TEntity entity, CancellationToken ct = default)
      where TEntity : Entity, ISoftDelete, ITenantEntity
    {
      entity.Deactivate();
      return Task.CompletedTask;
    }
  }

  /// <summary>
  /// Pusty <see cref="ISender"/> dla testów Create/UpdateService — handlery wołają
  /// <c>_mediator.Send(SetServiceEmployeesCommand)</c> tylko gdy <c>EmployeeIds</c>
  /// jest podane. Testy nie podają, więc Send nie jest wywoływane — NoOp wystarcza.
  /// </summary>
  private sealed class NoOpSender : ISender
  {
    public static readonly NoOpSender Instance = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
      => throw new InvalidOperationException("NoOpSender.Send<TResponse> nie powinien zostać wywołany w teście Create/UpdateService.");

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
      => Task.CompletedTask;

    public Task<object?> Send(object request, CancellationToken ct = default)
      => Task.FromResult<object?>(null);

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
      => throw new NotImplementedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
      => throw new NotImplementedException();
  }
}
