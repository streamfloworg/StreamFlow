# URI Protocol Implementation - Complete ✅

## Summary
Successfully implemented a complete URI protocol system for StreamFlow, enabling deep linking via `streamflow://` URIs.

## What Was Implemented

### 1. Unique Identifiers ✅
- **Audio.Id** (8-character): Auto-generated URL-safe ID for each audio track
- **LoopPoint.Id** (4-character): Auto-generated URL-safe ID for each loop point
- Both use base64-encoded GUIDs with special characters removed
- IDs persist across sessions via JSON serialization

### 2. URI Parsing Model ✅
- **StreamFlowUri** (`StreamFlow.Core/Protocol/StreamFlowUri.cs`)
  - Parse() and TryParse() for safe URI parsing
  - Build() for constructing valid URIs
  - Validates format: `streamflow://play/{audioId}?loop={loopId}&position={seconds}`
  - Handles query parameters with proper escaping

### 3. Protocol Handler Service ✅
- **ProtocolHandlerService** (`StreamFlow.App/Services/ProtocolHandlerService.cs`)
  - HandleUriAsync() processes incoming URIs
  - FindAudioById() searches audio collection
  - LoadAndPlayAudioAsync() uses existing AudioViewModel.PlayAudioCommand
  - ApplyLoopPoint() matches by ID or name (case-insensitive)
  - Shows notifications for success/error feedback

### 4. Single Instance Manager ✅
- **SingleInstanceManager** (`StreamFlow.App/Services/SingleInstanceManager.cs`)
  - Uses Mutex for instance detection
  - Named pipes for inter-process communication
  - First instance listens, subsequent instances send args and exit
  - ArgumentsReceived event for cross-instance communication

### 5. Protocol Registration ✅
- **ProtocolRegistration** (`StreamFlow.App/Services/ProtocolRegistration.cs`)
  - RegisterProtocol() creates Windows Registry entries
  - Uses HKEY_CURRENT_USER (no admin required)
  - IsProtocolRegistered() checks existing registration
  - UnregisterProtocol() for cleanup

### 6. Application Integration ✅
- **App.xaml.cs** updated with:
  - SingleInstanceManager initialization in OnStartup
  - Protocol registration on first launch
  - Command-line argument processing
  - ArgumentsReceived event handling
  - ProtocolHandlerService registered in DI container

### 7. Helper Extensions ✅
- **UriExtensions** (`StreamFlow.App/Helpers/UriExtensions.cs`)
  - ToStreamFlowUri() extension for Audio and LoopPoint
  - CopyUriToClipboard() for easy sharing
  - Simplifies URI generation in UI code

## How to Test

### Step 1: Run StreamFlow
Launch StreamFlow at least once to register the protocol in Windows Registry.

### Step 2: Get Audio IDs
In your code or debugger, get an audio ID:
```csharp
var audio = AppModel.Instance.Audios.First();
string audioId = audio.Id; // e.g., "AbC123De"
Console.WriteLine(audioId);
```

### Step 3: Test with HTML File
Open `test-uri-protocol.html` in a browser:
1. Replace placeholder IDs with your actual audio IDs
2. Click any test link
3. StreamFlow should open/activate and play the audio

### Step 4: Test Single Instance
1. Launch StreamFlow
2. Click another URI link
3. Verify: window comes to front, new audio plays, no second instance

### Step 5: Advanced Testing
Test various URI formats:
- Basic: `streamflow://play/AbC123De`
- With loop: `streamflow://play/AbC123De?loop=intro`
- With position: `streamflow://play/AbC123De?position=30`
- Combined: `streamflow://play/AbC123De?loop=chorus&position=45`

## Usage Examples

### In Code - Generate URI
```csharp
// Using extension method
var uri = audio.ToStreamFlowUri(loopPoint, positionSeconds: 30);

// Using static method
var uri = StreamFlowUri.Build("AbC123De", "intro", 30);

// Copy to clipboard
if (audio.CopyUriToClipboard(loopPoint))
{
    MainWindow.ShowNotification("URI Copied", "Audio URI copied to clipboard", InfoBarSeverity.Success);
}
```

### In UI - Context Menu
Add to AudioView.xaml context menu:
```xml
<MenuItem Header="Copy URI" Command="{Binding CopyAudioUriCommand}">
    <MenuItem.Icon>
        <iui:PathIcon Data="{StaticResource LinkIcon}"/>
    </MenuItem.Icon>
</MenuItem>
```

In AudioViewModel.cs:
```csharp
[RelayCommand]
private void CopyAudioUri()
{
    if (SelectedAudio != null)
    {
        if (SelectedAudio.CopyUriToClipboard())
        {
            MainWindow.ShowNotification("URI Copied", 
                $"URI for '{SelectedAudio.Name}' copied to clipboard", 
                InfoBarSeverity.Success);
        }
    }
}
```

## File Changes Made

### New Files Created
1. `StreamFlow.Core/Protocol/StreamFlowUri.cs` - URI parsing model
2. `StreamFlow.App/Services/ProtocolHandlerService.cs` - Protocol handler
3. `StreamFlow.App/Services/SingleInstanceManager.cs` - Single instance enforcement
4. `StreamFlow.App/Services/ProtocolRegistration.cs` - Windows Registry management
5. `StreamFlow.App/Helpers/UriExtensions.cs` - Convenience extensions
6. `URI_PROTOCOL_GUIDE.md` - Comprehensive documentation
7. `test-uri-protocol.html` - Interactive test page

### Modified Files
1. `StreamFlow.Core/AudioHandling/Audio.cs` - Added Id property
2. `StreamFlow.Core/AudioProperties/LoopPoint.cs` - Added Id property
3. `StreamFlow.App/App.xaml.cs` - Integrated all protocol components

## Architecture Flow

```
User clicks URI link
    ↓
Windows Shell reads registry (HKCU\Software\Classes\streamflow)
    ↓
Launches StreamFlow.exe with URI argument
    ↓
SingleInstanceManager checks mutex
    ↓
If second instance:
    - Send args via named pipe to first instance
    - Exit
    ↓
If first instance:
    - Receive args via pipe (or direct from startup)
    - Parse URI with StreamFlowUri
    - ProtocolHandlerService.HandleUriAsync()
    - Find audio by ID
    - Use AudioViewModel.PlayAudioCommand
    - Apply loop point (if specified)
    - Seek to position (if specified)
    - Show success notification
```

## Registry Structure

```
HKEY_CURRENT_USER\Software\Classes\streamflow
├── (Default) = "URL:StreamFlow Protocol"
├── URL Protocol = ""
├── DefaultIcon
│   └── (Default) = "C:\Path\To\StreamFlow.exe,0"
└── shell
    └── open
        └── command
            └── (Default) = "C:\Path\To\StreamFlow.exe" "%1"
```

## Future Enhancements (Suggested)

### UI Improvements
- [ ] Add "Copy URI" button in AudioView context menu
- [ ] Add "Share" button with URI options
- [ ] Display audio ID in properties dialog
- [ ] Loop point ID display in loop selection UI

### Additional Parameters
- [ ] Volume: `?volume=0.5`
- [ ] Fade in: `?fadein=2`
- [ ] Repeat: `?repeat=true`
- [ ] Next track: `?next={audioId}`

### New Actions
- [ ] Playlist: `streamflow://playlist/{playlistId}`
- [ ] Scene: `streamflow://scene/{sceneId}`
- [ ] Quick actions: `streamflow://action/play`, `streamflow://action/stop`
- [ ] Search: `streamflow://search?q={query}`

### Integration
- [ ] Web API for generating shareable links
- [ ] Export scene as URI
- [ ] QR code generation for URIs
- [ ] Markdown export with embedded URIs

## Testing Checklist

- [x] Build successful (no errors)
- [ ] Protocol registered in Windows Registry
- [ ] First launch registers protocol correctly
- [ ] Second instance sends args and exits
- [ ] First instance receives args from second instance
- [ ] Window comes to front when URI clicked
- [ ] Audio loads and plays from URI
- [ ] Loop point applied correctly (by ID)
- [ ] Loop point applied correctly (by name)
- [ ] Position seeking works
- [ ] Combined parameters work (loop + position)
- [ ] Invalid URI shows error notification
- [ ] Non-existent audio ID shows error notification
- [ ] Non-existent loop ID/name shows warning (but plays audio)
- [ ] Extension methods work for URI generation
- [ ] Clipboard copy works

## Notes

- Protocol registration requires no administrator privileges (uses HKCU)
- Single instance is enforced via Mutex and Named Pipes
- Audio and LoopPoint IDs are automatically generated and persist
- URI format follows RFC 3986 standards
- All query parameters are optional
- Loop matching tries ID first, then falls back to name (case-insensitive)
- Position values are in seconds (supports decimals)
- Integration is non-invasive (minimal changes to existing code)

## Support

For issues or questions:
1. Check `URI_PROTOCOL_GUIDE.md` for detailed documentation
2. Use `test-uri-protocol.html` for quick testing
3. Review Debug output for protocol handler messages
4. Check Windows Event Viewer for registration issues

---

**Implementation Status: COMPLETE ✅**
**Build Status: SUCCESS ✅**
**Ready for Testing: YES ✅**
