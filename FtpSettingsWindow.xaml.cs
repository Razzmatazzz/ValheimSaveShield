using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using WinSCP;

namespace ValheimSaveShield
{
    /// <summary>
    /// Interaction logic for FtpSettingsWindow.xaml
    /// </summary>
    public partial class FtpSettingsWindow : Window
    {
        public FtpSettingsWindow()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            txtIP.Text = Properties.Settings.Default.FtpIpAddress;
            txtPort.Text = Properties.Settings.Default.FtpPort;
            txtWorldsPath.Text = Properties.Settings.Default.FtpFilePath;
            txtUsername.Text = Properties.Settings.Default.FtpUsername;
            txtPassword.Text = Properties.Settings.Default.FtpPassword;
            foreach (var path in Properties.Settings.Default.SaveFolders)
            {
                lstSaveFolder.Items.Add(path);
            }
            if (Properties.Settings.Default.FtpSaveDest.Length > 0)
            {
                lstSaveFolder.SelectedItem = Properties.Settings.Default.FtpSaveDest;
            }
            else
            {
                lstSaveFolder.SelectedIndex = 0;
            }

            foreach (int i in Enum.GetValues(typeof(FtpMode)))
            {
                cmbFtpMode.Items.Add(Enum.GetName(typeof(FtpMode), i));
            }
            cmbFtpMode.SelectedIndex = Properties.Settings.Default.FtpMode;

        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!txtWorldsPath.Text.StartsWith("/"))
            {
                //txtWorldsPath.Text = txtWorldsPath.Text.Remove(0, 1);
                txtWorldsPath.Text = "/"+txtWorldsPath.Text;
            }

            if (txtWorldsPath.Text.EndsWith("/"))
            {
                txtWorldsPath.Text = txtWorldsPath.Text.Remove(txtWorldsPath.Text.Length - 1, 1);
            }

            Properties.Settings.Default.FtpIpAddress = txtIP.Text;
            Properties.Settings.Default.FtpPort = txtPort.Text;
            Properties.Settings.Default.FtpFilePath = txtWorldsPath.Text;
            Properties.Settings.Default.FtpUsername = txtUsername.Text;
            Properties.Settings.Default.FtpPassword = txtPassword.Text;
            Properties.Settings.Default.FtpMode = cmbFtpMode.SelectedIndex;
            Properties.Settings.Default.FtpSaveDest = lstSaveFolder.SelectedItem.ToString();
            Properties.Settings.Default.Save();

            DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtIP.Text.Length == 0 || txtPort.Text.Length == 0 || txtWorldsPath.Text.Length == 0 || txtUsername.Text.Length == 0 || txtPassword.Text.Length == 0)
                {
                    ModernMessageBox mmb = new ModernMessageBox(this);
                    mmb.Show("All fields are required.", "Missing FTP information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SessionOptions sessionOptions = new SessionOptions
                {
                    Protocol = Protocol.Ftp,
                    HostName = txtIP.Text,
                    PortNumber = Int32.Parse(txtPort.Text),
                    UserName = txtUsername.Text,
                    Password = txtPassword.Text,
                    FtpMode = (FtpMode)cmbFtpMode.SelectedIndex
                };

                using (Session session = new Session())
                {
                    // Connect
                    try
                    {
                        session.Open(sessionOptions);
                        if (session.FileExists(txtWorldsPath.Text))
                        {
                            var dbFound = false;
                            foreach (var file in session.ListDirectory(txtWorldsPath.Text).Files)
                            {
                                if (file.ToString().EndsWith(".db"))
                                {
                                    dbFound = true;
                                }
                            }
                            if (dbFound)
                            {
                                ModernMessageBox mmb = new ModernMessageBox(this);
                                mmb.Show("Connection successful.", "Connection Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                ModernMessageBox mmb = new ModernMessageBox(this);
                                mmb.Show($"Connected successfully, but no world saves found at {txtWorldsPath.Text}.", "No Worlds Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            ModernMessageBox mmb = new ModernMessageBox(this);
                            mmb.Show($"Connected successfully to FTP server, but path {txtWorldsPath.Text} does not exist.", "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModernMessageBox mmb = new ModernMessageBox(this);
                        mmb.Show($"Error connecting to FTP server: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error testing FTP connection: {ex.Message}");
            }
        }
    }
}
