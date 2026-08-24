public record HoldLease
{
  public Guid ReservationToken { get; }
  public DateTime ExpiryTimeUtc { get; }

  public HoldLease(Guid reservationToken, DateTime expiryTimeUtc)
  {
    ReservationToken = reservationToken;
    ExpiryTimeUtc = expiryTimeUtc;
  }

  public bool IsExpired() => DateTime.UtcNow > ExpiryTimeUtc;

  public bool IsValid(Guid token)
  {
    if (!IsExpired() && ReservationToken == token)
    {
      return true;
    }
    else
    {
      return false;
    }
  }
}