using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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


        private string Access;
        private bool isEditable;
        private string oldProjectName;

        public class User : INotifyPropertyChanged
        {
            private string _name;
            private bool _isAdmin;
            private bool _isParticipant;
            private bool _isCreator;
            private bool _isEnabled = true;
            private string _IsEnabledRemove;

            public bool IsEnabled
            {
                get { return _isEnabled; }
                set
                {
                    if (_isEnabled != value)
                    {
                        _isEnabled = value;
                        OnPropertyChanged(nameof(IsEnabled));
                    }
                }
            }

            public string IsEnabledRemove
            {
                get { return _IsEnabledRemove; }
                set
                {
                    if (_IsEnabledRemove != value)
                    {
                        _IsEnabledRemove = value;
                        OnPropertyChanged(nameof(IsEnabledRemove));
                    }
                }
            }

            public string Name
            {
                get { return _name; }
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        OnPropertyChanged(nameof(Name));
                    }
                }
            }

            public bool IsAdmin
            {
                get { return _isAdmin; }
                set
                {
                    if (_isAdmin != value)
                    {
                        _isAdmin = value;
                        OnPropertyChanged(nameof(IsAdmin));
                        // Ensure mutual exclusivity with IsCreator
                        if (_isAdmin && _isCreator)
                        {
                            _isAdmin = false;
                            OnPropertyChanged(nameof(IsAdmin));
                        }
                        if (_isParticipant && _isAdmin)
                        {
                            IsParticipant = false;
                        }
                    }
                }
            }

            public bool IsParticipant
            {
                get { return _isParticipant; }
                set
                {
                    if (_isParticipant != value)
                    {
                        _isParticipant = value;
                        OnPropertyChanged(nameof(IsParticipant));
                        // Ensure mutual exclusivity with IsCreator
                        if (_isParticipant && _isCreator)
                        {
                            _isParticipant = false;
                            OnPropertyChanged(nameof(IsParticipant));
                        }
                        if (_isParticipant && _isAdmin)
                        {
                            IsAdmin = false;
                        }
                    }
                }
            }

            public bool IsCreator
            {
                get { return _isCreator; }
                set
                {
                    if (_isCreator != value)
                    {
                        _isCreator = value;
                        OnPropertyChanged(nameof(IsCreator));
                        // Ensure mutual exclusivity
                        if (_isCreator)
                        {
                            _isAdmin = false;
                            _isParticipant = false;
                            _IsEnabledRemove = "Collapsed";
                            OnPropertyChanged(nameof(IsAdmin));
                            OnPropertyChanged(nameof(IsParticipant));
                            OnPropertyChanged(nameof(IsEnabledRemove));
                        }
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private UserProfile currentRole;

        private Info projectInfo;

        public class Info
        {
            public string ProjectName { get; set; }
            public string CodeLan { get; set; }
            public bool IsPrivate { get; set; }
            public List<User> users { get; set; } = new List<User>();
        }

        public class UserProfile
        {
            public bool isCretor { get; set; }
            public bool isViewer { get; set; }
            public bool isEditor { get; set; }
            public bool isCreate { get; set; }
            public bool isEditable { get; set; }
        }

        public AddProjectWindow(Communicator communicator, string mode, string projectName)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            this.communicator = communicator;
            this.Access = mode;
            DataContext = this;

            SearchResults = new ObservableCollection<User>();
            SelectedUsers = new ObservableCollection<User>();

            SearchResultsListBox.ItemsSource = SearchResults;
            SelectedUsersListBox.ItemsSource = SelectedUsers;

            isEditable = mode != "viewer";

            currentRole = new UserProfile
            {
                isViewer = mode == "viewer" ? true : false,
                isCreate = mode == "create" ? true : false,
                isCretor = mode == "creator" ? true : false,
                isEditor = mode == "editor" ? true : false,
                isEditable = mode == "editor" || mode == "creator" ? true : false
            };

            this.projectInfo = null;

            if (mode != "create")
            {
                string projectInfoCode = ((int)MessageCodes.MC_GET_PROJECT_INFO_REQUEST).ToString();

                communicator.SendData($"{projectInfoCode}{projectName.Length:D5}{projectName}");

                string update = communicator.ReceiveData();
                string code = update.Substring(0, 3);

                if (code == ((int)MessageCodes.MC_GET_PROJECT_INFO_RESP).ToString() && update.Length > 3)
                {
                    projectInfo = new Info();
                    int currentIndex = 3;

                    int projectNameLength = int.Parse(update.Substring(currentIndex, 5));
                    currentIndex += 5;
                    string MsgProjectName = update.Substring(currentIndex, projectNameLength);
                    currentIndex += projectNameLength;

                    // Extract selected users data
                    int selectedUsersDataLength = int.Parse(update.Substring(currentIndex, 5));
                    currentIndex += 5;
                    string selectedUsersData = update.Substring(currentIndex, selectedUsersDataLength);
                    currentIndex += selectedUsersDataLength;

                    // Extract code language
                    int codeLanguageLength = int.Parse(update.Substring(currentIndex, 5));
                    currentIndex += 5;
                    string codeLanguage = update.Substring(currentIndex, codeLanguageLength);
                    currentIndex += codeLanguageLength;

                    // Extract private/public flag
                    string privateFlag = update.Substring(currentIndex, 1); // Assuming it's a single character
                    bool isPrivate = privateFlag == "1"; // Example logic based on your protocol

                    projectInfo.IsPrivate = isPrivate;
                    projectInfo.CodeLan = codeLanguage;
                    projectInfo.ProjectName = MsgProjectName;
                    oldProjectName = projectInfo.ProjectName;
                    int index = 0;
                    while (index < selectedUsersData.Length)
                    {
                        int nameLen = int.Parse(selectedUsersData.Substring(index, 5));
                        index += 5;
                        string name = selectedUsersData.Substring(index, nameLen);
                        index += nameLen;

                        // Extract admin and participant roles
                        bool isAdmin = selectedUsersData[index] == '1';
                        bool isParticipant = selectedUsersData[index] == '0';
                        bool isCreator = selectedUsersData[index] == '2';
                        index++;
                        User user = new User();
                        user.Name = name;
                        user.IsAdmin = isAdmin;
                        user.IsParticipant = isParticipant;
                        user.IsCreator = isCreator;
                        // Create User object and add to projectInfo.users
                        projectInfo.users.Add(user);
                    }
                }

                if (projectInfo != null)
                {
                    CodeLanguageComboBox.SelectedItem = CodeLanguageComboBox.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(item => item.Content.ToString() == projectInfo.CodeLan);
                    // Populate selected users
                    foreach (var user in projectInfo.users)
                    {
                        SelectedUsers.Add(new User
                        {
                            Name = user.Name,
                            IsAdmin = user.IsAdmin,
                            IsParticipant = user.IsParticipant,
                            IsCreator = user.IsCreator,
                        });

                    }
                    DataContext = projectInfo;
                }

                if (currentRole.isViewer)
                {
                    UserSearchText.Visibility = Visibility.Visible;
                    UserSearchTextBox.Visibility = Visibility.Visible;
                    UserSearchTextBox.IsReadOnly = true;
                    ProjectNameTextBox.Visibility = Visibility.Visible;
                    ProjectNameTextBox.IsReadOnly = true;
                    craeteBtn.Visibility = Visibility.Collapsed;
                    CodeLanguageComboBox.IsEnabled = false;
                    editBtn.Visibility = Visibility.Collapsed;
                    PrivateCheckBox.IsEnabled = false;
                    // Disable checkboxes for existing users in SelectedUsersListBox
                    foreach (User user in SelectedUsersListBox.Items)
                    {
                        user.IsEnabled = false;
                        user.IsEnabledRemove = "Collapsed";
                    }
                }
                else if (currentRole.isEditable)
                {
                    UserSearchText.Visibility = Visibility.Visible;
                    UserSearchTextBox.Visibility = Visibility.Visible;
                    UserSearchTextBox.IsReadOnly = false;
                    ProjectNameTextBox.Visibility = Visibility.Visible;
                    ProjectNameTextBox.IsReadOnly = false;
                    CodeLanguageComboBox.IsEnabled = true;
                    craeteBtn.Visibility = Visibility.Collapsed;
                    editBtn.Visibility = Visibility.Visible;
                    PrivateCheckBox.IsEnabled = true;
                    foreach (User user in SelectedUsersListBox.Items)
                    {
                        user.IsEnabled = true;
                        if(user.IsCreator)
                        {
                            user.IsEnabledRemove = "Collapsed";
                        }
                        else
                        {
                            user.IsEnabledRemove = "Visible";
                        }
                    }
                }
                else if (currentRole.isCreate)
                {
                    UserSearchText.Visibility = Visibility.Visible;
                    UserSearchTextBox.Visibility = Visibility.Visible;
                    UserSearchTextBox.IsReadOnly = false;
                    ProjectNameTextBox.Visibility = Visibility.Visible;
                    ProjectNameTextBox.IsReadOnly = false;
                    CodeLanguageComboBox.IsEnabled = true;
                    craeteBtn.Visibility = Visibility.Visible;
                    editBtn.Visibility = Visibility.Collapsed;
                    PrivateCheckBox.IsEnabled = true;
                    foreach (User user in SelectedUsersListBox.Items)
                    {
                        user.IsEnabled = true;
                        if (user.IsCreator)
                        {
                            user.IsEnabledRemove = "Collapsed";
                        }
                        else
                        {
                            user.IsEnabledRemove = "Visible";
                        }
                    }
                }

            }
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
                if (!user.IsAdmin && !user.IsParticipant)
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

        private async void EditProject_Click(object sender, RoutedEventArgs e)
        {
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
                if ((!user.IsAdmin && !user.IsParticipant) && !user.IsCreator)
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
            string createProjectCode = ((int)MessageCodes.MC_MODIFY_PROJECT_INFO_REQUEST).ToString();

            communicator.SendData($"{createProjectCode}{oldProjectName.Length:D5}{oldProjectName}{projectName.Length:D5}{projectName}{selectedUsersData.Length:D5}" +
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
                System.Windows.MessageBox.Show($"Error receiving server updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (update.Length == 3)
                {
                    borderSerachBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    borderSerachBlock.Visibility = Visibility.Visible;
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
            if (sender is System.Windows.Controls.Button button)
            {
                if (button.Tag is User userToRemove)
                {
                    SelectedUsers.Remove(userToRemove);
                }
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
                    System.Windows.MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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