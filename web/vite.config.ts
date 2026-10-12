import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development, Vite serves the React app and forwards /api to the .NET host,
// so the browser sees one origin, exactly as in production (ADR-008, ADR-012).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      // Google redirects back here after sign-in; the .NET host handles it.
      '/signin-google': 'http://localhost:5080',
    },
  },
});
