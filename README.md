<p align="center">
  <img src="Assets\logo.png" width="70" alt="Fluent Scrobbler Logo" />
</p>

<h1 align="center">FluentScrobbler for Last.fm blocked countries</h1>

> **Fork:** a fork of [Fluent Scrobbler](https://github.com/snw-mint/fluent-scrobbler) with a built-in SOCKS5 proxy for countries where Last.fm is blocked.
> The proxy is configured in **Account → Proxy** (`Disabled` by default, `Manual` or `Auto` with a remote list of free SOCKS5 servers).
> <img width="1915" height="1042" alt="UI proxy" src="https://github.com/user-attachments/assets/ae6de7af-254d-43b8-ab7f-5710cfb838b7" />


---
<p align="center">
  <strong>A Lightweight, and Modern Scrobbler for Windows 10 & 11</strong>
</p>

![Fluent Scrobbler](Repo/mockup.webp)



<p align="center">
  <a href="https://snw-mint.github.io/fluent-scrobbler/">Overview</a> •
  <a href="https://snw-mint.github.io/fluent-scrobbler/privacy.html">Privacy</a> •
  <a href="https://snw-mint.github.io/fluent-scrobbler/terms.html">Terms</a>
</p>



---

## Features

- **Automatic System Media Scrobbling**: Captures currently playing tracks across desktop apps and browsers using Windows System Media Transport Controls.
- **Last.fm Integration**: Seamless authentication, real-time scrobbling, and "Now Playing" status updates.
- **Scrobble History**: View your recent scrobbles and track status directly inside the application.
- **Native Fluent Design**: Built with WinUI 3 for a modern Windows 11 design aesthetic with dark and light mode support.
- **Background Execution & Startup**: Runs quietly in the system background with optional auto-start on Windows startup.

## Installation & Setup

1. **Download**: Visit the [Releases](https://github.com/Skylake-unreachable/fluent-scrobbler-for-Last.fm-blocked-countries/releases) page and download the setup installer (`FluentScrobbler-Setup-x64.exe / FluentScrobbler-Setup-arm64.exe`).
2. **Install**: Run the installer and follow the on-screen instructions.
3. **Connect Account**: Open Fluent Scrobbler, navigate to the **Account** section, and click **Connect to Last.fm** to authorize the application.
4. **Start Scrobbling**: Play music in any Windows media player or web browser, and your listening history will scrobble automatically.

> [!NOTE]
> **VLC Media Player**: VLC does not support Windows Media Transport Controls (SMTC) by default. To enable scrobbling from VLC, install the [vlc-win10smtc plugin](https://github.com/spmn/vlc-win10smtc/releases).

## System Specifications & Tech Stack

### System Requirements

- **Operating System**: Windows 10 (version 1809 / build 17763 or higher) or Windows 11
- **Architecture**: x64

### Technical Specifications

- **Language**: C# (.NET 8.0)
- **UI Framework**: WinUI 3 / Windows App SDK 1.5+
- **Media API**: Windows.Media.Control (GlobalSystemMediaTransportControlsSessionManager)

## Privacy

I value your privacy. Please read the [Privacy Policy](https://snw-mint.github.io/fluent-scrobbler/privacy.html) to more information.

## Feedback & Bug Reports

Because this project is in Beta, user feedback is very valuable to me:

- To report a bug or request a new feature, please open an issue on [GitHub Issues](https://github.com/Skylake-unreachable/fluent-scrobbler-for-Last.fm-blocked-countries/issues).
- For security concerns, please refer to my [Security Policy](SECURITY.md).

## License & Legal Notice

This project is licensed under the **GNU General Public License v3.0** - see the [LICENSE](LICENSE) file for details.

### Legal Disclaimer

Fluent Scrobbler is an independent open-source application developed by me. It is not affiliated with, endorsed by, or sponsored by Last.fm, CBS Interactive. All trademarks and brand names belong to their respective owners.
