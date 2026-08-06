# StreamFlow

⚠️ Important Notice: StreamFlow is currently in an early alpha stage. We highly recommend conducting extensive off-air testing before using it for live broadcasts, as you may experience stability issues or unexpected behavior.

----

StreamFlow is a Windows desktop app that combines a soundboard/audio mixer with a live-streaming
control surface. It captures your screen/window/camera sources, composites them (with overlays,
chroma key, scenes, and transitions) via a native Rust core, and streams the result to Twitch or
YouTube — while a per-device audio channel strip mixer, backed by
[SoundFlow](https://github.com/LSXPrime/SoundFlow), handles playback, monitoring, and levels.

## Features

- **Go Live**: compose a scene from window/screen/camera captures and overlays (text, image,
  video, timer), with drag/resize/snap placement, chroma key, opacity, and rotation per layer.
- **Scenes**: save, switch between, and transition (fade/slide) multiple scene layouts; export
  and import scene sets.
- **Audio mixer**: per-device channel strips with volume, mute, solo, and live level metering,
  plus an always-present master control for overall stream mix volume.
- **Streaming**: Twitch and YouTube output via OAuth, with a bandwidth-test mode
  (`?bandwidthtest=true` on the stream key) that validates the pipeline without going live to
  followers.
- **Self-updating**: ships via [Velopack](https://velopack.io/) for unpackaged, self-updating
  installs, with an MSIX package as the alternate distribution path.

## Repository layout

| Path | What it is |
| --- | --- |
| `StreamFlow.App` | The WPF application (UI, ViewModels, IPC bridge to the native core). |
| `StreamFlow.Core` | Shared audio-handling, persistence, and protocol logic. |
| `StreamFlow.Shared` | Types shared across projects. |
| `StreamFlow.App.Package` | MSIX packaging project (Windows Application Packaging). |
| `StreamFlow.StreamDeck` | Companion Elgato Stream Deck plugin (TypeScript, early scaffold). |
| `StreamFlow.Tests` | Unit tests. |
| `native` | Rust workspace: the capture/compositing/streaming core, talking to the app over IPC. |
| `SoundFlow`, `atldotnet`, `libxmpBindings` | Git submodules (audio engine, tag reading, module playback bindings). |

## Prerequisites

- Windows 10 (17763+) or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Rust](https://rustup.rs/) with the `x86_64-pc-windows-msvc` target
  (`rustup target add x86_64-pc-windows-msvc`)
- FFmpeg dev libraries (headers + `.lib`s) — path configured via `FFMPEG_DIR` in
  `native/.cargo/config.toml`. CI fetches
  [BtbN's FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds); locally, download a matching
  `-shared` build and point `FFMPEG_DIR` at it.
- Visual Studio 2022 (or Build Tools) with the "Windows Application Packaging" and ".NET desktop
  development" workloads, if building the MSIX package.

## Getting started

```powershell
git clone --recurse-submodules https://github.com/streamfloworg/StreamFlow.git
cd StreamFlow
dotnet build StreamFlow.sln
```

The native Rust core builds automatically as part of `StreamFlow.App`'s build (a
`BeforeTargets="Build"` MSBuild target invokes `cargo build`), so a plain `dotnet build` is
enough — no separate `cargo build` step required unless you're iterating on `native/` in
isolation, in which case `cargo build` (or `cargo build --release`) from the `native` directory
is faster.

If you cloned without `--recurse-submodules`, run:

```powershell
git submodule update --init --recursive
```

### Running

Launch `StreamFlow.App` from Visual Studio or `dotnet run --project StreamFlow.App`.

**or**

run the executable from the build folder

## License

[GNU General Public License v3.0](LICENSE)
