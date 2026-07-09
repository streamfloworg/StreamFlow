# StreamFlow URI Protocol Guide

## Overview
StreamFlow now supports deep linking via the `streamflow://` URI protocol. This allows external applications, web browsers, and shortcuts to launch StreamFlow and directly play specific audio tracks with optional loop points and starting positions.

## URI Format
```
streamflow://play/{audioId}?loop={loopIdentifier}&position={seconds}
```

### Parameters
- **audioId** (required): The unique 8-character ID of the audio track
- **loop** (optional): The loop point ID (4 characters) or loop point name
- **position** (optional): Starting position in seconds (decimal number)

## Examples

### Basic - Play a song
```
streamflow://play/AbC123De
```
Opens StreamFlow and plays the audio track with ID "AbC123De".

### With Loop Point (by ID)
```
streamflow://play/AbC123De?loop=L1aB
```
Plays the song and activates the loop point with ID "L1aB".

### With Loop Point (by Name)
```
streamflow://play/AbC123De?loop=intro
```
Plays the song and activates the loop point named "intro" (case-insensitive).

### With Starting Position
```
streamflow://play/AbC123De?position=30
```
Plays the song starting at 30 seconds.

### Combined - Loop and Position
```
streamflow://play/AbC123De?loop=chorus&position=45
```
Plays the song, activates the "chorus" loop, and seeks to 45 seconds.

## How to Get Audio IDs

### In Code
Each `Audio` object now has an `Id` property that is automatically generated when first accessed:
```csharp
var audio = AppModel.Instance.Audios.First();
string audioId = audio.Id; // e.g., "AbC123De"
```

### In UI (Future Enhancement)
Consider adding a context menu item or button to copy the audio's URI to clipboard:
```csharp
var uri = StreamFlowUri.Build(audio.Id);
Clipboard.SetText(uri);
```

## How to Get Loop Point IDs

Each `LoopPoint` also has an auto-generated `Id` property:
```csharp
var loopPoint = audioTrack.LoopPoints.First();
string loopId = loopPoint.Id; // e.g., "L1aB"
string loopName = loopPoint.Name; // e.g., "intro"
```

## Testing the Protocol

### Method 1: HTML File
Create an HTML file with test links:
```html
<!DOCTYPE html>
<html>
<head><title>StreamFlow URI Test</title></head>
<body>
    <h1>StreamFlow URI Protocol Test</h1>
    <ul>
        <li><a href="streamflow://play/TEST1234">Test Song 1</a></li>
        <li><a href="streamflow://play/TEST1234?loop=intro">Test Song 1 with Loop</a></li>
        <li><a href="streamflow://play/TEST1234?position=15">Test Song 1 at 15s</a></li>
        <li><a href="streamflow://play/TEST1234?loop=L1aB&position=30">Test Song 1 Full</a></li>
    </ul>
</body>
</html>
```

### Method 2: Windows Run Dialog
1. Press `Win + R`
2. Type: `streamflow://play/AbC123De`
3. Press Enter

### Method 3: Command Prompt
```cmd
start streamflow://play/AbC123De
```

### Method 4: PowerShell
```powershell
Start-Process "streamflow://play/AbC123De"
```

## Protocol Registration

The protocol is automatically registered on first launch:
- Registry location: `HKEY_CURRENT_USER\Software\Classes\streamflow`
- No administrator privileges required
- Can be manually unregistered using `ProtocolRegistration.UnregisterProtocol()`

## Single Instance Behavior

StreamFlow enforces single-instance behavior:
1. **First launch**: Normal startup, registers protocol, listens for additional URIs
2. **Subsequent launches**: Send URI to first instance via named pipe, then exit
3. **First instance**: Receives URI, brings window to front, processes URI

## Implementation Details

### Architecture Components
- **StreamFlowUri** (`StreamFlow.Core/Protocol/StreamFlowUri.cs`): URI parsing and building
- **ProtocolHandlerService** (`StreamFlow.App/Services/ProtocolHandlerService.cs`): Processes URIs and controls playback
- **SingleInstanceManager** (`StreamFlow.App/Services/SingleInstanceManager.cs`): Enforces single instance with IPC
- **ProtocolRegistration** (`StreamFlow.App/Services/ProtocolRegistration.cs`): Windows Registry management

### Communication Flow
```
Browser/Link
    ↓
Windows Shell (reads HKCU registry)
    ↓
Launches StreamFlow.exe with URI argument
    ↓
SingleInstanceManager checks if first instance
    ↓ (if second instance)
Send args via named pipe → First instance receives → Exit
    ↓ (if first instance)
ProcessCommandLineArgsAsync()
    ↓
ProtocolHandlerService.HandleUriAsync()
    ↓
FindAudioById() → LoadAndPlayAudioAsync() → ApplyLoopPoint() → Seek()
```

## Building URIs Programmatically

Use the static `StreamFlowUri.Build()` method:
```csharp
// Simple
var uri1 = StreamFlowUri.Build("AbC123De");
// Result: "streamflow://play/AbC123De"

// With loop
var uri2 = StreamFlowUri.Build("AbC123De", loopIdentifier: "intro");
// Result: "streamflow://play/AbC123De?loop=intro"

// With position
var uri3 = StreamFlowUri.Build("AbC123De", positionSeconds: 30);
// Result: "streamflow://play/AbC123De?position=30"

// Full
var uri4 = StreamFlowUri.Build("AbC123De", "chorus", 45);
// Result: "streamflow://play/AbC123De?loop=chorus&position=45"
```

## Parsing URIs

Use the static `StreamFlowUri.Parse()` or `StreamFlowUri.TryParse()` methods:
```csharp
// Parse with exceptions
var parsed = StreamFlowUri.Parse("streamflow://play/AbC123De?loop=intro");
if (parsed.IsValid)
{
    Console.WriteLine($"Audio ID: {parsed.AudioId}");
    Console.WriteLine($"Loop: {parsed.LoopIdentifier}");
}

// Safe parsing
if (StreamFlowUri.TryParse("streamflow://play/TEST", out var result))
{
    // Use result
}
```

## Troubleshooting

### Protocol not registered
- Check `ProtocolRegistration.IsProtocolRegistered()` returns true
- Verify registry key exists: `HKCU\Software\Classes\streamflow`
- Try manually calling `ProtocolRegistration.RegisterProtocol()`

### Second instance not closing
- Check if `SingleInstanceManager` is properly initialized
- Verify named pipe is not blocked by antivirus
- Check for exceptions in Debug output

### Audio not playing
- Verify audio ID exists in `AppModel.Instance.Audios`
- Check that `AudioViewModel` is properly injected into `ProtocolHandlerService`
- Look for error notifications from `ProtocolHandlerService`

### Loop point not applied
- Verify loop point exists in `audioTrack.LoopPoints`
- Check ID or name matches (names are case-insensitive)
- Ensure loop point has valid start/end times

## Future Enhancements
- Add more parameters: volume, fadeIn, repeat, etc.
- Support playlist URIs: `streamflow://playlist/{playlistId}`
- Scene loading: `streamflow://scene/{sceneId}`
- Quick actions: `streamflow://action/play`, `streamflow://action/stop`
- Export/import functionality via URIs
