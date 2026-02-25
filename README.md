# Krypton Clipboard Manager

A cross-platform clipboard manager with server sync, image capture, and a hotkey popup — desktop client built with Avalonia UI, server built with ASP.NET Core.

![License](https://img.shields.io/badge/license-Source%20First%201.1-blue)

---

## Features

- **Clipboard history** — persists text and images across sessions
- **Image + text capture** — captures both text and image content from the clipboard
- **Server sync** — syncs clipboard history to a self-hosted server over TCP/protobuf (TLS supported)
- **Cross-platform** — Windows, Linux, macOS desktop client; Linux server
- **Self-update** — detects and installs new versions from GitHub Releases
- **Hotkey popup** — quickly access clipboard history without leaving the keyboard

---

## Installation

### Windows

| Variant | Download |
|---------|----------|
| Setup (recommended) | [krypton-desktop-win-x64-setup.exe](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-desktop-win-x64-setup.exe) |
| Portable EXE | [krypton-desktop-win-x64-portable.exe](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-desktop-win-x64-portable.exe) |

The **Setup** installer bundles two variants selectable during installation:
- **Self-contained** — includes the .NET 9 runtime; no prerequisites
- **Framework-dependent** — smaller download; requires [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)

The **Portable EXE** is a single-file, framework-dependent build. It requires the .NET 9 Desktop Runtime to be installed separately.

### Linux / macOS

| Platform | Download |
|----------|----------|
| Linux x64 | [krypton-desktop-linux-x64](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-desktop-linux-x64) |
| macOS Intel (x64) | [krypton-desktop-osx-x64.zip](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-desktop-osx-x64.zip) |
| macOS Apple Silicon (arm64) | [krypton-desktop-osx-arm64.zip](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-desktop-osx-arm64.zip) |

All Linux/macOS builds are portable single-file binaries (framework-dependent). Requires the [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).

On Linux, mark the binary as executable before running:
```bash
chmod +x krypton-desktop-linux-x64
./krypton-desktop-linux-x64
```

---

## Server Setup

Download the server binary for your platform:

| Platform | Download |
|----------|----------|
| Linux x64 | [krypton-server-linux-x64](https://github.com/Arimodu/Krypton-Clipboard-Manager/releases/latest/download/krypton-server-linux-x64) |

### Quick Start

```bash
chmod +x krypton-server-linux-x64
./krypton-server-linux-x64 setup   # Interactive setup wizard
./krypton-server-linux-x64 start   # Start the server
```

### CLI Commands

| Command | Description |
|---------|-------------|
| `krypton-server setup` | Run the interactive setup wizard (creates config, database, optional systemd service) |
| `krypton-server start` | Start the server in the foreground |
| `krypton-server upgrade` | Download and install the latest server release from GitHub |

---

## Connecting a Client

1. Open Krypton on the desktop
2. Open **Settings** and navigate to **Server**
3. Enter your server address (e.g., `myserver.example.com:5000`)
4. Enter the API key configured during server setup
5. Click **Connect**

---

## Building from Source

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build

```bash
# Clone the repository
git clone https://github.com/Arimodu/Krypton-Clipboard-Manager.git
cd Krypton-Clipboard-Manager

# Build the entire solution
dotnet build

# Run the desktop client
dotnet run --project Krypton-Desktop

# Run the server
dotnet run --project Krypton-Server
```

### Release Build

```bash
dotnet build --configuration Release
```

---

## Architecture

```
┌──────────────────────────────────────┐
│  Krypton-Desktop (Avalonia + MVVM)   │
│  - Clipboard monitor (text + image)  │
│  - Hotkey popup window               │
│  - Tray icon + update notifications  │
└────────────────┬─────────────────────┘
                 │ TCP / protobuf (TLS)
                 │ Krypton.Shared protocol
┌────────────────▼─────────────────────┐
│  Krypton-Server (ASP.NET Core)       │
│  - SQLite / PostgreSQL storage       │
│  - Image filesystem storage          │
│  - API key authentication            │
│  - Cleanup service                   │
└──────────────────────────────────────┘
```

**Solution structure**:
- `Krypton-Desktop/` — Avalonia desktop client (WinExe, .NET 9)
- `Krypton-Server/` — Backend console application (.NET 9)
- `Krypton-Shared/` — Shared protobuf protocol library

---

## License

This project is licensed under the [Source First License 1.1](LICENSE.md).
