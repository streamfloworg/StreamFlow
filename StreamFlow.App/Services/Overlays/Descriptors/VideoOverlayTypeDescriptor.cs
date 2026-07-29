using System.ComponentModel;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class VideoOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "video";
    public OverlayKind Kind => OverlayKind.Video;
    public string DisplayName => "Video Overlay";
    public string IconGlyph => "🎬";
    public OverlaySessionMode SessionMode => OverlaySessionMode.OngoingCoreSession;

    public IOverlayContent CreateDefault() => new VideoOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var chromaColor = s.ChromaKeyColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.ChromaKeyColorHex) is System.Windows.Media.Color parsedColor
            ? parsedColor
            : System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40);

        return new VideoOverlayContent
        {
            VideoPath = s.VideoPath,
            LoopVideo = s.LoopVideo,
            ChromaKeyEnabled = s.ChromaKeyEnabled,
            ChromaKeySimilarity = s.ChromaKeySimilarity,
            ChromaKeyColor = chromaColor
        };
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is VideoOverlayContent video)
        {
            s.OverlayKind = OverlayKind.Video;
            s.VideoPath = video.VideoPath;
            s.LoopVideo = video.LoopVideo;
            s.ChromaKeyEnabled = video.ChromaKeyEnabled;
            s.ChromaKeySimilarity = video.ChromaKeySimilarity;
            s.ChromaKeyColorHex = video.ChromaKeyColor.ToString();
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is VideoOverlayContent video)
        {
            return new VideoOverlayContent
            {
                VideoPath = video.VideoPath,
                LoopVideo = video.LoopVideo,
                ChromaKeyEnabled = video.ChromaKeyEnabled,
                ChromaKeySimilarity = video.ChromaKeySimilarity,
                ChromaKeyColor = video.ChromaKeyColor
            };
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, object? slotContext) => null;

    public void HookPropertyChanges(IOverlayContent content, Action<string> onPropertyChanged)
    {
        if (content is INotifyPropertyChanged notifying)
        {
            notifying.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                    onPropertyChanged(e.PropertyName);
            };
        }
    }

    public IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> GetInspectorSections(IOverlayContent content)
    {
        if (content is not VideoOverlayContent vid) return [];
        return [
            new StreamFlow.Plugin.SDK.Overlays.Sections.FilePickerSection("Video File", () => vid.VideoPath, v => vid.VideoPath = v, "Videos|*.mp4;*.mov;*.mkv;*.webm;*.avi", vid, nameof(vid.VideoPath)),
            new StreamFlow.Plugin.SDK.Overlays.Sections.ToggleSection("Loop", () => vid.LoopVideo, v => vid.LoopVideo = v, vid, nameof(vid.LoopVideo)),
            new StreamFlow.App.Services.Overlays.Sections.ChromaKeySection(vid)
        ];
    }
}
