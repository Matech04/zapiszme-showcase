namespace App.Domain.Aggregates.PromoCodeAggregate;

/// <summary>Pochodzenie / przeznaczenie kodu promocyjnego (semantyka biznesowa, nie wpływa na obliczanie rabatu).</summary>
public enum PromoCodeKind
{
  Influencer = 0,
  Referral = 1,
  AdminIssued = 2,
}
