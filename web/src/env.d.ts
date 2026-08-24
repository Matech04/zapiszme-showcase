/// <reference types="astro/client" />

interface ImportMetaEnv {
	readonly PUBLIC_API_BASE_URL?: string;
	readonly PUBLIC_DASHBOARD_URL?: string;
	readonly PUBLIC_SITE_URL?: string;
	/**
	 * Cloudflare Turnstile site-key dla widgetu w trybie **Invisible** (osobny widget
	 * w CF dashboard niż Managed używany przez dashboard/login/register). Publiczny —
	 * widoczny w HTML.
	 */
	readonly PUBLIC_TURNSTILE_INVISIBLE_SITE_KEY?: string;
}

interface ImportMeta {
	readonly env: ImportMetaEnv;
}
