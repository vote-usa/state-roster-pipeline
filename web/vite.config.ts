import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The API (src/StateBallot.Api) listens on 5080. Proxying keeps the browser on one origin.
// docker-compose.yml sets API_URL to the api service.
const api = { "/api": process.env.API_URL ?? "http://localhost:5080" };

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: api },
  preview: { proxy: api },
});
