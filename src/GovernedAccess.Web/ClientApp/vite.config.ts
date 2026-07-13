import { fileURLToPath } from "node:url";

import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

const clientRoot = fileURLToPath(new URL(".", import.meta.url));
const webRoot = fileURLToPath(new URL("../wwwroot", import.meta.url));

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, clientRoot, "VITE_");
  const apiProxyTarget =
    environment.VITE_API_PROXY_TARGET ?? "https://localhost:7251";

  return {
    plugins: [react()],
    build: {
      outDir: webRoot,
      emptyOutDir: true,
      rollupOptions: {
        output: {
          entryFileNames: "assets/[name]-[hash].js",
          chunkFileNames: "assets/[name]-[hash].js",
          assetFileNames: "assets/[name]-[hash][extname]",
        },
      },
    },
    server: {
      proxy: {
        "/api": {
          target: apiProxyTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
    test: {
      environment: "jsdom",
      globals: true,
      css: true,
      restoreMocks: true,
      include: ["src/**/*.test.{ts,tsx}"],
    },
  };
});
