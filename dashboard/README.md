# Keyframe Dashboard

The operator console for Keyframe: run image, video, and audio analyses, follow jobs, see performance over time on the Analytics page, check model endpoints, read request history, call any route from the API Explorer, and edit server settings. It is a static React app that talks to the Keyframe REST server straight from the browser.

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
| `node scripts/capture-screenshots.mjs` | Regenerates the README screenshots: seeds an image, video, and audio analysis from `docker/factory/samples`, waits for the default listening endpoint to report healthy, and writes ten 1280 by 800 light-theme PNGs to `assets/screenshots/`. Needs `KEYFRAME_KEY` (admin key); `KEYFRAME_URL`, `DASHBOARD_URL`, `SCREENSHOT_DIR`, and `PLAYWRIGHT_MODULE` (a Playwright package directory, when `playwright` is not installed) are optional. Key fields are blanked before every capture |
| `node scripts/check-i18n.mjs` | Fails on missing or orphaned keys, hard-coded JSX strings, and any locale that differs from `en.json` in keys, placeholders, empty values, or dashes (the same checks run in `npm test`) |

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
| `/dashboard/analyze/video` | Same builder plus scenes, per-scene fields, scene audio (`Scenes.Audio`, with a warning when the vision endpoint cannot take audio) and its audio quality, transcription, and keyframe thumbnails. Submits a job and follows it on the page, with the Listening card on the result |
| `/dashboard/analyze/audio` | `POST /v1.0/audio`: audio file or URL (or the audio track of a video); Listen (on by default) with a listening endpoint picker limited to audio-capable endpoints, their health, and a warning when the analysis will fall back to the transcript; excerpt counts and a collapsed Audio quality group (format, sample rate, channels, MP3 bitrate, each starting at the server default); transcription and text endpoints; Measure; Sections split at silences with per-section summary and fields; summary and typed fields (available when Transcribe or Listen is on). Submits a job, polls it on the page, and shows the Listening card, transcript, sections, measurements, results, fields, telemetry, properties, and raw JSON |
| `/dashboard/jobs` | Jobs with status, kind, principal, and time filters (kept in the URL), pagination, live refresh, cancel, delete, bulk delete, JSON |
| `/dashboard/jobs/:id` | Live progress, failure and interruption detail, the Listening card (mode, reason, endpoint, model, audio sent, heard versus total, clips, with a warning badge whenever the mode is not Full or Scenes), scene timeline with thumbnails, for audio jobs a section timeline (keyboard-navigable SVG of sections, silences, and heard clips, with per-section results and audio downloads) and a Measurements grid, transcript search, telemetry waterfall, result download. Each scene and transcript segment can be downloaded as a clip (MP4), audio (MP3), or for scenes a frame (JPEG) through `POST /v1.0/segment`; for uploaded media the original file is picked again from disk |
| `/dashboard/analytics` | Admin. Time range (1 hour, 24 hours, 7 days, 30 days, custom), media, endpoint, and model filters; KPIs; per media kind cards; analyses over time (click a bar to list it); timing percentiles over time for any metric (analysis time, queue wait, model call time, time to first token, generation time, tokens per second, tokens per analysis, realtime factor, media duration and size); tokens and success rate over time; a distribution table (count, average, p50, p90, p95, p99, max); token and input totals; breakdowns by model, stage, and purpose; recent analyses with a detail drawer (metadata, per-call waterfall with queue, first-token, and generation segments, call table, JSON, delete) |
| `/dashboard/endpoints` | Endpoints with health, capability badges (images, structured output, audio input, transcription), kind and capability filters, connectivity test (admin), detail and JSON |
| `/dashboard/request-history` | KPIs, activity chart, method, status, path, principal, and time filters, request inspector with headers and bodies, delete and bulk delete (admin) |
| `/dashboard/api-explorer` | Every operation from the live `/openapi.json`, parameters, headers, JSON body, destructive-operation confirmation, response tabs, code snippets, history |
| `/dashboard/settings` | Form editor for the settings file with Live and Restart badges, environment overrides, masked secrets, save, and restart with recovery polling (admin) |
| `/dashboard/server` | Health, version, identity, and copyable server URLs |

Navigation groups: Home, then Analyze (Image, Video, Audio), Operate (Jobs, Endpoints), Observability (Analytics, Request History, API Explorer), and System (Settings, Server Info).

The analyze pages are one column: request sections, the action bar, then results. The API Explorer is one column too: a full-width, searchable operation picker at the top, then path and query parameters, headers, body, and the response. The request history chart is twice the height of the home chart.

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
  theme/                      palettes.js (token tables), tokens.js (derived tokens and CSS),
                              color.js (mixing and WCAG contrast), index.js (persistence, apply)
  i18n/                       index.js, localeRegistry.js, resources.js, pseudo.js, merge.js,
                              formatters.js, locales/<code>.json
```

All HTTP goes through `ApiClient` in `src/utils/api.js`, a small `fetch` wrapper that adds `Authorization: Bearer <key>`, normalizes errors into `ApiError` (status, server error code, message, details, request id, network flag), and normalizes paged results into `{ items, pageNumber, pageSize, totalCount, totalPages }`. Charts are hand-rolled SVG (`ActivityChart`, `charts/TimeSeriesChart`, `TelemetryWaterfall`); there is no charting or UI library.

## Credentials

The server URL is stored in `localStorage` (`keyframe.serverUrl`). The key is held in the auth context. It is written to `localStorage` (`keyframe.apiKey`) only when "Remember key on this device" is ticked, and it is removed on sign-out, when the server rejects it, or when signing in without that option. The key is never logged, never placed in generated code snippets (they read `KEYFRAME_API_KEY` from the environment), and never saved in API Explorer history or presets. Settings secrets come back from the server masked (`****` plus the last four characters) and are sent back unchanged unless an admin chooses Change.

## Appearance

Sixteen palettes, each with a light and a dark variant: Slate (default), Indigo Night, Fjord, Nocturne, Solstice, Cocoa, Dusk Rose, Metro Night, Forest, Ocean, Amber, Graphite, Lavender, Mint, Sandstone, and Terracotta. Mode is Light, Dark, or System (follows the operating system live). Pick them from the palette button in the top bar or under Settings, Preferences. The choice is saved per browser under `keyframe.appearance`; the inline script in `index.html` sets `data-palette` and `data-theme` on `<html>` and the cached background color before the first paint, and `src/theme` injects the token stylesheet before React renders, so a reload never flashes the wrong colors.

Each palette in `src/theme/palettes.js` defines base tokens (background, surface, surface 2, border, text, muted text, primary, primary contrast, accent, success, warning, danger, info, focus ring, shadow) and eight chart hues. `tokens.js` derives the rest (hover and soft fills, faint text, solid danger buttons, chart fills, JSON colors, shadows, scrim) as concrete colors and emits one `:root[data-palette][data-theme]` block per palette and mode. Every stylesheet uses these custom properties; components contain no literal colors (the palette picker swatches read the token tables). `theme.test.js` computes WCAG contrast from the tables and requires 4.5:1 for text on every surface, primary and danger buttons, status text on its soft background, and code colors, and 3:1 for focus rings and chart marks, in every palette and mode.

To add a palette: add an entry to `PALETTES` with `light`, `dark`, and `chart`, add `appearance.palettes.<id>` to every locale, add the id to the `appearance.palettes` list in `scripts/check-i18n.mjs`, and run `npm test`.

Controls that sit side by side share one height: `--control-h` (34px) for inputs, selects, and buttons, `--control-h-sm` (28px) for compact toolbars. Fields in a `.form-row` use CSS subgrid so their controls line up even when one label wraps or one field has help text.

## Internationalization

Every visible and accessibility string lives in `src/i18n/locales/en.json` and is read through i18next. The locale registry (`localeRegistry.js`) holds code, English name, autonym, direction, fallback, Intl tag, aliases, and an optional script-first font stack for each locale:

| Code | Language | Selector label | Notes |
|---|---|---|---|
| `en` | English | English | Source and fallback |
| `es` | Spanish | Español | |
| `pt` | Portuguese | Português | Brazilian Portuguese (`pt-BR` for formatting) |
| `fr` | French | Français | |
| `it` | Italian | Italiano | |
| `de` | German | Deutsch | |
| `zh` | Mandarin Chinese | 中文 | Simplified (`zh-Hans`), CJK font stack |
| `ar` | Arabic | العربية | Right to left, Arabic font stack |
| `ru` | Russian | Русский | |
| `ms` | Malay | Bahasa Melayu | |
| `hi` | Hindi | हिन्दी | Devanagari font stack |
| `ja` | Japanese | 日本語 | Japanese font stack; `Kanji` is an alias |
| `en-XA` | Pseudo (expansion) | [Ƥşḗŭḓǿ] | Generated: accented and about 35 percent longer |
| `ar-XB` | Pseudo (RTL) | [RTL Pseudo] | Generated: right to left |

English is bundled; every other catalog is a lazily loaded chunk merged over English. `main.jsx` waits for the active catalog before the first render, so there is no English flash. The locale is resolved from `?lang=`, then `localStorage` (`keyframe.locale`), then the browser, then English, and survives reloads, sign-out, and deep links. `<html lang dir>` and the `--locale-font` stack follow it, and API calls send `Accept-Language`. The selector shows autonyms on the login page, in the top bar, and under Settings. Layout uses logical CSS properties (`margin-inline`, `inset-inline-start`, `text-align: start`), so right-to-left locales mirror the shell, forms, tables, menus, drawers, and modals; charts keep a left-to-right time axis, and code, IDs, and URLs stay left to right inside Arabic text.

Numbers, dates, times, relative time, durations, byte sizes, percentages, compact numbers, and lists go through `formatters.js` with an explicit locale. Page titles come from the active locale (`app.documentTitle`).

Conventions:

- Keys are stable dotted identifiers grouped by screen (`analytics.byModel.title`). Plurals use i18next suffixes (`_one`, `_other`, plus `_zero`, `_two`, `_few`, `_many` where the language needs them). Interpolation uses `{{name}}`; never concatenate translated fragments.
- Wire values (job statuses, media kinds, stages, purposes, providers) are translated through `enums.*`; raw enum values are never shown.
- Not translated: product and tool names (Keyframe, Grafana, Prometheus, Tempo, ffmpeg, Ollama), route paths, setting paths such as `Inference.DefaultText`, JSON property names, field keys, file formats, and acronyms (API, URL, JSON, p50 to p99).
- Server-authored text (error messages, OpenAPI summaries, model output) is shown as the server sends it; the dashboard sends `Accept-Language` so a localizing server can respond in kind, and every client-side headline and hint around it is localized.
- No em-dashes or en-dashes in any catalog.

Glossary (keep consistent across a catalog): analysis, job, endpoint (a configured model server), model, transcript, scene, keyframe, prompt tokens, completion tokens, time to first token, realtime factor (analysis time divided by media duration), palette.

To add a locale: add `src/i18n/locales/<code>.json` with every key of `en.json` (copy it and translate), add an entry to `LOCALES` in `localeRegistry.js`, and run `node scripts/check-i18n.mjs` and `npm test`. No application code changes. To add strings: add the key to `en.json`, use it through `t()`, then add it to every locale; the parity test fails until you do. Pick `en-XA` or `ar-XB` in the language selector to check expansion and right-to-left layouts.

## API Explorer and CORS

The explorer loads `GET /openapi.json` from the signed-in server. If the browser cannot read it directly (a network or CORS failure), it retries `/openapi.json` on the dashboard's own origin, which the Vite dev server and the nginx image proxy to `KEYFRAME_SERVER_URL`, and shows a notice when it does. Requests made from the explorer always go straight to the signed-in server. For local development against a server on another port, run `KEYFRAME_SERVER_URL=http://127.0.0.1:8711 npm run dev`.

## Tests

`npm test` runs Vitest with jsdom and Testing Library: the ApiClient (headers, queries, paging, error normalization, 401 handling, audio, analytics, and segment routes), the field model and validation, the FieldBuilder component, the Login, Jobs, Settings, Server Info, Analytics (KPIs, filters, detail drawer, delete, missing backend, empty and admin states), Analyze Audio (request shape for every listening, rendition, measure, and section field; field builder enablement under Transcribe and Listen; listening endpoint health and fallback warnings; submit, polling, transcript), the Listening card, Measurements and Sections tabs with their empty and error states, the section timeline (rendering and keyboard navigation), Analyze Video scene audio, Endpoints capability badges, Settings listening fields, Job Detail (segment downloads, uploaded-media note), API Explorer (operation picker), Request History (chart height), and Analyze Image (one column) views, the appearance picker and persistence, palette contrast for every palette and mode, and the i18n runtime and catalogs (registry, lazy loading, persistence, `lang` and `dir`, plural rules, formatters across locales, missing and orphaned keys, hard-coded JSX strings, and key and placeholder parity for all twelve locales). API calls in tests use mocked clients and fixtures from `src/test/fixtures.js`; no server is needed.
