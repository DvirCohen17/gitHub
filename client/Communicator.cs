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
        MC_GET_MESSAGES_REQUEST = 109,
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

        MC_ERROR_RESP = 200, //responses
        MC_INITIAL_RESP = 201,
        MC_INSERT_RESP = 202,
        MC_DELETE_RESP = 203,
        MC_REPLACE_RESP = 204,
        MC_CREATE_FILE_RESP = 205,
        MC_GET_FILES_RESP = 206,
        MC_ADD_FILE_RESP = 207,
        MC_CLOSE_FILE_RESP = 208,
        MC_GET_MESSAGES_RESP = 209,
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

        public Theme(
            Brush background,
            Brush foreground,
            Brush buttonBackground,
            Brush buttonForeground,
            Brush approveButtonBackground,
            Brush approveButtonForeground,
            Brush rejectButtonBackground,
            Brush rejectButtonForeground,
            Brush textColor)
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
        }
    }

    public class Communicator
    {
        public string DirectoryPath = @"C:\githubDemo\codeStyles";
        private Theme AppTheme;

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

            ModifyTheme("cyberpunk");
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
                        Brushes.Black); // Set text color for light theme
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
                        Brushes.White); // Set text color for dark theme
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
                        Brushes.DarkBlue); // Set text color for blue theme
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
                        Brushes.DarkGreen); // Set text color for green theme
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
                        Brushes.DarkRed); // Set text color for red theme
                    break;
                case "cyberpunk":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(10, 0, 50)), // Dark blue background
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)), // Cyan text
                        new SolidColorBrush(Color.FromRgb(255, 20, 147)), // Deep pink button
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)), // Green approve button
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(255, 0, 0)), // Red reject button
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 255))); // Cyan text color
                    break;
                case "matrix":
                    AppTheme = new Theme(
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)), // Green text
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)), // Green text
                        Brushes.DarkGreen, // Green approve button
                        Brushes.White,
                        Brushes.DarkRed, // Red reject button
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0))); // Green text color
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
                        Brushes.Black); // Default text color
                    break;
            }
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
                if (button.Tag != null && button.Tag.ToString() == "Approve")
                {
                    button.Background = AppTheme.ApproveButtonBackground;
                    button.Foreground = AppTheme.ApproveButtonForeground;
                }
                else if (button.Tag != null && button.Tag.ToString() == "Reject")
                {
                    button.Background = AppTheme.RejectButtonBackground;
                    button.Foreground = AppTheme.RejectButtonForeground;
                }
                else
                {
                    button.Background = AppTheme.ButtonBackground;
                    button.Foreground = AppTheme.ButtonForeground;
                }
            }
            else if (control is TextBlock textBlock)
            {
                textBlock.Foreground = AppTheme.TextColor; // Apply text color
            }
            else if (control is TextBox textBox)
            {
                textBox.Foreground = AppTheme.TextColor; // Apply text color
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

        public byte[] ReceiveImage(int imageSize)
        {
            byte[] buffer = new byte[imageSize];  // Adjust the buffer size as needed
            int totalBytesReceived = 0;

            while (totalBytesReceived < imageSize)
            {
                int bytesReceived = m_socket.Receive(buffer, totalBytesReceived, imageSize - totalBytesReceived, SocketFlags.None);
                if (bytesReceived == 0)
                {
                    throw new IOException("Connection closed unexpectedly while receiving image.");
                }
                totalBytesReceived += bytesReceived;
            }

            return buffer;
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
