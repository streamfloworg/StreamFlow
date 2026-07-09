using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using StreamFlow.App.ViewModels.Pages.Compose;

namespace StreamFlow.App.Controls.Compose;
/// <summary>
/// Interaction logic for CompositionSegment.xaml
/// </summary>
public partial class CompositionSegment : System.Windows.Controls.UserControl
{
    public CompositionSegment(CompositionEditorViewModel ViewModel)
    {
        InitializeComponent();
    }

    
}
