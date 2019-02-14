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

namespace PSVRHistoryExporter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
       }

        internal HandConverter hc = null;
        bool isRunning = false;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                hc.Stop();
                startStopBtn.Content = "Start";
            }
            else
            {

                hc = new HandConverter(matchLogPathTxt.Text, exportDirTxt.Text);
                if (hc.Start())
                {
                    startStopBtn.Content = "Stop";
                    Properties.Settings.Default.MatchLogFilePath = matchLogPathTxt.Text;
                    Properties.Settings.Default.ExportDirPath = exportDirTxt.Text;
                    Properties.Settings.Default.Save();
                }
                else MessageBox.Show("Match Log file does not exist or export directory does not exist.", "Can't start", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseMatchLogBtn_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void BrowseExportfolderBtn_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set a default export directory
            if (Properties.Settings.Default.ExportDirPath == "")
            {
                string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                Properties.Settings.Default.ExportDirPath = System.IO.Path.Combine(appDataLocal, "Nadromar", "PSVRHistoryExporter_Exports");
                Properties.Settings.Default.Save();
                if (!System.IO.Directory.Exists(Properties.Settings.Default.ExportDirPath))
                    System.IO.Directory.CreateDirectory(Properties.Settings.Default.ExportDirPath);
            }

            // Load settings
            matchLogPathTxt.Text = Properties.Settings.Default.MatchLogFilePath;
            exportDirTxt.Text = Properties.Settings.Default.ExportDirPath;
        }
    }
}
