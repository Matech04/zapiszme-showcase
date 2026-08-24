namespace App.Application.Feedback;

public interface IFeedbackSender
{
  Task SendAsync(
    string kind,
    string title,
    string description,
    string? pageUrl,
    string? userEmail,
    string salonName,
    CancellationToken ct = default);
}
