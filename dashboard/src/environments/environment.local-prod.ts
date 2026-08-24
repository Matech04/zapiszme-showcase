/**
 * Local-prod: ten sam Production-mode Angular build, ale celuje w API/Turnstile lokalne
 * (docker-compose.prod.local.yml + caddyfile.local). Wybierane przez `--configuration=local-prod`
 * — `fileReplacements` w angular.json mapuje `environment.ts` na ten plik.
 */
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.localhost',
  bookingBaseUrl: 'https://localhost',
  // Cloudflare Turnstile public TEST site key — zawsze pass, ale tylko z origin localhost.
  // Dopasowane do Turnstile__SiteKey ustawianego w `.env.local`.
  turnstileSiteKey: '1x00000000000000000000AA',
};
