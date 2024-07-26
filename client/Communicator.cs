using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Security.Cryptography;

namespace client_side
{
    enum MessageCodes
    {
        MC_INITIAL_REQUEST = 101, //requests
        MC_INSERT_REQUEST = 102,
        MC_DELETE_REQUEST = 103,
        MC_REPLACE_REQUEST = 104,
        MC_CREATE_FILE_REQUEST = 105,
        MC_GET_FILES_REQUEST = 106,
        MC_CLOSE_FILE_REQUEST = 108,
        MC_GET_CHAT_MESSAGES_REQUEST = 109,
        MC_GET_USERS_ON_FILE_REQUEST = 110,
        MC_POST_MSG_REQUEST = 111,
        MC_ENTER_FILE_REQUEST = 112,
        MC_EXIT_FILE_REQUEST = 113,
        MC_DELETE_FILE_REQUEST = 114,
        MC_GET_USERS_REQUEST = 115,
        MC_GET_USERS_PERMISSIONS_REQ_REQUEST = 116,
        MC_APPROVE_PERMISSION_REQUEST = 117,
        MC_REJECT_PERMISSION_REQUEST = 118,
        MC_PERMISSION_FILE_REQ_REQUEST = 119,
        MC_PROFILE_INFO_REQUEST = 120,
        MC_FRIENDS_LIST_REQUEST = 121,
        MC_PROJECTS_LIST_REQUEST = 122,
        MC_ADD_FRIEND_REQUEST = 123,
        MC_REMOVE_FRIEND_REQUEST = 124,
        MC_SEARCH_REQUEST = 125,
        MC_ENTER_PROJECT_REQUEST = 126,
        MC_EDIT_PROFILE_INFO_REQUEST = 127,
        MC_CREATE_PROJECT_REQUEST = 128,
        MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST = 129,
        MC_GET_PROJECT_FILES_REQUEST = 130,
        MC_EXIT_PROJECT_REQUEST = 131,
        MC_DELETE_PROJECT_REQUEST = 132,
        MC_RENAME_FILE_REQUEST = 133,
        MC_GET_PROFILE_IMAGE_REQUEST = 134,
        MC_UPLOAD_PROFILE_IMAGE_REQUEST = 135,
        MC_APPROVE_FRIEND_REQ_REQUEST = 136,
        MC_REJECT_FRIEND_REQ_REQUEST = 137,
        MC_FRIEND_REQ_REQUEST = 138,
        MC_SEARCH_FRIENDS_REQUEST = 139,
        MC_BACK_TO_HOME_PAGE_REQUEST = 140,
        MC_USER_PROJECTS_LIST_REQUEST = 141,
        MC_LEAVE_PROJECT_REQUEST = 142,
        MC_ACCEPT_PROJECT_INVITE_REQUEST = 143,
        MC_DECLINE_PROJECT_INVITE_REQUEST = 144,
        MC_VIEW_PROJECT_INFO_REQUEST = 145,
        MC_EDIT_PROJECT_INFO_REQUEST = 146,
        MC_UPDATE_PROJECT_LIST_REQUEST = 147,
        MC_GET_PROJECT_INFO_REQUEST = 148,
        MC_MODIFY_PROJECT_INFO_REQUEST = 149,
        MC_GET_CODE_STYLES_REQUEST = 150,
        MC_SETTINGS_REQUEST = 151,
        MC_GET_MAIL_IMAGES_REQUEST = 152,
        MC_GET_MESSAGES_REQUEST = 153,
        MC_MOVE_TO_MESSAGES_REQUEST = 154,
        MC_GET_MSG_COUNT_REQUEST = 155,
        MC_SEND_MSG_REQUEST = 156,
        MC_MARK_AS_READ_REQUEST = 157,

        MC_ERROR_RESP = 200, //responses
        MC_INITIAL_RESP = 201,
        MC_INSERT_RESP = 202,
        MC_DELETE_RESP = 203,
        MC_REPLACE_RESP = 204,
        MC_CREATE_FILE_RESP = 205,
        MC_GET_FILES_RESP = 206,
        MC_ADD_FILE_RESP = 207,
        MC_CLOSE_FILE_RESP = 208,
        MC_GET_CHAT_MESSAGES_RESP = 209,
        MC_GET_USERS_ON_FILE_RESP = 210,
        MC_POST_MSG_RESP = 211,
        MC_ENTER_FILE_RESP = 212,
        MC_EXIT_FILE_RESP = 213,
        MC_DELETE_FILE_RESP = 214,
        MC_GET_USERS_RESP = 215,
        MC_GET_USERS_PERMISSIONS_REQ_RESP = 216,
        MC_APPROVE_PERMISSION_RESP = 217,
        MC_REJECT_PERMISSION_RESP = 218,
        MC_PERMISSION_FILE_REQ_RESP = 219,
        MC_PROFILE_INFO_RESP = 220,
        MC_FRIENDS_LIST_RESP = 221,
        MC_PROJECTS_LIST_RESP = 222,
        MC_ADD_FRIEND_RESP = 223,
        MC_REMOVE_FRIEND_RESP = 224,
        MC_SEARCH_RESP = 225,
        MC_ENTER_PROJECT_RESP = 226,
        MC_EDIT_PROFILE_INFO_RESP = 227,
        MC_CREATE_PROJECT_RESP = 228,
        MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP = 229,
        MC_GET_PROJECT_FILES_RESP = 230,
        MC_EXIT_PROJECT_RESP = 231,
        MC_DELETE_PROJECT_RESP = 232,
        MC_RENAME_FILE_RESP = 233,
        MC_GET_PROFILE_IMAGE_RESP = 234,
        MC_UPLOAD_PROFILE_IMAGE_RESP = 235,
        MC_APPROVE_FRIEND_REQ_RESP = 236,
        MC_REJECT_FRIEND_REQ_RESP = 237,
        MC_FRIEND_REQ_RESP = 238,
        MC_SEARCH_FRIENDS_RESP = 239,
        MC_BACK_TO_HOME_PAGE_RESP = 240,
        MC_USER_PROJECTS_LIST_RESP = 241,
        MC_LEAVE_PROJECT_RESP = 242,
        MC_ACCEPT_PROJECT_INVITE_RESP = 243,
        MC_DECLINE_PROJECT_INVITE_RESP = 244,
        MC_VIEW_PROJECT_INFO_RESP = 245,
        MC_EDIT_PROJECT_INFO_RESP = 246,
        MC_UPDATE_PROJECT_LIST_RESP = 247,
        MC_GET_PROJECT_INFO_RESP = 248,
        MC_MODIFY_PROJECT_INFO_RESP = 249,
        MC_GET_CODE_STYLES_RESP = 250,
        MC_SETTINGS_RESP = 251,
        MC_GET_MAIL_IMAGES_RESP = 252,
        MC_GET_MESSAGES_RESP = 253,
        MC_MOVE_TO_MESSAGES_RESP = 254,
        MC_GET_MSG_COUNT_RESP = 255,
        MC_SEND_MSG_RESP = 256,
        MC_MARK_AS_READ_RESP = 257,

        MC_DISCONNECT = 300, //user
        MC_LOGIN_REQUEST = 301,
        MC_LOGOUT_REQUEST = 306,
        MC_SIGNUP_REQUEST = 303,
        MC_FORGOT_PASSW_REQUEST = 304,

        MC_APPROVE_REQ_RESP = 302,
        MC_APPROVE_JOIN_RESP = 305,

        MC_LOGIN_RESP = 401,
        MC_SIGNUP_RESP = 403,
        MC_FORGOT_PASSW_RESP = 404,
        MC_LOGOUT_RESP = 406

    };

    public class Theme
    {
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
        public Brush ButtonBackground { get; set; }
        public Brush ButtonForeground { get; set; }
        public Brush ApproveButtonBackground { get; set; }
        public Brush ApproveButtonForeground { get; set; }
        public Brush RejectButtonBackground { get; set; }
        public Brush RejectButtonForeground { get; set; }
        public Brush TextColor { get; set; } 
        public string theame {  get; set; }

        public Theme(
            Brush background,
            Brush foreground,
            Brush buttonBackground,
            Brush buttonForeground,
            Brush approveButtonBackground,
            Brush approveButtonForeground,
            Brush rejectButtonBackground,
            Brush rejectButtonForeground,
            Brush textColor,
            string theame)
        {
            Background = background;
            Foreground = foreground;
            ButtonBackground = buttonBackground;
            ButtonForeground = buttonForeground;
            ApproveButtonBackground = approveButtonBackground;
            ApproveButtonForeground = approveButtonForeground;
            RejectButtonBackground = rejectButtonBackground;
            RejectButtonForeground = rejectButtonForeground;
            TextColor = textColor;
            this.theame = theame;
        }
    }

    public class Communicator
    {
        public string CodeStylesDir = @"C:\githubDemo\codeStyles";
        public string MailImagesDir = @"C:\githubDemo\MailImages";
        public Theme AppTheme;

        private Socket m_socket;
        public int UserId { get; set; }
        public string UserName { get; set; }

        public Communicator(string ip, int port)
        {
            m_socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            if (m_socket == null)
            {
                throw new ExternalException("Socket failed to initialize");
            }

            m_socket.Connect(ip, port);

            LoadSettings();
        }

        private void LoadSettings()
        {
            string theme = Properties.Settings.Default.Theme;

            if (string.IsNullOrEmpty(theme))
            {
                // First run, set default theme to Dark
                theme = "Dark";
                Properties.Settings.Default.Theme = theme;
                Properties.Settings.Default.Save();
            }

            // Apply the theme
            ModifyTheme(theme);
        }

        ~Communicator()
        {
            m_socket.Close();
        }

        public void ModifyTheme(string theme)
        {
            switch (theme.ToLower())
            {
                case "light":
                    AppTheme = new Theme(
                        Brushes.White,
                        Brushes.Black,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.Black, "Light");
                    break;
                case "dark":
                    AppTheme = new Theme(
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Yellow,
                        Brushes.White,
                        Brushes.DarkRed,
                        Brushes.White,
                        Brushes.White, "Dark");
                    break;
                case "blue":
                    AppTheme = new Theme(
                        Brushes.LightBlue,
                        Brushes.DarkBlue,
                        Brushes.LightBlue,
                        Brushes.DarkBlue,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.DarkBlue, "Blue");
                    break;
                case "green":
                    AppTheme = new Theme(
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.DarkGreen, "Green");
                    break;
                case "red":
                    AppTheme = new Theme(
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.DarkRed, "Red");
                    break;
                case "cyberpunk":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(10, 0, 50)),
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)),
                        new SolidColorBrush(Color.FromRgb(255, 20, 147)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(255, 0, 0)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)), "CyberPunk");
                    break;
                case "matrix":
                    AppTheme = new Theme(
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        Brushes.DarkGreen,
                        Brushes.White,
                        Brushes.DarkRed,
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)), "Matrix");
                    break;
                case "solarized light":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(253, 246, 227)),
                        new SolidColorBrush(Color.FromRgb(101, 123, 131)),
                        new SolidColorBrush(Color.FromRgb(238, 232, 213)),
                        new SolidColorBrush(Color.FromRgb(88, 110, 117)),
                        new SolidColorBrush(Color.FromRgb(42, 161, 152)),
                        new SolidColorBrush(Color.FromRgb(133, 153, 0)),
                        new SolidColorBrush(Color.FromRgb(203, 75, 75)),
                        new SolidColorBrush(Color.FromRgb(139, 0, 0)),
                        new SolidColorBrush(Color.FromRgb(101, 123, 131)), "Solarized Light");
                    break;
                case "solarized dark":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(0, 43, 54)),
                        new SolidColorBrush(Color.FromRgb(131, 148, 153)),
                        new SolidColorBrush(Color.FromRgb(7, 54, 33)),
                        new SolidColorBrush(Color.FromRgb(147, 161, 161)),
                        new SolidColorBrush(Color.FromRgb(38, 139, 210)),
                        new SolidColorBrush(Color.FromRgb(42, 161, 152)),
                        new SolidColorBrush(Color.FromRgb(203, 75, 75)),
                        new SolidColorBrush(Color.FromRgb(253, 246, 227)),
                        new SolidColorBrush(Color.FromRgb(131, 148, 153)), "Solarized Dark");
                    break;
                case "vintage":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(244, 227, 227)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(208, 182, 182)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(155, 109, 109)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(244, 227, 227)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)), "Vintage");
                    break;
                case "neon":
                    AppTheme = new Theme(
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(57, 255, 20)),
                        new SolidColorBrush(Color.FromRgb(255, 0, 127)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(255, 0, 0)),
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(57, 255, 20)), "Neon");
                    break;
                case "pastel":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(251, 232, 235)),
                        new SolidColorBrush(Color.FromRgb(195, 162, 176)),
                        new SolidColorBrush(Color.FromRgb(245, 227, 230)),
                        new SolidColorBrush(Color.FromRgb(176, 58, 106)),
                        new SolidColorBrush(Color.FromRgb(247, 198, 199)),
                        new SolidColorBrush(Color.FromRgb(227, 182, 178)),
                        new SolidColorBrush(Color.FromRgb(251, 232, 235)),
                        new SolidColorBrush(Color.FromRgb(176, 58, 106)),
                        new SolidColorBrush(Color.FromRgb(195, 162, 176)), "Pastel");
                    break;
                default:
                    AppTheme = new Theme(
                        Brushes.White,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Black,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.Black, "Light");
                    break;
            }
            SaveSettings(theme);

        }


        public void ApplyTheme(Window window)
        {
            if (AppTheme != null)
            {
                window.Background = AppTheme.Background;
                window.Foreground = AppTheme.Foreground;

                // Apply theme to all child controls
                ApplyThemeToControls(window.Content as Panel);
            }
        }

        private void ApplyThemeToControls(Panel panel)
        {
            if (panel == null)
                return;

            foreach (var control in panel.Children)
            {
                ApplyThemeToControl(control as DependencyObject); // Cast to DependencyObject
            }
        }

        private void ApplyThemeToControl(DependencyObject control)
        {
            if (control == null)
                return;

            if (control is Button button)
            {
                // Apply the background and foreground colors
                button.Background = AppTheme.ButtonBackground;
                button.Foreground = AppTheme.ButtonForeground;

                // Apply the border color for buttons
                button.BorderBrush = AppTheme.ButtonForeground;
            }
            else if (control is TextBlock textBlock)
            {
                textBlock.Foreground = AppTheme.TextColor; // Apply text color

            }
            else if (control is TextBox textBox)
            {
                textBox.Foreground = AppTheme.TextColor; // Apply text color
                textBox.BorderBrush = AppTheme.TextColor; 
            }
            else if (control is Border border)
            {
                border.BorderBrush = AppTheme.TextColor; // Apply text color
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.Foreground = AppTheme.TextColor; // Apply text color
                if (checkBox.Content is string contentString)
                {
                    checkBox.Content = new TextBlock
                    {
                        Text = contentString,
                        Foreground = AppTheme.TextColor
                    };
                }
                else if (checkBox.Content is TextBlock contentTextBlock)
                {
                    contentTextBlock.Foreground = AppTheme.TextColor;
                }
            }
            else if (control is ListBox listBox)
            {
                listBox.Foreground = AppTheme.TextColor; // Apply text color
                foreach (var item in listBox.Items)
                {
                    // Find the ListBoxItem and apply theme to it
                    ListBoxItem listBoxItem = listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (listBoxItem != null)
                    {
                        ApplyThemeToControl(listBoxItem);
                    }
                }
            }
            else if (control is Panel childPanel)
            {
                foreach (var child in childPanel.Children)
                {
                    ApplyThemeToControl(child as DependencyObject); // Cast to DependencyObject
                }
            }
        }


        private void ApplyThemeToControl(ListBoxItem listBoxItem)
        {
            if (listBoxItem == null)
                return;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(listBoxItem); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(listBoxItem, i);
                ApplyThemeToControl(child);
            }
        }

        private void SaveSettings(string theme)
        {
            // Save the selected theme to the application settings
            Properties.Settings.Default.Theme = theme;
            Properties.Settings.Default.Save();
        }

        public void SaveCredentials(string username, string hashedPassword)
        {
            Properties.Settings.Default.Username = username;
            Properties.Settings.Default.HashedPassword = hashedPassword;
            Properties.Settings.Default.Save();
        }

        public void ClearCredentials()
        {
            Properties.Settings.Default.Username = string.Empty;
            Properties.Settings.Default.HashedPassword = string.Empty;
            Properties.Settings.Default.Save();
        }

        public string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hashedBytes.Length; i++)
                {
                    builder.Append(hashedBytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public void SendData(string message)
        {
            //LogAction(message);
            byte[] data = Encoding.UTF8.GetBytes(message);
            m_socket.Send(data);
        }

        public void SendImage(byte[] message)
        {
            //LogAction(message);
            m_socket.Send(message);
        }

        public string ReceiveData()
        {
            byte[] buffer = new byte[512];  // Adjust the buffer size as needed
            int bytesRead = m_socket.Receive(buffer);
            string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            //LogAction(rep);
            return rep;
        }

        public string ReceiveFileData()
        {
            byte[] buffer = new byte[30000];  // Adjust the buffer size as needed
            int bytesRead = m_socket.Receive(buffer);
            string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            //LogAction(rep);
            return rep;
        }

        public string ReceiveImage(int imageSize)
        {
            byte[] buffer = new byte[imageSize];
            int received = 0;

            while (received < imageSize)
            {
                int bytes = m_socket.Receive(buffer, received, imageSize - received, SocketFlags.None);
                if (bytes == 0) throw new Exception("Connection closed prematurely.");
                received += bytes;
            }

            return Convert.ToBase64String(buffer);
        }

        /*
        public string ReceiveData(CancellationToken cancellationToken)
        {
            // Adjust the timeout value as needed
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            // Use CancellationTokenSource to apply timeout
            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    byte[] buffer = new byte[1024];  // Adjust the buffer size as needed
                    int bytesRead = m_socket.Receive(buffer);
                    string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    LogAction(rep);
                    return rep;
                }
                catch (OperationCanceledException)
                {
                    // Handle the case where the operation is canceled due to timeout
                    throw new TimeoutException("Receive operation timed out.");
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    throw new Exception($"Error receiving data: {ex.Message}");
                }
            }
        }

        public string ReceiveDataWithTimeout(TimeSpan timeout)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    var token = cts.Token;

                    Task<string> receiveTask = Task.Run(() =>
                    {
                        // Replace the following line with your actual ReceiveData logic
                        // Here, I'm assuming ReceiveData returns a string
                        return ReceiveData();
                    }, token);

                    // Wait for the task to complete within the specified timeout
                    if (receiveTask.Wait(timeout))
                    {
                        // Task completed within the timeout
                        return receiveTask.Result;
                    }
                    else
                    {
                        // Timeout occurred
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions as needed
                Console.WriteLine($"Error in ReceiveDataWithTimeout: {ex.Message}");
                return null;
            }
        }

        */
        public void LogAction(string action)
        {
            try
            {
                string logFilePath = "UserLog_" + UserName + ".txt";

                // Append the action to the log file
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {action}\n");
                //File.AppendAllText(logFilePath, $"{action}\n"); // - just the msg without date
            }
            catch (Exception ex)
            {
                // Handle the exception or log it to another source
                MessageBox.Show($"Error logging action: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public bool ContainsSqlInjection(string input)
        {
            // Define a list of special characters commonly used in SQL injection
            string[] sqlSpecialCharacters = { "'", ";", "--", "/*", "*/", "xp_", "exec", "sp_"};

            // Check if the input contains any of the special characters
            foreach (var specialCharacter in sqlSpecialCharacters)
            {
                if (input.Contains(specialCharacter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
