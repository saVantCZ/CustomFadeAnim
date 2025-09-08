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

namespace CustomFadeAnim
{
    public partial class CustomFadeAnimSettingsView : UserControl
    {
        public Array SlideDirectionValues => Enum.GetValues(typeof(SlideDirection));
        public CustomFadeAnimSettingsView()
        {
            InitializeComponent();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CustomFadeAnimSettingsViewModel vm)
            {
                vm.ResetCurrentToDefaults();
            }
        }
    }
}