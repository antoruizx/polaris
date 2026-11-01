import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// La web habla con la API por el mismo origen: Vite reenvía /api y /hubs a ASP.NET Core.
const api = "http://localhost:5080";

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
    proxy: {
      "/api": { target: api, changeOrigin: true },
      "/hubs": { target: api, changeOrigin: true, ws: true },
    },
  },
});
