using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace client_side
{
    public partial class AddProjectWindow : Window
    {
        private Communicator communicator;

        public ObservableCollection<User> SearchResults { get; set; }
        public ObservableCollection<User> SelectedUsers { get; set; }

        private bool disconnect = true;
        private Thread receiveServerUpdatesThread;
        private bool isListeningToServer = true;

        public class User
        {
            public string Name { get; set; }
            public bool IsAdmin { get; set; }
            public bool IsParticipant { get; set; }
        }

        public AddProjectWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            this.communicator = communicator;

            SearchResults = new ObservableCollection<User>();
            SelectedUsers = new ObservableCollection<User>();

            SearchResultsListBox.ItemsSource = SearchResults;
            SelectedUsersListBox.ItemsSource = SelectedUsers;

            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();

            Closing += createProject_CloseFile;
        }

        private async void CreateProject_Click(object sender, RoutedEventArgs e)
        {
            // Validate fields
            if (string.IsNullOrEmpty(ProjectNameTextBox.Text))
            {
                HandleError("Please enter a project name.");
                return;
            }

            if (CodeLanguageComboBox.SelectedItem == null)
            {
                HandleError("Please select a code language.");
                return;
            }

            string selectedUsersData = "";
            foreach (var user in SelectedUsers)
            {
                if(!user.IsAdmin && !user.IsParticipant)
                {
                    HandleError("Please select a role for every user.");
                    return;
                }

                if (user.IsAdmin && user.IsParticipant)
                {
                    HandleError("Please select one role for every user.");
                    return;
                }

                selectedUsersData += $"{user.Name.Length:D5}{user.Name}{(user.IsAdmin ? "1" : "")}{(user.IsParticipant ? "0" : "")}";
            }

            // Gather project data
            string projectName = ProjectNameTextBox.Text;
            string codeLanguage = ((ComboBoxItem)CodeLanguageComboBox.SelectedItem).Content.ToString();
            bool isPrivate = PrivateCheckBox.IsChecked ?? true;

            // Prepare selected users data
            
            // Example: Send data to server and handle response
            string createProjectCode = ((int)MessageCodes.MC_CREATE_PROJECT_REQUEST).ToString();

            communicator.SendData($"{createProjectCode}{projectName.Length:D5}{projectName}{selectedUsersData.Length:D5}" +
                $"{selectedUsersData}{codeLanguage.Length:D5}{codeLanguage}{isPrivate}");
        }

        private async void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    string update = communicator.ReceiveData();
                    string code = update.Substring(0, 3);

                    switch (code)
                    {
                        case "200": // MC_ERR_RESP
                            HandleError(update);
                            break;
                        case "239":
                            HandleSearchFriends(update);
                            break;
                        case "240":
                            HandleBackToMainPage();
                            break;

                        default:
                            throw new InvalidOperationException($"{code}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error receiving server updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                string msg = await Task.Run(() => communicator.ReceiveData());
            }
        }

        private void HandleBackToMainPage()
        {
            disconnect = false;
            isListeningToServer = false;

            Dispatcher.Invoke(() =>
            {
                HomePage homePageWindow = new HomePage(communicator);
                homePageWindow.Show();
                Close();
            });
        }

        private void HandleSearchFriends(string update)
        {
            Dispatcher.Invoke(() =>
            {
                SearchResults.Clear();

                int startIndex = 3;
                if (update.Length ==3)
                {
                    borderSerachBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    borderSerachBlock.Visibility= Visibility.Visible;
                }

                while (startIndex < update.Length)
                {
                    int nameLength = int.Parse(update.Substring(startIndex, 5));
                    string userName = update.Substring(startIndex + 5, nameLength);
                    startIndex += nameLength + 5;

                    // Add user to search results
                    SearchResults.Add(new User
                    {
                        Name = userName,
                        IsAdmin = false, // Initialize admin and participant status
                        IsParticipant = false,
                    });
                }
            });
        }

        private void UserSearchTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            string searchCommand = UserSearchTextBox.Text;
            string searchRequestCode = ((int)MessageCodes.MC_SEARCH_FRIENDS_REQUEST).ToString();
            communicator.SendData($"{searchRequestCode}{searchCommand.Length:D5}{searchCommand}");
        }

        private void SearchResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Add selected user from search results to selected users list
            if (SearchResultsListBox.SelectedItem is User selectedUser && !SelectedUsers.Any(u => u.Name == selectedUser.Name))
            {
                SelectedUsers.Add(new User
                {
                    Name = selectedUser.Name,
                    IsAdmin = false,
                    IsParticipant = false,
                });
            }
        }

        private void RemoveUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is User userToRemove)
            {
                SelectedUsers.Remove(userToRemove);
            }
        }

        private async void createProject_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string disconnectCode = ((int)MessageCodes.MC_DISCONNECT).ToString();
                    string fullMessage = $"{disconnectCode}{communicator.UserId:D5}";
                    communicator.SendData(fullMessage);
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void HandleError(string update)
        {
            string errorMessage = update;

            Dispatcher.Invoke(() =>
            {
                ErrorTextBlock.Text = errorMessage;
                ErrorTextBlock.Visibility = Visibility.Visible;
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            string disconnectCode = ((int)MessageCodes.MC_BACK_TO_HOME_PAGE_REQUEST).ToString();
            string fullMessage = $"{disconnectCode}{communicator.UserId:D5}";
            communicator.SendData(fullMessage);
        }
    }
}