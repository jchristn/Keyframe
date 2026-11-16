# Keyframe Dashboard

The operator console for Keyframe: run image and video analyses, follow video jobs, check model endpoints, read request history, call any route from the API Explorer, and edit server settings. It is a static React app that talks to the Keyframe REST server straight from the browser.

Version 0.1.0 (ALPHA). Dashboard port 8701, server default `http://127.0.0.1:8700`.

## Quick start

```bash
cd dashboard
npm ci
npm run dev        # http://127.0.0.1:8701
```

Open `http://127.0.0.1:8701`, enter the server URL (remembered on this device) and a key. A fresh local server accepts the access key `default` and the admin key `keyframeadmin`; change both before sharing a server. The admin key unlocks Settings, server restart, endpoint tests, and request history deletion.

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server on 127.0.0.1:8701 |
| `npm run build` | Production build into `dist/` |
| `npm run preview` | Serve the build on 127.0.0.1:8701 |
| `npm test` | Unit tests (Vitest and Testing Library) |
| `npm run lint` | ESLint, zero warnings allowed |
| `node scripts/check-i18n.mjs` | Fails when a referenced translation key is missing from `en.json` (also run by `npm test`) |

`npm run dev` and `npm run build` first run `scripts/sync-assets.mjs`, which copies `assets/glyph.ico` (as `favicon.ico`), `assets/logo-dark.png`, and `assets/logo-light.png` from the repository into `public/`. When `../assets` is not present (for example inside the Docker build context), the copies already in `public/` are used.

## Docker

```bash
docker build -t jchristn77/keyframe-dashboard ./dashboard
docker run --rm -p 8701:8701 -e KEYFRAME_SERVER_URL=http://keyframe-server:8700 jchristn77/keyframe-dashboard
```

The image builds with Node and serves `dist/` from nginx on port 8701, with `curl` installed and a `GET /healthz` endpoint for health checks (`curl -fsS http://127.0.0.1:8701/healthz`). `KEYFRAME_SERVER_URL` is used only for the API Explorer's same-origin `/openapi.json` fallback described below; the browser still calls the server URL entered at login for everything else. `NGINX_RESOLVER` (default `127.0.0.11`, Docker's embedded DNS) resolves that upstream per request, so the container starts even when the server is down.

## Routes

| Route | Purpose |
|---|---|
| `/login` | Server URL and key. Validates with `GET /v1.0/whoami`; explains unreachable servers, bad keys, and rate limiting |
| `/dashboard/home` | Health, KPIs, attention items, request activity chart, recent jobs, workflow shortcuts, external services (Grafana 3700, Prometheus 9700, Tempo 3710) |
| `/dashboard/analyze/image` | Upload or URL, context, model, summary, typed field builder, presets, results tabs (Results, Fields, Telemetry, Properties, Raw JSON), copy as cURL, JavaScript, Python, or C# |
| `/dashboard/analyze/video` | Same builder plus scenes, per-scene fields, transcription, and keyframe thumbnails. Submits a job and opens it |
| `/dashboard/jobs` | Jobs with status, kind, principal, and time filters (kept in the URL), pagination, live refresh, cancel, delete, bulk delete, JSON |
| `/dashboard/jobs/:id` | Live progress, failure and interruption detail, scene timeline with thumbnails, transcript search, telemetry waterfall, result download |
| `/dashboard/endpoints` | Endpoints with health, kind filter, connectivity test (admin), detail and JSON |
| `/dashboard/request-history` | KPIs, activity chart, method, status, path, principal, and time filters, request inspector with headers and bodies, delete and bulk delete (admin) |
| `/dashboard/api-explorer` | Every operation from the live `/openapi.json`, parameters, headers, JSON body, destructive-operation confirmation, response tabs, code snippets, history |
| `/dashboard/settings` | Form editor for the settings file with Live and Restart badges, environment overrides, masked secrets, save, and restart with recovery polling (admin) |
| `/dashboard/server` | Health, version, identity, and copyable server URLs |

Navigation groups: Home, then Analyze (Image, Video), Operate (Jobs, Endpoints), Observability (Request History, API Explorer), and System (Settings, Server Info).

## Layout

```
src/
  main.jsx, App.jsx           entry point and routes
  index.css, App.css          design tokens (light and dark) and component styles
  context/                    AuthContext, ThemeContext, ToastContext
  components/                 shell (Sidebar, Topbar, DashboardLayout) and shared parts:
                              DataTable, Pagination, Modal, ConfirmModal, CopyButton, CopyableId,
                              StatusBadge, JsonViewer, ActivityChart, TelemetryWaterfall,
                              FieldBuilder, ContextEditor, SceneTimeline, RequestDetailsModal,
                              ActionMenu, Tabs, PresetBar, ExternalServicesCard, LanguageSelector
  views/                      one file per route
  hooks/                      useRemote, useApiExplorer, usePersistedState, useInterval, useElementWidth
  utils/                      api.js (ApiClient), fields.js, ranges.js, snippets.js, openApi.js,
                              settingsSchema.js, errors.js, storage.js, media.js
  i18n/                       index.js, localeRegistry.js, resources.js, formatters.js, locales/en.json
```

All HTTP goes through `ApiClient` in `src/utils/api.js`, a small `fetch` wrapper that adds `Authorization: Bearer <key>`, normalizes errors into `ApiError` (status, server error code, message, details, request id, network flag), and normalizes paged results into `{ items, pageNumber, pageSize, totalCount, totalPages }`. Charts are hand-rolled SVG; there is no charting or UI library.

## Credentials

The server URL is stored in `localStorage` (`keyframe.serverUrl`). The key is held in the auth context. It is written to `localStorage` (`keyframe.apiKey`) only when "Remember key on this device" is ticked, and it is removed on sign-out, when the server rejects it, or when signing in without that option. The key is never logged, never placed in generated code snippets (they read `KEYFRAME_API_KEY` from the environment), and never saved in API Explorer history or presets. Settings secrets come back from the server masked (`****` plus the last four characters) and are sent back unchanged unless an admin chooses Change.

## Internationalization

Every visible and accessibility string lives in `src/i18n/locales/en.json` and is read through i18next. The locale registry (`localeRegistry.js`) holds codes, autonyms, text direction, and fallbacks; the choice persists under `keyframe.locale` and survives reloads and sign-out, and `<html lang dir>` follows it. Numbers, dates, durations, byte sizes, percentages, and lists go through `formatters.js` with an explicit locale.

English is the only translated bundle today. Two pseudo-locales are generated from it at runtime for layout testing: `en-XA` (accented, about 35 percent longer) and `ar-XB` (right to left). Pick them from the language selector on the login page, in the top bar, or under Settings.

To add a locale: add `src/i18n/locales/<code>.json` with the same keys, import it in `resources.js`, and add an entry to `LOCALES` in `localeRegistry.js`. Keys are stable dotted identifiers grouped by screen (`jobs.columns.status`); plurals use i18next `_one` and `_other` suffixes; wire values such as job statuses are translated through `enums.*`. Product names (Keyframe, Grafana, ffmpeg), route paths, JSON property names, and field keys stay untranslated.

## API Explorer and CORS

The explorer loads `GET /openapi.json` from the signed-in server. If the browser cannot read it directly (a network or CORS failure), it retries `/openapi.json` on the dashboard's own origin, which the Vite dev server and the nginx image proxy to `KEYFRAME_SERVER_URL`, and shows a notice when it does. Requests made from the explorer always go straight to the signed-in server. For local development against a server on another port, run `KEYFRAME_SERVER_URL=http://127.0.0.1:8711 npm run dev`.

## Tests

`npm test` runs Vitest with jsdom and Testing Library: the ApiClient (headers, queries, paging, error normalization, 401 handling), the field model and validation, the FieldBuilder component (add, type switch, Enum options, reorder, duplicate, remove, inline errors), the Login, Jobs, Settings, and Server Info views, chart range normalization, snippet redaction, and the i18n runtime (missing keys, locale persistence, `lang` and `dir`, formatters).
