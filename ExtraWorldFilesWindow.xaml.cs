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
    /// Interaction logic for ExtraWorldFilesWindow.xaml
    /// </summary>
    public partial class ExtraWorldFilesWindow : Window
    {
        public ExtraWorldFilesWindow()
        {
            InitializeComponent();
            foreach (var ext in Properties.Settings.Default.WorldFileExtensions)
            {
                lstExtensions.Items.Add(ext);
            }
        }

        private void menuExtensions_Opened(object sender, RoutedEventArgs e)
        {
            if (lstExtensions.SelectedIndex > -1)
            {
                menuEdit.Visibility = Visibility.Visible;
                menuRemove.Visibility = Visibility.Visible;
            }
            else
            {
                menuEdit.Visibility = Visibility.Collapsed;
                menuRemove.Visibility = Visibility.Collapsed;
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.WorldFileExtensions.Clear();
            foreach (var ext in lstExtensions.Items)
            {
                Properties.Settings.Default.WorldFileExtensions.Add(ext.ToString());
            }
            Properties.Settings.Default.Save();
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void menuRemove_Click(object sender, RoutedEventArgs e)
        {
            lstExtensions.Items.Remove(lstExtensions.SelectedItem);
        }

        private void menuAdd_Click(object sender, RoutedEventArgs e)
        {
            var editWin = new ExtraWorldFileEditWindow();
            editWin.Owner = this;
            editWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (editWin.ShowDialog().GetValueOrDefault())
            {
                var ext = editWin.Tag.ToString();
                if (ext.IndexOf(".") != 0) {
                    ext = "." + ext;
                }
                lstExtensions.Items.Add(ext);
            }
        }

        private void menuEdit_Click(object sender, RoutedEventArgs e)
        {
            var editWin = new ExtraWorldFileEditWindow(lstExtensions.SelectedItem.ToString());
            editWin.Owner = this;
            editWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (editWin.ShowDialog().GetValueOrDefault())
            {
                var ext = editWin.Tag.ToString();
                if (ext.IndexOf(".") != 0)
                {
                    ext = "." + ext;
                }
                lstExtensions.Items[lstExtensions.SelectedIndex] = ext;
            }
        }
    }
}
