import { defineConfig } from 'vitest/config';
import angular from '@analogjs/vite-plugin-angular';

export default defineConfig(({ mode }) => ({
  plugins: [
    angular(),
  ],
  test: {
    globals: true,
    setupFiles: ['src/test-setup.ts'],
    environment: 'jsdom',
    include: ['src/**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}'],
    reporters: ['default'],
  },
  resolve: {
    alias: {
      '@core': '/src/app/core',
      '@domains': '/src/app/domains',
      // Był w tsconfigu, ale nie tutaj — typecheck przechodził, a vitest wywalał się na
      // „Failed to resolve import" przy pierwszym specu, który po niego sięgnął.
      '@features': '/src/app/features',
      '@shared': '/src/app/shared',
      '@apps': '/src/app/apps',
      '@env': '/src/environments',
    },
  },
}));
