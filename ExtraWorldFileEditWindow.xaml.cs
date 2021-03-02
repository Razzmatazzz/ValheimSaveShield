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
using System.Windows.Shapes;

namespace ValheimSaveShield
{
    /// <summary>
    /// Interaction logic for ExtraWorldFileEditWindow.xaml
    /// </summary>
    public partial class ExtraWorldFileEditWindow : Window
    {
        public ExtraWorldFileEditWindow(string ext)
        {
            InitializeComponent();
            if (ext != null) txtExtension.Text = ext;
        }
        public ExtraWorldFileEditWindow() : this(null) { }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Tag = txtExtension.Text;
            Close();
        }
    }
}
