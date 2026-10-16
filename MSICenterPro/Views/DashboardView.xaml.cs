using System.Windows.Controls;
using MSICenterPro.Core;

namespace MSICenterPro.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => EntranceAnim.Play(this);
    }
}
