/**
 * Cloudflare Turnstile — invisible mode helper dla publicznego booking flow.
 *
 * Użycie:
 *   onMount(async () => {
 *     await loadTurnstileScript();
 *     widgetId = renderInvisibleTurnstile(container, (token) => currentToken = token);
 *   });
 *   onDestroy(() => removeTurnstile(widgetId));
 *   // Przed API call:
 *   const token = await getFreshTurnstileToken(widgetId);
 *   await client.publicOtp_RequestOtp(slug, id, { ..., turnstileToken: token });
 *
 * W invisible mode widget renderuje się ukryty i sam wywołuje callback z tokenem (passive).
 * Token żyje ~5 min; getFreshTurnstileToken() wymusza odświeżenie tuż przed użyciem.
 */

declare global {
	interface Window {
		turnstile?: {
			render(
				element: HTMLElement,
				options: {
					sitekey: string;
					size?: 'normal' | 'compact' | 'invisible';
					theme?: 'light' | 'dark' | 'auto';
					callback: (token: string) => void;
					'expired-callback'?: () => void;
					'error-callback'?: () => void;
				}
			): string;
			execute(widgetId: string): void;
			reset(widgetId: string): void;
			remove(widgetId: string): void;
			getResponse(widgetId: string): string | undefined;
		};
	}
}

const SCRIPT_SRC = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

let scriptPromise: Promise<void> | null = null;

export function getTurnstileSiteKey(): string | undefined {
	return import.meta.env.PUBLIC_TURNSTILE_INVISIBLE_SITE_KEY || undefined;
}

export function isTurnstileEnabled(): boolean {
	return !!getTurnstileSiteKey();
}

/** Async-loads Cloudflare Turnstile script. Idempotent — wielokrotne wywołania zwracają ten sam Promise. */
export function loadTurnstileScript(): Promise<void> {
	if (typeof window === 'undefined') {
		// SSR: nie ładujemy — komponenty wołają to z onMount (client-only).
		return Promise.reject(new Error('Turnstile requires browser env'));
	}
	if (window.turnstile) {
		return Promise.resolve();
	}
	if (scriptPromise) {
		return scriptPromise;
	}

	scriptPromise = new Promise<void>((resolve, reject) => {
		const existing = document.querySelector<HTMLScriptElement>('script[data-turnstile-script]');
		if (existing) {
			existing.addEventListener('load', () => resolve(), { once: true });
			existing.addEventListener('error', () => reject(new Error('Turnstile script load failed')), {
				once: true
			});
			return;
		}

		const script = document.createElement('script');
		script.src = SCRIPT_SRC;
		script.async = true;
		script.defer = true;
		script.dataset['turnstileScript'] = 'true';
		script.onload = () => resolve();
		script.onerror = () => reject(new Error('Turnstile script load failed'));
		document.head.appendChild(script);
	});

	return scriptPromise;
}

/** Renderuje invisible widget. Zwraca widgetId. Callback dostaje token, gdy CF przepuszcza. */
export function renderInvisibleTurnstile(
	container: HTMLElement,
	onToken: (token: string) => void,
	onError?: () => void
): string | null {
	const siteKey = getTurnstileSiteKey();
	if (!siteKey || !window.turnstile) {
		return null;
	}

	return window.turnstile.render(container, {
		sitekey: siteKey,
		size: 'invisible',
		callback: onToken,
		'error-callback': () => onError?.(),
		'expired-callback': () => onError?.()
	});
}

/**
 * Generuje świeży token przez reset + execute. Cloudflare validuje każdy token tylko RAZ
 * przez siteverify — więc cache-owanie `getResponse` powodowało `timeout-or-duplicate`
 * na drugim wywołaniu w tej samej sesji (np. user klika drugi slot po pierwszym /hold).
 * Reset czyści stan widgetu i `execute` generuje nowy passive token.
 * Timeout 10s zabezpiecza przed wiszącymi promise-ami.
 */
export function getFreshTurnstileToken(widgetId: string | null): Promise<string | undefined> {
	if (!widgetId || !window.turnstile) {
		// Turnstile wyłączony / niezaładowany — pomijamy (backend i tak zaakceptuje w env Testing/Dev).
		return Promise.resolve(undefined);
	}

	// Reset zruje stary token (jeśli był), żeby getResponse zwracał dopiero NOWY z execute.
	window.turnstile.reset(widgetId);

	return new Promise<string | undefined>((resolve) => {
		const id = setTimeout(() => resolve(undefined), 10000);
		const poll = () => {
			const t = window.turnstile?.getResponse(widgetId);
			if (t) {
				clearTimeout(id);
				resolve(t);
				return;
			}
			setTimeout(poll, 100);
		};
		window.turnstile?.execute(widgetId);
		poll();
	});
}

export function resetTurnstile(widgetId: string | null): void {
	if (widgetId && window.turnstile) {
		window.turnstile.reset(widgetId);
	}
}

export function removeTurnstile(widgetId: string | null): void {
	if (widgetId && window.turnstile) {
		window.turnstile.remove(widgetId);
	}
}
