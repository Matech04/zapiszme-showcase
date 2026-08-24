// @ts-check
import { defineConfig } from 'astro/config';
import svelte from '@astrojs/svelte';
import tailwindcss from '@tailwindcss/vite';

// Statyczny eksport: brak @astrojs/node. Dynamiczne URL /:slug rezerwacji
// obsługujemy w kliencie + fallback Caddy do /_app (patrz caddyfile).
// W trybie dev: plugin poniżej replikuje ten fallback — dowolny slug działa bez hardkodowania.
/** @returns {import('vite').Plugin} */
function bookingSlugDevFallback() {
  // Jednosegmentowe trasy Astro (poza slugiem rezerwacji) — inaczej dev przepisuje je na /_app.
  const RESERVED = new Set([
    '_app',
    '_astro',
    '_image', // endpoint astro:assets w dev — nie przepisuj na /_app (inaczej obrazki się nie ładują)
    'favicon.ico',
    'favicon.svg',
    'demo-kalendarz',
    'regulamin',
    'polityka-prywatnosci',
  ]);
  return {
    name: 'booking-slug-dev-fallback',
    configureServer(server) {
      server.middlewares.use((req, _res, next) => {
        // Connect typuje `req` jako IncomingMessage; bez @types/node stub nie ma `url`.
        const r = /** @type {{ url?: string }} */ (req);
        const url = r.url ?? '/';
        const pathname = url.split('?')[0].split('#')[0];
        const segments = pathname.split('/').filter(Boolean);
        if (segments.length === 1 && !segments[0].includes('.') && !RESERVED.has(segments[0])) {
          r.url = '/_app' + url.slice(pathname.length);
        }
        next();
      });
    },
  };
}

export default defineConfig({
  output: 'static',
  devToolbar: { enabled: false },
  integrations: [svelte()],
  vite: {
    plugins: [tailwindcss(), bookingSlugDevFallback()]
  }
});
