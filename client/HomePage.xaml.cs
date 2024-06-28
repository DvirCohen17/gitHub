using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls; // For the Button and other WPF controls
using System.Windows.Media.Imaging; // For the Image control
using System.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Globalization;
using static client_side.ProjectDirectory;
using System.Collections.Generic;
using Microsoft.Win32;
using System.IO;
using static client_side.HomePage;


namespace client_side
{
    public partial class HomePage : Window, INotifyPropertyChanged
    {
        private Communicator communicator;
        private bool disconnect = true;
        private Thread receiveServerUpdatesThread;
        private bool isListeningToServer = true;
        private UserProfile loggedUserProfile;
        private UserProfile displayedUserProfile;

        private bool waitingForImage = false;
        private int imageSize;

        private bool inSearch = false;
        private bool first = true;

        public event PropertyChangedEventHandler PropertyChanged;
        public HomePage(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            DataContext = this;

            lstFriends.MouseDoubleClick += LstFriends_MouseDoubleClick;
            lstProjects.MouseDoubleClick += LstProjects_MouseDoubleClick;

            start();

            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();

            Closing += HomePage_CloseFile;

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set window size based on screen size
            this.Width = SystemParameters.PrimaryScreenWidth * 0.9;
            this.Height = SystemParameters.PrimaryScreenHeight * 0.9;

            // Center the window on the screen
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
        }

        private void start()
        {
            // Load profile info, friends list, and projects list
            LoadProfileInfo();
            LoadFriendsList();
            LoadProjectsList();
        }

        private void LoadProfileInfo()
        {
            string profileInfoCode = ((int)MessageCodes.MC_PROFILE_INFO_REQUEST).ToString();
            communicator.SendData($"{profileInfoCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string profileRep = communicator.ReceiveData();
            string profileRepCode = profileRep.Substring(0, 3);

            if (profileRepCode == ((int)MessageCodes.MC_PROFILE_INFO_RESP).ToString() && profileRep.Length > 3)
            {
                int userNameLen = int.Parse(profileRep.Substring(3, 5));
                string userName = profileRep.Substring(8, userNameLen);

                int emailLenPos = 8 + userNameLen;
                int emailLen = int.Parse(profileRep.Substring(emailLenPos, 5));
                string email = profileRep.Substring(emailLenPos + 5, emailLen);

                int bioLenPos = emailLenPos + 5 + emailLen;
                int bioLen = int.Parse(profileRep.Substring(bioLenPos, 5));
                string bio = profileRep.Substring(bioLenPos + 5, bioLen);
                BitmapImage receivedImage = null;

                /*
                int imageLenPos = bioLenPos + 5 + bioLen;
                int imageSize = int.Parse(profileRep.Substring(imageLenPos, 6));

                if (imageSize > 0)
                {
                    if (profileRep.Length >= imageLenPos + 6 + imageSize)
                    {
                        string imageDataString = profileRep.Substring(imageLenPos + 6, imageSize);
                        byte[] imageData = Convert.FromBase64String(imageDataString);
                        receivedImage = communicator.ByteArrayToImage(imageData);
                    }
                }
                */

                displayedUserProfile = new UserProfile
                {
                    ProfileImage = receivedImage,
                    UserName = userName,
                    Email = email,
                    Bio = bio,
                    IsCurrentUserProfile = (userName == communicator.UserName)
                };

                // Set visibility of controls based on whether it's the current user's profile and if there's an image
                if (displayedUserProfile.IsCurrentUserProfile)
                {
                    editButton.Visibility = Visibility.Visible;
                    BioTextBox.Visibility = Visibility.Visible;
                    BioTextBlock.Visibility = Visibility.Collapsed;
                    addFriendBtn.Visibility = Visibility.Collapsed;
                    addFriendText.Visibility = Visibility.Collapsed;
                    closeSerachBtn.Visibility = Visibility.Collapsed;

                    /* Show "Upload Picture" button only if no profile image exists
                    if (displayedUserProfile.ProfileImage == null)
                    {
                        displayedUserProfile.RaisePropertyChanged(nameof(displayedUserProfile.ProfileImage));
                    }
                    */
                    loggedUserProfile = displayedUserProfile;
                }
                else
                {
                    editButton.Visibility = Visibility.Collapsed;
                    BioTextBox.Visibility = Visibility.Collapsed;
                    BioTextBlock.Visibility = Visibility.Visible;
                    addFriendBtn.Visibility = Visibility.Collapsed;
                    addFriendText.Visibility = Visibility.Collapsed;
                    closeSerachBtn.Visibility = Visibility.Collapsed;
                }

                DataContext = displayedUserProfile;
                backButton.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadFriendsList()
        {
            string friendsListCode = ((int)MessageCodes.MC_FRIENDS_LIST_REQUEST).ToString();
            communicator.SendData($"{friendsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string friendsRep = communicator.ReceiveData();
            HandleLoadFriendsList(friendsRep);
        }

        private void HandleLoadFriendsList(string update)
        {
            string friendsRepCode = update.Substring(0, 3);

            Dispatcher.Invoke(() =>
            {
                if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                {
                    friends.Clear();
                }
            });

            if (friendsRepCode == ((int)MessageCodes.MC_FRIENDS_LIST_RESP).ToString() && update.Length > 3)
            {
                bool friendCheck = true;
                bool friendRequsetCheck = false;

                int index = 3;
                if (first)
                {
                    ObservableCollection<User> friends = new ObservableCollection<User>();
                    while (index < update.Length)
                    {
                        int friendNameLen = int.Parse(update.Substring(index, 5));
                        index += 5;
                        string friendName = update.Substring(index, friendNameLen);
                        index += friendNameLen;
                        string onlineStatus = update.Substring(index, 1);
                        index += 1;

                        if (onlineStatus == "0" || onlineStatus == "1")
                        {
                            friendCheck = true;
                            friendRequsetCheck = false;
                        }
                        else if (onlineStatus == "3")
                        {
                            friendCheck = false;
                            friendRequsetCheck = true;
                        }

                        friends.Add(new User
                        {
                            Name = friendName,
                            Status = ParseStatus(onlineStatus).ToString(),
                            IsFriend = friendCheck,
                            IsFriendRequest = friendRequsetCheck,
                        });

                    }

                    SortFriendsList(friends);
                    first = false;
                }
                else
                {
                    while (index < update.Length)
                    {
                        int friendNameLen = int.Parse(update.Substring(index, 5));
                        index += 5;
                        string friendName = update.Substring(index, friendNameLen);
                        index += friendNameLen;
                        string onlineStatus = update.Substring(index, 1);
                        index += 1;
                        Dispatcher.Invoke(() =>
                        {
                            if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                            {
                                
                                if (onlineStatus == "0" || onlineStatus == "1")
                                {
                                    friendCheck = true;
                                    friendRequsetCheck = false;
                                }
                                else if (onlineStatus == "3")
                                {
                                    friendCheck = false;
                                    friendRequsetCheck = true;
                                }

                                friends.Add(new User
                                {
                                    Name = friendName,
                                    Status = ParseStatus(onlineStatus).ToString(),
                                    IsFriend = friendCheck,
                                    IsFriendRequest = friendRequsetCheck,
                                });
                                // Optionally, sort the friends list after adding
                                SortFriendsList(friends);
                            }
                        });
                        // Move to the next user's data
                        index += 5 + friendNameLen;
                    }
                }
                
            }
            
        }

        private void LoadProjectsList()
        {
            string projectsListCode = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
            communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string projectsRep = communicator.ReceiveData();
            string projectsRepCode = projectsRep.Substring(0, 3);

            if (projectsRepCode == ((int)MessageCodes.MC_PROJECTS_LIST_RESP).ToString() && projectsRep.Length > 3)
            {
                int index = 3;
                ObservableCollection<string> projects = new ObservableCollection<string>();
                while (index < projectsRep.Length)
                {
                    int projectNameLen = int.Parse(projectsRep.Substring(index, 5));
                    index += 5;
                    string projectName = projectsRep.Substring(index, projectNameLen);
                    index += projectNameLen;

                    projects.Add(projectName);
                }

                Dispatcher.Invoke(() => lstProjects.ItemsSource = projects);
            }
        }

        private async void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    if(waitingForImage)
                    {
                        communicator.ReceiveImage(imageSize);
                    }

                    string update = communicator.ReceiveData();
                    string code = update.Substring(0, 3);

                    switch (code)
                    {
                        case "200": // MC_ERR_RESP
                            HandleError(update);
                            break;
                        case "305": // MC_APPROVE_JOIN_RESP
                            HandleJoinProject(update);
                            break;
                        case "220":
                            HandleReciveInfo(update);
                            break;
                        case "221":
                            HandleLoadFriendsList(update);
                            break;
                        case "222":
                            HandleReciveProjects(update);
                            break;
                        case "223":

                            break;
                        case "236":

                            break;
                        case "224":
                            HandleRemoveUser(update);
                            break;
                        case "225":
                            HandleSerachUsers(update);
                            break;
                        case "406":
                            HandleLogout(update);
                             break;
                        case "401" or "403":
                            HandleLogin(update);
                            break;
                        case "229":
                            HandleMoveToCreateWindow(update);
                            break;
                        case "232":
                            HandleDeleteProject(update);
                            break;
                        case "300":
                            HandleDisconnect(update);
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
        
        private void HandleError(string update)
        {
            string code = update.Substring(0, 3);
            string msg = update.Substring(3);

            MessageBox.Show($"Error: {msg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void HandleJoinProject(string update)
        {
            disconnect = false;
            isListeningToServer = false;

            int projectLength = int.Parse(update.Substring(3, 5));
            string ProjectName = update.Substring(8, projectLength);

            int codeLanLen = int.Parse(update.Substring(8 + projectLength, 5));
            string codeLan = update.Substring(13 + projectLength, codeLanLen);
            Dispatcher.Invoke(() =>
            {
                ProjectDirectory TextEditorWindow = new ProjectDirectory(communicator, ProjectName, codeLan);
                TextEditorWindow.Show();
                Close();
            });
        }

        private void HandleSerachUsers(string update)
        {
            // Clear existing friends list
            Dispatcher.Invoke(() =>
            {
                if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                {
                    friends.Clear();
                }
            });

            int startIndex = 3;
            while (startIndex < update.Length)
            {
                int nameLength = int.Parse(update.Substring(startIndex, 5));
                string userName = update.Substring(startIndex + 5, nameLength);

                Dispatcher.Invoke(() =>
                {
                    if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                    {
                        // Add the logged-in user with updated status
                        friends.Add(new User
                        {
                            Name = userName,
                            Status = ParseStatus("2").ToString(),
                            IsFriend = false,
                            IsFriendRequest = false,
                        });
                        // Optionally, sort the friends list after adding
                        SortFriendsList(friends);
                    }
                });
                // Move to the next user's data
                startIndex += 5 + nameLength;
            }
        }

        private void HandleDeleteProject(string update)
        {
            string deletedProjectName = update.Substring(3); // Assuming update format is "DEL:ProjectName"

            Dispatcher.Invoke(() =>
            {
                if (lstProjects.ItemsSource is ObservableCollection<string> projects)
                {
                    // Find the index of the project to delete
                    int deleteIndex = projects.IndexOf(deletedProjectName);

                    // If the project is found, remove it from the list
                    if (deleteIndex != -1)
                    {
                        projects.RemoveAt(deleteIndex);
                    }
                }
            });
        }

        private void HandleMoveToCreateWindow(string update)
        {
            disconnect = false;
            isListeningToServer = false;
            Dispatcher.Invoke(() =>
            {
                AddProjectWindow addProjectWindow = new AddProjectWindow(communicator);
                addProjectWindow.Show();
                Close();
            });
        }
        
        private void HandleDisconnect(string update)
        {
            try
            {
                int nameIndex = 3;
                string userName = update.Substring(nameIndex);

                Dispatcher.Invoke(() =>
                {
                    if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                    {
                        User userToUpdate = friends.FirstOrDefault(u => u.Name == userName);
                        if (userToUpdate != null)
                        {
                            friends.Remove(userToUpdate);
                            friends.Add(new User { Name = userName, Status = ParseStatus("0").ToString() });
                            SortFriendsList(friends);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Disconnect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleLogin(string update)
        {
            try
            {
                int nameIndex = 3;
                string userName = update.Substring(nameIndex);

                Dispatcher.Invoke(() =>
                {
                    if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                    {
                        User userToUpdate = friends.FirstOrDefault(u => u.Name == userName);
                        if (userToUpdate != null)
                        {
                            friends.Remove(userToUpdate);
                            friends.Add(new User { Name = userName, Status = ParseStatus("1").ToString() });
                            SortFriendsList(friends);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Login response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleReciveInfo(string msg)
        {
            string profileRepCode = msg.Substring(0, 3);

            if (profileRepCode == ((int)MessageCodes.MC_PROFILE_INFO_RESP).ToString() && msg.Length > 3)
            {
                int userNameLen = int.Parse(msg.Substring(3, 5));
                string userName = msg.Substring(8, userNameLen);

                int emailLenPos = 8 + userNameLen;
                int emailLen = int.Parse(msg.Substring(emailLenPos, 5));
                string email = msg.Substring(emailLenPos + 5, emailLen);

                int bioLenPos = emailLenPos + 5 + emailLen;
                int bioLen = int.Parse(msg.Substring(bioLenPos, 5));
                string bio = msg.Substring(bioLenPos + 5, bioLen);
                Dispatcher.Invoke(() =>
                {
                    displayedUserProfile = new UserProfile { UserName = userName, Email = email, Bio = bio };
                    DataContext = displayedUserProfile;

                    if (userName == communicator.UserName)
                    {
                        displayedUserProfile.IsCurrentUserProfile = true;
                        editButton.Visibility = Visibility.Visible; // Hide edit button for other users' profiles
                        backButton.Visibility = Visibility.Collapsed;
                        BioTextBox.Visibility = Visibility.Visible;
                        BioTextBlock.Visibility = Visibility.Collapsed;
                        addFriendText.Visibility = Visibility.Collapsed;
                        addFriendBtn.Visibility = Visibility.Collapsed;
                        closeSerachBtn.Visibility = Visibility.Collapsed;
                        return;
                    }
                    else if(inSearch)
                    {
                        displayedUserProfile.IsCurrentUserProfile = false;
                        editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                        backButton.Visibility = Visibility.Visible;
                        BioTextBox.Visibility = Visibility.Collapsed;
                        BioTextBlock.Visibility = Visibility.Visible;
                        addFriendText.Visibility = Visibility.Visible;
                        addFriendBtn.Visibility = Visibility.Visible;
                        closeSerachBtn.Visibility = Visibility.Visible;
                        return;
                    }
                    else
                    {
                        displayedUserProfile.IsCurrentUserProfile = false;
                        editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                        backButton.Visibility = Visibility.Visible;
                        BioTextBox.Visibility = Visibility.Collapsed;
                        BioTextBlock.Visibility = Visibility.Visible;
                        addFriendText.Visibility = Visibility.Collapsed;
                        addFriendBtn.Visibility = Visibility.Collapsed;
                        closeSerachBtn.Visibility = Visibility.Collapsed;
                        return;
                    }
                });
            }
        }

        private void HandleReciveProjects(string msg)
        {
            string projectsRepCode = msg.Substring(0, 3);

            if (projectsRepCode == ((int)MessageCodes.MC_PROJECTS_LIST_RESP).ToString() && msg.Length > 3)
            {
                int index = 3;
                ObservableCollection<string> projects = new ObservableCollection<string>();
                while (index < msg.Length)
                {
                    int projectNameLen = int.Parse(msg.Substring(index, 5));
                    index += 5;
                    string projectName = msg.Substring(index, projectNameLen);
                    index += projectNameLen;

                    projects.Add(projectName);
                }

                Dispatcher.Invoke(() => lstProjects.ItemsSource = projects);
            }
        }

        private void HandleRemoveUser(string msg)
        {
            // Assuming the message format is "{code}{userName}"
            int nameIndex = 3; // Adjust this index based on your actual message format

            // Extract user name
            string userName = msg.Substring(nameIndex);

            if (lstFriends.ItemsSource is ObservableCollection<User> friends)
            {
                Dispatcher.Invoke(() =>
                {
                    User userToRemove = friends.FirstOrDefault(u => u.Name == userName);
                    if (userToRemove != null)
                    {
                        friends.Remove(userToRemove); // Modify the ObservableCollection
                    }
                });
            }
        }

        private void HandleLogout(string update)
        {
            disconnect = false;
            isListeningToServer = false;

            Dispatcher.Invoke(() =>
            {
                LoginWindow loginWindow = new LoginWindow(communicator);
                loginWindow.Show();
                Close();
            });
        }

        private async void HomePage_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();
                    string fullMessage = $"{chatMessageCode}{communicator.UserId:D5}";
                    communicator.SendData(fullMessage);
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void closeSearch_Click(object sender, RoutedEventArgs e)
        {
            inSearch = false;
            searchBarTextBox.Text = "";
            closeSerachBtn.Visibility = Visibility.Collapsed;

            string friendsListCode = ((int)MessageCodes.MC_FRIENDS_LIST_REQUEST).ToString();
            communicator.SendData($"{friendsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");
        }

        private void LstFriends_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var selectedFriend = lstFriends.SelectedItem as User;
            if (selectedFriend != null)
            {
                string profileInfoCode = ((int)MessageCodes.MC_PROFILE_INFO_REQUEST).ToString();
                string userProjects = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
                communicator.SendData($"{profileInfoCode}{selectedFriend.Name.Length:D5}{selectedFriend.Name}");
                communicator.SendData($"{userProjects}{selectedFriend.Name.Length:D5}{selectedFriend.Name}");
            }
        }

        private void LstProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var selectedProject = lstProjects.SelectedItem as string;
            if (selectedProject != null)
            {
                string joinProjectCode = ((int)MessageCodes.MC_JOIN_PROJECT_REQUEST).ToString();
                communicator.SendData($"{joinProjectCode}{selectedProject.Length:D5}{selectedProject}");
            }
        }

        private void LstFiles_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back)
            {
                deleteProject(sender, e);
            }
        }

        private void TxtSearchUsers_KeyUp(object sender, KeyEventArgs e)
        {
            string searchCommand = searchBarTextBox.Text;
            inSearch = true;
            closeSerachBtn.Visibility = Visibility.Visible;
            string joinProjectCode = ((int)MessageCodes.MC_SEARCH_REQUEST).ToString();
            communicator.SendData($"{joinProjectCode}{searchCommand.Length:D5}{searchCommand}");
        }

        private void deleteProject(object sender, RoutedEventArgs e)
        {
            var selectedProject = lstProjects.SelectedItem as string;
            if (selectedProject != null)
            {
                string code = ((int)MessageCodes.MC_DELETE_PROJECT_REQUEST).ToString();
                communicator.SendData($"{code}{selectedProject.Length:D5}{selectedProject}");
            }
        }

        private void ViewUserProfile(string userName)
        {
            string profileInfoCode = ((int)MessageCodes.MC_PROFILE_INFO_REQUEST).ToString();
            communicator.SendData($"{profileInfoCode}{userName.Length:D5}{userName}");

            string profileRep = communicator.ReceiveData();
            string profileRepCode = profileRep.Substring(0, 3);

            if (profileRepCode == ((int)MessageCodes.MC_PROFILE_INFO_RESP).ToString() && profileRep.Length > 3)
            {
                int userNameLen = int.Parse(profileRep.Substring(3, 5));
                string userNameResp = profileRep.Substring(8, userNameLen);

                int emailLenPos = 8 + userNameLen;
                int emailLen = int.Parse(profileRep.Substring(emailLenPos, 5));
                string email = profileRep.Substring(emailLenPos + 5, emailLen);

                int bioLenPos = emailLenPos + 5 + emailLen;
                int bioLen = int.Parse(profileRep.Substring(bioLenPos, 5));
                string bio = profileRep.Substring(bioLenPos + 5, bioLen);

                displayedUserProfile = new UserProfile { ProfileImage = null, UserName = userNameResp, Email = email, Bio = bio };

                // Check if it's the current user's profile
                if (userNameResp == communicator.UserName)
                {
                    displayedUserProfile.IsCurrentUserProfile = true;
                    editButton.Visibility = Visibility.Visible; // Hide edit button for other users' profiles
                    backButton.Visibility = Visibility.Collapsed;
                    BioTextBox.Visibility = Visibility.Visible;
                    BioTextBlock.Visibility = Visibility.Collapsed;
                    addFriendBtn.Visibility = Visibility.Collapsed;
                    addFriendText.Visibility = Visibility.Collapsed;
                    closeSerachBtn.Visibility = Visibility.Collapsed;
                    return;
                }
                else if (inSearch)
                {
                    displayedUserProfile.IsCurrentUserProfile = false;
                    editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                    backButton.Visibility = Visibility.Visible;
                    BioTextBox.Visibility = Visibility.Collapsed;
                    BioTextBlock.Visibility = Visibility.Visible;
                    addFriendBtn.Visibility = Visibility.Visible;
                    addFriendText.Visibility = Visibility.Visible;
                    closeSerachBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    displayedUserProfile.IsCurrentUserProfile = false;
                    editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                    backButton.Visibility = Visibility.Visible;
                    BioTextBox.Visibility = Visibility.Collapsed;
                    BioTextBlock.Visibility = Visibility.Visible;
                    addFriendBtn.Visibility = Visibility.Collapsed;
                    addFriendText.Visibility = Visibility.Collapsed;
                    closeSerachBtn.Visibility = Visibility.Collapsed;
                }

                DataContext = displayedUserProfile;
                
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            displayedUserProfile = loggedUserProfile;
            DataContext = displayedUserProfile;
            string projectsListCode = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
            communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");
            backButton.Visibility = Visibility.Collapsed;
            editButton.Visibility = Visibility.Visible;
            BioTextBox.Visibility = Visibility.Visible;
            BioTextBlock.Visibility = Visibility.Collapsed;
            addFriendText.Visibility = Visibility.Collapsed;
            addFriendBtn.Visibility = Visibility.Collapsed;
        }
        
        private void AddProject_Click(object sender, RoutedEventArgs e)
        {
            string command = ((int)MessageCodes.MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST).ToString();
            string message = $"{command}";
            communicator.SendData(message);
        }

        private void EditBio_Click(object sender, RoutedEventArgs e)
        {
            string newBio = BioTextBox.Text;

            string profileInfoCode = ((int)MessageCodes.MC_EDIT_PROFILE_INFO_REQUEST).ToString();
            string message = $"{profileInfoCode}{newBio.Length:D5}{newBio}";
            communicator.SendData(message);
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            string code = ((int)MessageCodes.MC_LOGOUT_REQUEST).ToString();
            communicator.SendData($"{code}");
        }

        private void approveRequest_Click(object sender, RoutedEventArgs e)
        {

            Button button = sender as Button;
            if (button != null)
            {
                User user = button.CommandParameter as User;
                if (user != null)
                {
                    string addFriendCode = ((int)MessageCodes.MC_APPROVE_FRIEND_REQ_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{user.Name.Length:D5}{user.Name}");
                }
            }
        }

        private void declineRequest_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                User user = button.CommandParameter as User;
                if (user != null)
                {
                    string rejectFriendCode = ((int)MessageCodes.MC_REJECT_FRIEND_REQ_REQUEST).ToString();
                    communicator.SendData($"{rejectFriendCode}{user.Name.Length:D5}{user.Name}");
                }
            }
        }

        private void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                User user = button.CommandParameter as User;
                if (user != null)
                {
                    // Handle removing the user (e.g., send request to server, update UI)
                    string removeFriendCode = ((int)MessageCodes.MC_REMOVE_FRIEND_REQUEST).ToString();
                    communicator.SendData($"{removeFriendCode}{communicator.UserName.Length:D5}{communicator.UserName}{user.Name.Length:D5}{user.Name}");
                }
            }
        }

        private void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            string addFriendCode = ((int)MessageCodes.MC_ADD_FRIEND_REQUEST).ToString();
            communicator.SendData($"{addFriendCode}{displayedUserProfile.UserName.Length:D5}{displayedUserProfile.UserName}");
        }

        private void SortFriendsList(ObservableCollection<User> friends)
        {
            var sortedFriends = new ObservableCollection<User>(friends.OrderBy(u => u.Name));
            lstFriends.ItemsSource = sortedFriends;
        }

        private Status ParseStatus(string statusCode)
        {
            return statusCode switch
            {
                "0" => Status.Offline,
                "1" => Status.Online,
                "2" => Status.search,
                "3" => Status.search,
                _ => throw new ArgumentException($"Invalid status code: {statusCode}"),
            };
        }

        public class UserProfile : INotifyPropertyChanged
        {
            private BitmapImage _profileImage;

            public BitmapImage ProfileImage
            {
                get { return _profileImage; }
                set
                {
                    _profileImage = value;
                    RaisePropertyChanged(nameof(ProfileImage));
                }
            }

            // Implement INotifyPropertyChanged
            public event PropertyChangedEventHandler PropertyChanged;

            public void RaisePropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public string UserName { get; set; }
            public string Email { get; set; }
            public string Bio { get; set; }
            public bool IsCurrentUserProfile { get; set; }
        }


        public class User
        {
            private bool _isFriend;
            private bool _isFriendRequest;

            public string Name { get; set; }
            public string Status { get; set; }

            public bool IsFriend
            {
                get { return _isFriend; }
                set
                {
                    _isFriend = value;
                    OnPropertyChanged(nameof(IsFriend));
                }
            }

            public bool IsFriendRequest
            {
                get { return _isFriendRequest; }
                set
                {
                    _isFriendRequest = value;
                    OnPropertyChanged(nameof(IsFriendRequest));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public enum Status
        {
            Offline,
            Online,
            search
        }

    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}