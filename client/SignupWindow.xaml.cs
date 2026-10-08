using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace client_side
{
    /// <summary>
    /// Interaction logic for SignupWindow.xaml
    /// </summary>
    public partial class SignupWindow : Window
    {
        Communicator communicator;
        bool disconnect = true; // if window closed by the user disconnect

        public SignupWindow(Communicator _communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            communicator = _communicator;
            Closing += signUp_CloseFile; // Hook up the closing event handler
            communicator.ApplyTheme(this);

        }

        private async void signUp_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();

                    string fullMessage = $"{chatMessageCode}";

                    communicator.SendData(fullMessage);

                    // Close the window on the UI thread
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private void hprlnkLogin(object sender, RoutedEventArgs e)
        {
            disconnect = false;
            LoginWindow loginWindow = new LoginWindow(communicator);
            loginWindow.Show();
            Close();
        }

        private void btnSignup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = txtUsername.Text.Trim();
                string email = txtEmail.Text.Trim();

                string? problem = ValidateSignup(name, email, pwdPassword.Password, pwdConfirm.Password);
                if (problem != null)
                {
                    lblErrMsg.Text = problem;
                    return;
                }

                string code = ((int)MessageCodes.MC_SIGNUP_REQUEST).ToString();
                string pass = communicator.HashPassword(pwdPassword.Password);

                communicator.SendData($"{code}{name.Length:D5}{name}{pass.Length:D5}{pass}{email.Length:D5}{email}");

                string update = communicator.ReceiveData();
                string rep = update.Substring(0, 3);

                if (rep == ((int)MessageCodes.MC_SIGNUP_RESP).ToString())
                {
                    communicator.UserName = name;
                    communicator.UserId = int.Parse(update.Substring(3));
                    disconnect = false;
                    communicator.UseResponseTimeout(false); // from here on, windows listen in the background

                    HomePage filesWindow = new HomePage(communicator);
                    filesWindow.Show();
                    Close();
                }
                else if (rep == ((int)MessageCodes.MC_ERROR_RESP).ToString())
                {
                    lblErrMsg.Text = update.Substring(3);
                }
            }
            catch (Exception ex)
            {
                lblErrMsg.Text = ex.Message;
            }
        }

        // Usernames are limited to ASCII: they appear in length-prefixed protocol
        // fields that are counted in characters on this side and bytes on the server.
        private static readonly Regex UsernamePattern = new Regex(@"^[A-Za-z0-9_.-]{3,30}$");
        private static readonly Regex EmailPattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        public const int MinPasswordLength = 6;

        private static string? ValidateSignup(string name, string email, string password, string confirm)
        {
            if (!UsernamePattern.IsMatch(name))
                return "Username must be 3-30 characters: letters, digits, '.', '_' or '-'.";
            if (!EmailPattern.IsMatch(email) || email.Length > 100)
                return "Please enter a valid email address.";
            if (password.Length < MinPasswordLength)
                return $"Password must be at least {MinPasswordLength} characters.";
            if (password != confirm)
                return "The passwords don't match.";
            return null;
        }

    }
}
