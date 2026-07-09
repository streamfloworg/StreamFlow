using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SoundFlow.Abstracts;
using SoundFlow.Editing;

namespace StreamFlow.App.ViewModels.Pages.Compose;
public class CompositionEditorViewModel
{
    private Composition CurrentComposition { get; set; }

    private List<AudioSegment> AudioSegments { get; set; }

}
