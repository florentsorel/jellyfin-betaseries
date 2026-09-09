<p align="center">
  <img src="docs/assets/betaseries_cover.jpg" alt="BetaSeries Plugin for Jellyfin" width="720" />
</p>

# Jellyfin Plugin BetaSeries

<p align="center">
  <a href="https://github.com/florentsorel/jellyfin-betaseries/actions/workflows/test.yaml">
    <img src="https://github.com/florentsorel/jellyfin-betaseries/actions/workflows/test.yaml/badge.svg" alt="Test Status" />
  </a>
  <a href="https://github.com/florentsorel/jellyfin-betaseries/releases">
    <img src="https://img.shields.io/github/v/release/florentsorel/jellyfin-betaseries?color=blue&label=release" alt="Latest Release" />
  </a>
  <img src="https://img.shields.io/badge/dynamic/yaml?url=https://raw.githubusercontent.com/florentsorel/jellyfin-betaseries/master/build.yaml&query=$.targetAbi&label=Jellyfin&color=purple&prefix=%3E%3D%20" alt="Jellyfin Version" />
  <img src="https://img.shields.io/badge/dynamic/yaml?url=https://raw.githubusercontent.com/florentsorel/jellyfin-betaseries/master/build.yaml&query=$.framework&label=.NET&color=512bd4" alt=".NET Version" />
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/license-GPL--3.0-green.svg" alt="License: GPL-3.0" />
  </a>
</p>

This plugin for **[Jellyfin](https://jellyfin.org)** automatically synchronizes your **movies**, **shows**, **seasons**, and **episodes** watch status with your **[BetaSeries](https://www.betaseries.com)** account.

---

## 🌟 Features

- 🔄 **Automatic Scrobbling on Playback Stop:**
  - As soon as an episode or movie reaches the completion threshold configured in Jellyfin (`PlaybackStopped`), the item is automatically marked as watched on your BetaSeries account with the exact playback timestamp (in UTC).
  - Paused or partially watched media items are safely ignored.

- ⚡ **Manual Sync Actions (Mark Watched / Unwatched):**
  - Manually marking a movie or episode as watched in Jellyfin immediately synchronizes it to BetaSeries.
  - **Unmarking** a movie or episode in Jellyfin automatically deletes it from your BetaSeries history (`DELETE`).
  - **Full Series & Seasons Support:** Marking or unmarking an entire season or show propagates the action to all relevant episodes.

- 🛡️ **History Protection (`bulk=false`):**
  - Episode watch requests are explicitly sent with `bulk=false`. This guarantees that BetaSeries will **never** inadvertently mark missing earlier episodes in your library, faithfully preserving your actual progress and historical watch dates.

- 👥 **Multi-User & Multi-Account Management:**
  - Create as many BetaSeries profiles as needed.
  - Map one or more Jellyfin users to each BetaSeries profile (or share a single BetaSeries account across multiple local users).
  - Individual profile settings: toggle movie sync, show sync, live playback scrobble, and manual sync independently.

- 🔀 **Intelligent Metadata Fallbacks:**
  - **Movies:** lookup via TMDB ID with automatic fallback to IMDb ID for missing matches or HTTP 4001 errors.
  - **Shows:** lookup via TVDB ID, fallback to IMDb ID, and fallback to title search validated by TMDB ID.
  - **Episodes:** direct lookup via episode TVDB ID or resolution by season and episode number (SxxExx).

- ⏱️ **Debounce & Idempotency Handling:**
  - An in-memory 30-second debounce cache prevents duplicate or rapid-fire API requests during simultaneous events.
  - BetaSeries API responses indicating an item is already watched or already unwatched are handled gracefully without generating errors.

- 🔐 **Streamlined OAuth2 Authentication:**
  - Link your account directly from the plugin configuration page using an OAuth2 popup authorization window.

---

## ⚙️ Compatibility & Prerequisites

### Compatibility Matrix

| Plugin Version | Jellyfin Version | .NET Runtime | Branch / Status |
| :--- | :--- | :--- | :--- |
| **`2.x`** | **Jellyfin $\ge$ 12.0.0** | **.NET 10** | `master` (Current) |
| **`1.x`** | Jellyfin 10.9.x - 10.11.x | .NET 9 | [`v1.x`](https://github.com/florentsorel/jellyfin-betaseries/tree/v1.x) (Maintenance) |

### Prerequisites

1. A compatible **Jellyfin server** (see compatibility matrix above).
2. A **[BetaSeries](https://www.betaseries.com)** account.
3. A BetaSeries API Key (Client ID) & Client Secret:
   - Go to **[https://www.betaseries.com/en/account/api](https://www.betaseries.com/en/account/api)**.
   - Register an application to obtain your **Application Key** (Client ID) and **Secret**.

---

## 🚀 Jellyfin Configuration

1. In Jellyfin, navigate to **Dashboard** > **Plugins** > **BetaSeries**.
2. **Application Settings:**
   - Enter your **Client ID** (API Key).
   - Enter your **Client Secret** (OAuth Secret).
   - Click **Save API settings**.
3. **Adding a BetaSeries Profile:**
   - Click **+ Add a profile**.
   - Enter a name for the profile (e.g., *My BetaSeries Profile*).
   - Select the associated Jellyfin user(s).
   - Configure your desired options (Sync series, Sync movies, Scrobble playback stop, Sync manual marks).
   - Click **🔗 Connect with BetaSeries (OAuth2)** to authorize and link your account via the popup window.
   - Click **Save this profile**.

---

## 📦 Installation

### Method 1: Via Jellyfin Plugin Repository (Recommended)

1. In your Jellyfin web interface, navigate to **Dashboard** > **Plugins** > **Repositories** tab.
2. Click the **+** button to add a new repository:
   - **Repository Name:** `BetaSeries`
   - **Repository URL:** `https://raw.githubusercontent.com/florentsorel/jellyfin-betaseries/master/manifest.json`
3. Switch to the **Catalog** tab, find **BetaSeries**, and click **Install**.
4. Restart your Jellyfin server.

### Method 2: Manual Installation

1. Head to the [Releases](https://github.com/florentsorel/jellyfin-betaseries/releases) page and download the latest release archive.
2. Create a `BetaSeries` directory inside your Jellyfin plugins folder:
   - **Linux:** `/var/lib/jellyfin/plugins/BetaSeries/`
   - **Windows:** `%ProgramData%\Jellyfin\Server\plugins\BetaSeries\`
   - **Docker:** `/config/plugins/BetaSeries/`
3. Extract `Jellyfin.Plugin.BetaSeries.dll` and `betaseries_cover.jpg` into that folder.
4. Restart the Jellyfin server:
   ```bash
   sudo systemctl restart jellyfin
   ```

---

## 🛠️ Development & Testing

### Build

```bash
# Build in Release mode
dotnet build -c Release
```

### Running Tests

The project includes a comprehensive unit test suite (xUnit, Moq) that isolates all HTTP interactions through a hermetic mock handler (`MockHttpMessageHandler`):

```bash
# Run all unit tests
dotnet test
```

Test coverage includes:
- Configuration and profile validation (`PluginConfiguration`, XML serialization).
- API Client (`BetaSeriesClient`): movie, show, and episode resolution, scrobbling with UTC timestamp, `bulk=false`, unmarking, handling of 4001 error codes and HTTP errors.
- Event Manager (`BetaSeriesManager`): listening to `PlaybackStopped`, handling `UserDataSaved`, filtering duplicate `PlaybackFinished` events, in-memory debouncing, and processing full seasons and shows.

---

## 📄 License

This project is licensed under the terms of the [GPL-3.0](LICENSE) license.
