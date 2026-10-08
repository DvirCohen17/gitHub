using System;
using System.Threading.Tasks;
using System.Windows;

namespace client_side
{
    // Shown when there is no server configured or the server can't be reached.
    // Lets the user fix the address and retry instead of the app just exiting.
    public partial class ServerConnectWindow : Window
    {
        public Communicator? ConnectedCommunicator { get; private set; }

        public ServerConnectWindow(ServerConfig? current, string reason)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));

            ServerConfig config = current ?? new ServerConfig();
            txtHost.Text = config.Host;
            txtPort.Text = config.Port.ToString();
            lblReason.Text = reason;
            Loaded += (_, _) => txtHost.Focus();
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!ServerConfig.TryParse($"{txtHost.Text},{txtPort.Text}", out ServerConfig config))
            {
                lblErrMsg.Text = "Enter a server address and a port between 1 and 65535.";
                return;
            }

            btnConnect.IsEnabled = false;
            lblErrMsg.Text = "";
            lblReason.Text = $"Connecting to {config}...";
            try
            {
                ConnectedCommunicator = await Task.Run(() => new Communicator(config.Host, config.Port));
                ConnectedCommunicator.Initialize();
                try
                {
                    config.Save();
                }
                catch (Exception)
                {
                    // Connecting worked; failing to remember the address isn't fatal.
                }
                DialogResult = true;
            }
            catch (Exception ex)
            {
                lblReason.Text = "";
                lblErrMsg.Text = $"Couldn't connect to {config}: {ex.Message}";
                btnConnect.IsEnabled = true;
            }
        }

        private void btnQuit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
