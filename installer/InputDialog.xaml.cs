using System.Windows;

namespace InstallerApp
{
    public partial class InputDialog : Window
    {
        public string IPAddress => ipTextBox.Text;
        public int Port => int.TryParse(portTextBox.Text, out var port) ? port : 0;

        public InputDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(IPAddress) || Port <= 0)
            {
                MessageBox.Show("Please enter valid IP address and port.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
