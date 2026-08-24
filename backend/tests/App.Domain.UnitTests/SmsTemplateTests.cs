using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

/// <summary>SMS-TPL-* — maszyna stanów akceptacji custom szablonu SMS.</summary>
public sealed class SmsTemplateTests
{
  private static readonly DateTime Now = new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);

  private static SmsTemplate New() =>
    SmsTemplate.Create(Guid.NewGuid(), NotificationType.BookingConfirmationToCustomer);

  [Fact]
  public void Create_StartsInNoneStatus()
  {
    var t = New();
    Assert.Equal(SmsTemplateStatus.None, t.Status);
    Assert.Null(t.Body);
    Assert.Null(t.PendingBody);
    Assert.False(t.HasApprovedBody);
  }

  [Fact]
  public void Submit_MovesToPendingApproval_AndKeepsPreviousApprovedBody()
  {
    var t = New();
    t.SubmitForApproval("Pierwsza tresc {salon}", Now);
    t.Approve(Now);
    Assert.True(t.HasApprovedBody);

    // Druga edycja — zatwierdzona treść pozostaje aktywna do akceptacji nowej.
    t.SubmitForApproval("Druga tresc {salon}", Now);
    Assert.Equal(SmsTemplateStatus.PendingApproval, t.Status);
    Assert.Equal("Druga tresc {salon}", t.PendingBody);
    Assert.Equal("Pierwsza tresc {salon}", t.Body);
    Assert.True(t.HasApprovedBody); // poprzednia wciąż aktywna
  }

  [Fact]
  public void Approve_PromotesPendingToBody()
  {
    var t = New();
    t.SubmitForApproval("Tresc {salon}", Now);
    t.Approve(Now);

    Assert.Equal(SmsTemplateStatus.Approved, t.Status);
    Assert.Equal("Tresc {salon}", t.Body);
    Assert.Null(t.PendingBody);
    Assert.True(t.HasApprovedBody);
    Assert.NotNull(t.ReviewedAtUtc);
  }

  [Fact]
  public void Reject_SetsRejectedAndReason_KeepsApprovedBody()
  {
    var t = New();
    t.SubmitForApproval("Pierwsza {salon}", Now);
    t.Approve(Now);
    t.SubmitForApproval("Spam http://zly.link", Now);

    t.Reject("Niedozwolony link", Now);

    Assert.Equal(SmsTemplateStatus.Rejected, t.Status);
    Assert.Equal("Niedozwolony link", t.RejectionReason);
    Assert.Null(t.PendingBody);
    Assert.Equal("Pierwsza {salon}", t.Body); // poprzednia zatwierdzona bez zmian
    Assert.True(t.HasApprovedBody);
  }

  [Fact]
  public void Submit_EmptyBody_Throws()
  {
    var t = New();
    Assert.Throws<ArgumentException>(() => t.SubmitForApproval("   ", Now));
  }

  [Fact]
  public void Approve_WithoutPending_Throws()
  {
    var t = New();
    Assert.Throws<InvalidOperationException>(() => t.Approve(Now));
  }

  [Fact]
  public void Reject_WithoutPending_Throws()
  {
    var t = New();
    Assert.Throws<InvalidOperationException>(() => t.Reject("x", Now));
  }

  [Fact]
  public void Submit_AfterReject_ClearsRejectionReason()
  {
    var t = New();
    t.SubmitForApproval("a {salon}", Now);
    t.Reject("zly", Now);

    t.SubmitForApproval("b {salon}", Now);

    Assert.Equal(SmsTemplateStatus.PendingApproval, t.Status);
    Assert.Null(t.RejectionReason);
  }
}
