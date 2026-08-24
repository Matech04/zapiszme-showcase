/** Parsowanie JSON ProblemDetails / ValidationProblemDetails z ciała błędu (ApiException, HttpClient + withFetch). */
export function parseAuthProblemJsonString(raw: string): Record<string, unknown> | null {
  const cleaned = raw.trim().replace(/^\uFEFF/, '');
  if (!cleaned.startsWith('{')) {
    return null;
  }
  try {
    return JSON.parse(cleaned) as Record<string, unknown>;
  } catch {
    return null;
  }
}
