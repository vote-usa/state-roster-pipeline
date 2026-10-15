# Run console

The web front end for the staging database: React 18, TypeScript, Vite, Material UI, TanStack Query and React Router. It has no data of its own. Every page reads from the API in `src/StateBallot.Api`, so the API has to be running first. What the console shows and how jobs run is in the "Run console" section of the [root README](../README.md).

## Running it

Use Node 22, the version the Docker image builds with.

```bash
# from the repo root
docker compose up -d --wait mysql
dotnet run --project src/StateBallot.Api      # http://localhost:5080, migrates the staging schema on start

# in another terminal
cd web
npm ci                                        # installs exactly what package-lock.json pins
npm run dev                                   # http://localhost:5173, reloads as you edit
```

Use `npm ci`, not `npm install`. `npm install` can rewrite `package-lock.json` when your npm version differs from the one that wrote it.

If a page says "The API answered 503" or "Is it running?", the API is not running or cannot reach MySQL. The API's terminal says which.

## Scripts

| Command | What it does |
| --- | --- |
| `npm run dev` | Dev server on port 5173 with hot reload |
| `npm run build` | Type-checks with `tsc`, then builds to `dist/`. A type error fails the build. |
| `npm run preview` | Serves the built `dist/` on port 5173. The Docker image runs this. |

There are no tests or lint script for the front end yet. Run `npm run build` before pushing, because it is the only check.

## Talking to the API

The browser code only calls relative `/api/...` paths (`src/api.ts`). Vite forwards them to the API, so the browser stays on one origin and the API needs no CORS. The forwarding target is set by `API_URL`, which only Vite reads (see `vite.config.ts`):

| Where | `API_URL` |
| --- | --- |
| `npm run dev` on the host | unset, defaults to `http://localhost:5080` |
| `docker compose up web` | `http://api:5080`, set in `docker-compose.yml` |
| An API somewhere else | `API_URL=http://host:5080 npm run dev` |

There are no `.env` files or `VITE_*` variables.

Pages poll the API, so runs started from the CLI or another browser show up without a reload.

## Layout

| Path | What it is |
| --- | --- |
| `src/main.tsx` | Entry point: React Query client, router, theme |
| `src/App.tsx` | Top bar and routes |
| `src/api.ts` | Response types, `useApi` (polling fetch hook), `startJob` |
| `src/theme.ts` | Material UI theme |
| `src/ui.tsx` | Shared components |
| `src/StartRunDialog.tsx` | The "Start a run" dialog, which posts to `/api/jobs` |
| `src/pages/` | One file per route |

| Route | Page |
| --- | --- |
| `/` | `Overview`: every state with its latest retrieve and run |
| `/states/:code` | `StatePage` |
| `/states/:code/elections/:key` | `ElectionPage` |
| `/runs/:id` | `RunPage` |
| `/retrieves/:id` | `CapturePage`. The console calls a capture a "retrieve". |
| `/activity` | `Activity`: recent jobs, retrieves and runs |
