# Bazarr Emby Trigger

Bazarr Emby Trigger is an Emby server plugin that registers as a subtitle provider but delegates the actual subtitle search and download work to Bazarr. When a user runs a subtitle search from Emby, the plugin records a subtitle-file snapshot, matches the Emby item to Bazarr, triggers Bazarr, and then notifies Emby users when a new subtitle file is detected.

## What the plugin does

- Shows up inside Emby as a subtitle provider.
- Accepts subtitle search requests from Emby for movies and episodes.
- Queues and rate-limits outbound subtitle searches before they are sent to Bazarr.
- Prefers ID-based matching (`Radarr`, `Sonarr`, `TVDB`, `IMDb`) and falls back to cached file-path matching.
- Lets Bazarr handle provider choice, matching, download logic, and post-download processing.
- Polls the media folder for new or modified subtitle files after Bazarr has been triggered.
- Sends an Emby notification once subtitles arrive, or when a search times out.

## End-to-end flow

1. Emby calls the plugin through the subtitle-provider contract.
2. The plugin captures the current subtitle snapshot for the requested media file.
3. The request is written to the plugin's state file and queued.
4. The background worker waits for rate-limit capacity.
5. The plugin fetches or reuses cached Bazarr metadata and matches the Emby item.
6. For movies, the plugin triggers Bazarr's `search-missing` workflow.
7. For episodes, the plugin uses Bazarr's public manual-search API to request candidates and submits the top-scored result because Bazarr's current public API does not expose an exact single-episode `search-missing` trigger.
8. The plugin returns an empty subtitle result list to Emby so the request behaves as a background trigger instead of a manual picker.
9. The worker polls the media directory for new or changed subtitle files.
10. When a subtitle appears, the plugin sends an Emby notification naming the movie or episode.

## Features

- Emby subtitle-provider integration
- Bazarr connection test endpoint for the dashboard UI
- Configurable host, port, reverse-proxy base URL, API key, queue rate limit, cache TTL, timeout, verbosity, and custom headers
- Persistent pending-search cache stored in the plugin data directory
- File snapshot comparison for subtitle arrival detection
- Debug logging around matching, rate limiting, Bazarr requests, queue flow, and notifications
- Focused automated tests for matching, snapshot detection, and rate limiting

## Requirements and compatibility

- Emby Server with plugin support
- Bazarr reachable from the Emby server host
- `.NET Standard 2.0` plugin target for broad Emby compatibility
- .NET 8 SDK or later for local development and CI builds

## Installation

1. Build the plugin with `dotnet build -c Release`.
2. Copy `src/Plugin.Bazarr.Emby.Trigger/bin/Release/netstandard2.0/Plugin.Bazarr.Emby.Trigger.dll` into Emby's plugins directory.
3. Restart Emby.
4. Open **Dashboard → Plugins → My Plugins → Bazarr Emby Trigger**.
5. Enter Bazarr connectivity settings and save.

## Configuration

The dashboard configuration page includes:

- Bazarr host
- Bazarr port
- Optional reverse-proxy/base URI
- Bazarr API key
- Searches-per-hour rate limit
- Bazarr metadata cache TTL
- Queue poll interval
- Subtitle detection timeout
- Verbose logging toggle
- Optional custom headers for development or proxy troubleshooting

### Validation and connection testing

- Host and port are validated inline in the dashboard UI.
- The page shows the resolved Bazarr API endpoint summary.
- **Test Connection** calls the plugin's own server-side endpoint so the Bazarr API key stays in headers and out of URLs.

## Local build instructions

```bash
dotnet restore Plugin.Bazarr.Emby.Trigger.sln
dotnet build Plugin.Bazarr.Emby.Trigger.sln
dotnet test Plugin.Bazarr.Emby.Trigger.sln
```

## CI/CD and release workflow behavior

The repository is structured around a two-branch release model even though GitHub still reports `main` as the current default branch today:

- `dev` is intended for active development and prerelease validation.
- `main` is intended for stable promotion.

### Workflows

- **CI (`ci.yml`)** runs on pushes and pull requests, builds the solution, runs tests, and uploads a build artifact.
- **Release (`release.yml`)** runs on pushes to `dev` and `main`.
  - `dev` publishes a prerelease artifact/tag with `dev` in the identifier.
  - `main` publishes a stable artifact/tag without the prerelease suffix.
  - Both release flows use the GitHub run number to auto-increment artifact and tag versions.

## Architecture overview

### Emby-facing layer

- `Plugin.cs` wires the plugin metadata and dashboard page.
- `BazarrSubtitleProvider.cs` implements `ISubtitleProvider` and intentionally returns an empty list so Emby treats the action as a background trigger.
- `ServerEntryPoint.cs` starts the long-lived queue processor.
- `Configuration/TestConnectionService.cs` exposes a server-side connection test endpoint for the settings page.

### Core services

- `Integration/BazarrClient.cs` sends authenticated Bazarr API requests and keeps the API key in headers.
- `Services/MediaMatcher.cs` matches Emby requests to Bazarr metadata.
- `Services/BazarrCatalogCache.cs` caches Bazarr metadata with a configurable TTL.
- `Services/SlidingWindowRateLimiter.cs` enforces the searches-per-hour cap.
- `Services/SearchCoordinator.cs` persists queued requests, triggers Bazarr, compares snapshots, and sends notifications.
- `Services/SubtitleSnapshotService.cs` captures and compares subtitle file state without touching Emby's database.
- `Services/PendingSearchRepository.cs` stores queue state in the plugin data directory.

## Important design decisions

- **No API keys in URLs:** The plugin always places the Bazarr API key in `X-API-KEY` headers.
- **Persistent plugin-owned state:** Pending searches are stored in a plugin state file instead of Emby's metadata DB so the implementation stays localized and reversible.
- **Empty subtitle list response:** Emby still expects a subtitle provider response even though Bazarr does the real work, so the plugin returns an empty result set to avoid an error state.
- **Episode trigger compromise:** Bazarr's public API exposes an exact movie `search-missing` route but not an exact episode equivalent. The plugin therefore uses Bazarr's public episode manual-search API and posts the top-scored result rather than inventing its own subtitle matching rules.

## Logging and debugging

Verbose logging is designed for real deployments:

- request entry and exit
- match decisions and which IDs were considered
- queue/rate-limit delays
- Bazarr request intent and response outcome
- subtitle snapshot comparisons
- notification decisions
- recoverable failures and timeout cases

The plugin deliberately avoids logging the Bazarr API key.

## Troubleshooting

- **Connection test fails:** Re-check host, port, reverse-proxy base path, and API key.
- **Queued searches never run:** Increase the searches-per-hour setting or wait for the sliding window to clear.
- **No match found:** Confirm that Bazarr has synced the movie/series and that Emby provider IDs or file paths line up.
- **No notification arrives:** Check Emby notification configuration and review the plugin logs for snapshot comparison output.
- **Episode behavior looks different from movies:** This is expected because Bazarr currently exposes different public APIs for movie auto-search and episode manual search.

## Security handling

- The Bazarr API key is stored in plugin configuration and only transmitted through request headers.
- The dashboard connection test uses the plugin's own server-side endpoint so the API key is not placed in query strings or browser history.
- Custom headers are supported for edge-case reverse proxies, but they are still sent as headers rather than URL parameters.

## Development references

- Emby SDK: https://github.com/MediaBrowser/Emby.SDK
- Emby plugin developer docs: https://dev.emby.media/index.html
- Bazarr API and source: https://github.com/morpheus65535/bazarr
