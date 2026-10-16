using System.Windows.Controls;
using MSICenterPro.Core;

namespace MSICenterPro.Views;

public partial class PerformanceView : UserControl
{
    public PerformanceView()
    {
        InitializeComponent();
        Loaded += (_, _) => EntranceAnim.Play(this);
    }
}
