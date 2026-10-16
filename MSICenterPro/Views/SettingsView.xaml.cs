using System.Windows.Controls;
using MSICenterPro.Core;

namespace MSICenterPro.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => EntranceAnim.Play(this);
    }
}
