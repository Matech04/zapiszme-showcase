import { svelte } from '@sveltejs/vite-plugin-svelte';
import { svelteTesting } from '@testing-library/svelte/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte(), svelteTesting()],
  define: {
    'import.meta.env.PUBLIC_API_BASE_URL': JSON.stringify('https://api.example/v1/'),
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/vitest-setup.ts'],
    include: ['src/**/*.test.ts'],
    passWithNoTests: false,
  },
});
