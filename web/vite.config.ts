import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The API (src/StateBallot.Api) listens on 5080. Proxying keeps the browser on one origin.
const api = { "/api": "http://localhost:5080" };

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: api },
  preview: { proxy: api },
});
