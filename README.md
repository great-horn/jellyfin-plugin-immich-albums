# Jellyfin Immich Albums Plugin

A Jellyfin plugin that syncs your [Immich](https://immich.app/) photo albums into Jellyfin as photo libraries using symlinks.

## Features

- **Automatic sync** — Periodically fetches albums from Immich and creates symlinks in a local folder
- **HEIC to JPEG conversion** — Automatically converts HEIC/HEIF files to JPEG with proper EXIF orientation (macOS only)
- **Shared albums** — Optionally include albums shared with you
- **Path mapping** — Maps Immich container paths to host paths (for Docker setups)
- **Orphan cleanup** — Removes symlinks and folders for photos/albums that no longer exist in Immich
- **Duplicate handling** — Handles duplicate filenames across albums
- **Configurable interval** — Set sync frequency from 1 to 168 hours
- **Test connection** — Verify your Immich API connection from the plugin settings

## Requirements

- **Jellyfin** 10.11+
- **Immich** instance with API access
- **.NET 9.0** runtime
- **macOS** for HEIC→JPEG conversion (uses `sips`). On Linux/Windows, HEIC files are skipped with a warning — all other formats work fine.

## Installation

### From Plugin Repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**
2. Add a new repository:
   - **Name**: `Immich Albums`
   - **URL**: `https://raw.githubusercontent.com/aldebahran/jellyfin-plugin-immich-albums/main/manifest.json`
3. Go to **Catalog**, find **Immich Albums** and install it
4. Restart Jellyfin

### Manual Installation

1. Download the latest release ZIP from [Releases](https://github.com/aldebahran/jellyfin-plugin-immich-albums/releases)
2. Extract to your Jellyfin plugins directory:
   - **macOS**: `~/Library/Application Support/jellyfin/plugins/ImmichAlbums_X.X.X.X/`
   - **Linux**: `/var/lib/jellyfin/plugins/ImmichAlbums_X.X.X.X/`
   - **Windows**: `%LOCALAPPDATA%\jellyfin\plugins\ImmichAlbums_X.X.X.X\`
3. Restart Jellyfin

## Configuration

After installation, go to **Dashboard → Plugins → Immich Albums**:

| Setting | Description |
|---------|-------------|
| **Immich API URL** | Your Immich instance URL (e.g., `http://localhost:2283`) |
| **API Token** | Immich API key (Settings → API Keys in Immich) |
| **Sync Folder** | Where symlinks are created. Add this folder as a Jellyfin Photos library. |
| **Sync Interval** | Hours between syncs (1–168, default: 6) |
| **Include Shared Albums** | Also sync albums shared with you |
| **Path Mapping** | Map Immich container paths → host paths (see below) |

### Path Mapping

If Immich runs in Docker, internal paths like `/usr/src/app/upload/...` need to be mapped to the actual host paths where files are stored.

**Example:**

| Setting | Value |
|---------|-------|
| Container path (upload) | `/usr/src/app/upload` |
| Host path (upload) | `/path/to/immich/upload` |
| Container path (external) | `/external/photos` |
| Host path (external) | `/path/to/your/photos` |

## How It Works

1. The plugin fetches all albums (and optionally shared albums) from the Immich API
2. For each album, it creates a folder in the sync directory
3. For each photo in the album:
   - **HEIC/HEIF files** (macOS only): Converted to JPEG with proper orientation via `sips`
   - **All other files**: A symlink is created pointing to the original file
4. Orphaned symlinks and empty album folders are cleaned up
5. Add the sync folder as a **Photos** library in Jellyfin

## Building from Source

```bash
dotnet build -c Release
```

The DLL will be in `bin/Release/net9.0/`.

## Recommended CSS

For a better photo browsing experience in Jellyfin, add this Custom CSS in **Dashboard → General → Custom CSS**:

```css
/* Show full image (no crop) with blurred background */
.card[data-type="PhotoAlbum"] .coveredImage,
.card[data-type="Photo"] .coveredImage {
    background-size: contain !important;
    background-position: center !important;
    background-color: transparent !important;
}
.card[data-type="PhotoAlbum"] .blurhash-canvas,
.card[data-type="Photo"] .blurhash-canvas {
    opacity: 1 !important;
}
```

This displays photos in their original aspect ratio with Jellyfin's blurhash as a colorful blurred background instead of black bars.

## License

GPL-3.0 — See [LICENSE](LICENSE)
