using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.ImpersonationAggregate;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using Microsoft.EntityFrameworkCore;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.ShortLinkAggregate;
using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.GlobalSettingsAggregate;

namespace App.Application.Common.Interfaces;

//This interface represents what tables we have in our database and a method to confirm changes

public interface IApplicationDbContext
{
  DbSet<Tenant> Tenants { get; }
  DbSet<Employee> Employees { get; }
  DbSet<Customer> Customers { get; }
  DbSet<Service> Services { get; }
  DbSet<ServiceCategory> ServiceCategories { get; }
  DbSet<User> Users { get; }
  DbSet<UserGuideCompletion> UserGuideCompletions { get; }
  DbSet<VatRate> VatRates { get; }
  DbSet<Appointment> Appointments { get; }
  DbSet<ShiftTemplate> ShiftTemplates { get; }
  DbSet<SelfServiceOtp> SelfServiceOtps { get; }
  DbSet<PhoneConfirmationOtp> PhoneConfirmationOtps { get; }
  DbSet<Notification> Notifications { get; }
  DbSet<NotificationUsageRecord> NotificationUsage { get; }
  DbSet<PushSubscription> PushSubscriptions { get; }
  DbSet<PromoCode> PromoCodes { get; }
  DbSet<PromoCodeRedemption> PromoCodeRedemptions { get; }
  DbSet<ImpersonationSession> ImpersonationSessions { get; }
  DbSet<ShortLink> ShortLinks { get; }
  DbSet<SmsTemplate> SmsTemplates { get; }
  DbSet<GlobalSettings> GlobalSettings { get; }
  DbSet<TEntity> Set<TEntity>() where TEntity : class;
  Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
