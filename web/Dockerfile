# The run console, built and served by Vite's preview server. For local use, not a production web server.
FROM node:22-alpine

# Vite writes a temporary file beside its config when it starts, so the app directory belongs to the node user.
RUN mkdir /web && chown node:node /web
USER node
WORKDIR /web

COPY --chown=node:node package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY --chown=node:node . .
RUN npm run build

EXPOSE 5173
CMD ["npx", "vite", "preview", "--host", "0.0.0.0", "--port", "5173", "--strictPort"]
