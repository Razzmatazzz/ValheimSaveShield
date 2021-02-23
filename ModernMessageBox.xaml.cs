using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ValheimSaveShield
{
    /// <summary>
    /// Interaction logic for ModernMessageBox.xaml
    /// </summary>
    public partial class ModernMessageBox : Window
    {
        private MessageBoxResult _result;
        private static MessageBoxResult _defaultResult = MessageBoxResult.Cancel;
        public MessageBoxResult Result
        {
            get
            {
                return _result;
            }
        }
        public ModernMessageBox(Window owner) : this()
        {
            if (owner.IsLoaded)
            {
                this.Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
        }
        public ModernMessageBox()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _result = _defaultResult;
        }
        public MessageBoxResult Show(string message) {
            return Show(message, "", MessageBoxButton.OK, MessageBoxImage.Information, _defaultResult);
        }
        public MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            return ShowDialog(message, title, buttons, image, _defaultResult);
        }
        public MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            return ShowDialog(message, title, buttons, image, defaultResult);
        }

        public MessageBoxResult ShowDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            _result = defaultResult;
            Title = title;
            lblMessage.Text = message;
            if (buttons != MessageBoxButton.OK && buttons != MessageBoxButton.OKCancel)
            {
                btnOK.Visibility = Visibility.Collapsed;
            }
            if (buttons != MessageBoxButton.YesNo && buttons != MessageBoxButton.YesNoCancel)
            {
                btnYes.Visibility = Visibility.Collapsed;
                btnNo.Visibility = Visibility.Collapsed;
            }
            if (buttons != MessageBoxButton.OKCancel && buttons != MessageBoxButton.YesNoCancel)
            {
                btnCancel.Visibility = Visibility.Collapsed;
            }
            
            if (image == MessageBoxImage.Asterisk)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Asterisk);
            }
            else if (image == MessageBoxImage.Error)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Error);
            }
            else if (image == MessageBoxImage.Exclamation)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Exclamation);
            }
            else if (image == MessageBoxImage.Hand)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Hand);
            }
            else if (image == MessageBoxImage.Information)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Information);
            }
            else if (image == MessageBoxImage.Question)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Question);
            }
            else if (image == MessageBoxImage.Stop)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Hand);
            }
            else if (image == MessageBoxImage.Warning)
            {
                imgIcon.Source = ToImageSource(SystemIcons.Warning);
            }
            else if (image == MessageBoxImage.None)
            {
                imgIcon.Visibility = Visibility.Collapsed;
            }
            base.ShowDialog();
            return _result;
        }

        private ImageSource ToImageSource(Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            this._result = MessageBoxResult.OK;
            Close();
        }

        private void btnYes_Click(object sender, RoutedEventArgs e)
        {
            this._result = MessageBoxResult.Yes;
            Close();
        }

        private void btnNo_Click(object sender, RoutedEventArgs e)
        {
            this._result = MessageBoxResult.No;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this._result = MessageBoxResult.Cancel;
            Close();
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
}
