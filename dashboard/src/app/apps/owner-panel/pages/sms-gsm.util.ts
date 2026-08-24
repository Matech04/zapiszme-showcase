/**
 * Wykrywanie kodowania SMS po stronie UI — spójne z backendem (`SmsLengthCalculator`).
 * Treść z choćby jednym znakiem spoza GSM 03.38 → UCS-2 (limit 70); inaczej GSM-7 (próg 140).
 */

const GSM_BASIC =
  '@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !"#¤%&\'()*+,-./0123456789:;<=>?' +
  '¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà';

const GSM_EXTENSION = '^{}\\[~]|€';

const GSM_SET = new Set([...GSM_BASIC, ...GSM_EXTENSION]);

export const GSM_LIMIT = 140;
export const UNICODE_LIMIT = 70;

/** True, gdy treść wymusza UCS-2 (np. polskie ogonki) → limit 70. */
export function gsmRequiresUnicode(text: string): boolean {
  for (const ch of text ?? '') {
    if (!GSM_SET.has(ch)) {
      return true;
    }
  }
  return false;
}

/** Limit znaków dla danej treści (140 GSM-7 / 70 UCS-2). */
export function smsLimit(text: string): number {
  return gsmRequiresUnicode(text) ? UNICODE_LIMIT : GSM_LIMIT;
}
