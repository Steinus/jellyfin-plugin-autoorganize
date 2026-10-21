# Jellyfin Auto Organize

Private development project for an Emby Auto Organize-style workflow in Jellyfin.

## Compatibility

Current development target: **Jellyfin 12.2 / .NET 10**.

## Current milestone (0.1.0)

- Separate watch/source and destination folders for movies and TV shows.
- Move, Copy and Hardlink modes.
- Dry-run enabled by default.
- Configurable movie and episode naming templates.
- Localized title lookup through Jellyfin's already configured metadata providers (no separate TMDB API key).
- Windows/SMB-safe filenames. By default `:` becomes ` - `.
- Sidecar handling for subtitles, NFO and common image files.
- Minimum file age to avoid touching downloads that are still being written.
- Jellyfin Scheduled Task integration (default every 10 minutes).

## Safety defaults

The plugin ships with processing disabled and dry-run enabled. Configure paths, enable processing, run the scheduled task, and inspect Jellyfin logs before disabling dry-run.

## Current limitation

0.1.0 performs provider lookup through Jellyfin after conservative filename parsing. Automatic matching is intentionally protected by dry-run defaults; a persistent manual-review queue and confidence scoring are still planned.

## Example

Movie input:

`/media/Torrents/Movies/Spider-Man.Brand.New.Day.2026.mkv`

Default output:

`/media/Movies/Spider-Man Brand New Day (2026)/Spider-Man Brand New Day (2026).mkv`

A Russian provider title such as `Человек-паук: Новый день` will be sanitized to:

`Человек-паук - Новый день (2026)`
