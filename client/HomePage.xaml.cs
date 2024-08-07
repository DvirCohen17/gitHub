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
using System.Collections.Generic;
using Microsoft.Win32;
using System.IO;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Net.WebRequestMethods;
using Microsoft.VisualBasic;
using System.Windows.Media;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace client_side
{
    public partial class HomePage : System.Windows.Window, INotifyPropertyChanged // Specified full namespace for Window
    {
        private Communicator communicator;
        private bool disconnect = true;
        private Thread receiveServerUpdatesThread;
        private bool isListeningToServer = true;
        private UserProfile loggedUserProfile;
        private UserProfile displayedUserProfile;
        private bool waitingForImage = false;
        private int imageSize;
        private string theame;


        private bool inSearch = false;
        private int MessageCount = 0;
        private string _messageCountStr;

        private ObservableCollection<User> friends { get; set; }
        private ObservableCollection<ProjectInfo> projects { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private string mailImage;
        private string imageMode;
        private bool inMessagesMode = false;
        private bool inSettings = false;
        private bool inCreate = false;

        public HomePage(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            this.communicator = communicator;
            DataContext = this;

            communicator.ApplyTheme(this);
            communicator.ThemeChanged += OnThemeChanged;

            lstFriends.MouseDoubleClick += LstFriends_MouseDoubleClick;
            lstProjects.MouseDoubleClick += LstProjects_MouseDoubleClick;

            // Initialize Friends collection
            Friends = new ObservableCollection<User>();
            lstFriends.ItemsSource = Friends;

            Projects = new ObservableCollection<ProjectInfo>();
            lstProjects.ItemsSource = Projects;

            theame = communicator.AppTheme.theame;

            EnableAllButtonsAndLists();

            start();
            openMailImage();
            msgBtn.Background = Brushes.Transparent;
            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();

            Closing += HomePage_CloseFile;
        }

        private void RefreshUIElements()
        {
            lstProjects.ItemsSource = null;
            lstProjects.ItemsSource = Projects;

            lstFriends.ItemsSource = null;
            lstFriends.ItemsSource = Friends;

            SortFriendsList(Friends);
            SortProjectsList(Projects);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            communicator.ApplyTheme(this);
            openMailImage();
            msgBtn.Background = Brushes.Transparent;

            RefreshUIElements();

            if (inSettings || inMessagesMode)
            {
                DisableAllButtonsAndLists();
            }
            else
            {
                EnableAllButtonsAndLists();
            }
        }

        private void EnableAllButtonsAndLists()
        {
            SetButtonsAndListsEnabledState(true);
            EnableDoubleClickEvents();
        }

        private void DisableAllButtonsAndLists()
        {
            SetButtonsAndListsEnabledState(false);
            DisableDoubleClickEvents();
        }

        private void SetButtonsAndListsEnabledState(bool isEnabled)
        {
            AddProjectBtn.IsEnabled = isEnabled;
            settingsBtn.IsEnabled = isEnabled;
            backButton.IsEnabled = isEnabled;
            msgBtn.IsEnabled = isEnabled;
            closeSerachBtn.IsEnabled = isEnabled;
            addFriendBtn.IsEnabled = isEnabled;
            editButton.IsEnabled = isEnabled;
            LogoutBtn.IsEnabled = isEnabled;
            searchBarTextBox.IsEnabled = isEnabled;
            BioTextBox.IsEnabled = isEnabled;

            lstProjects.UpdateLayout();
            lstFriends.UpdateLayout();

            foreach (var item in lstProjects.Items)
            {
                ListViewItem container = lstProjects.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                if (container != null)
                {
                    SetButtonsInContainerEnabledState(container, isEnabled);
                }
            }

            foreach (var item in lstFriends.Items)
            {
                ListBoxItem container = lstFriends.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (container != null)
                {
                    SetButtonsInContainerEnabledState(container, isEnabled);
                }
            }
        }

        private void EnableDoubleClickEvents()
        {
            lstProjects.MouseDoubleClick += LstProjects_MouseDoubleClick;
            lstFriends.MouseDoubleClick += LstFriends_MouseDoubleClick;
        }

        private void DisableDoubleClickEvents()
        {
            lstProjects.MouseDoubleClick -= LstProjects_MouseDoubleClick;
            lstFriends.MouseDoubleClick -= LstFriends_MouseDoubleClick;
        }

        private void SetButtonsInContainerEnabledState(DependencyObject container, bool isEnabled)
        {
            foreach (var child in GetChildren(container))
            {
                if (child is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = isEnabled;
                }
            }
        }

        private IEnumerable<DependencyObject> GetChildren(DependencyObject parent)
        {
            if (parent == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var grandChild in GetChildren(child))
                {
                    yield return grandChild;
                }
            }
        }

        private void openMailImage()
        {
            if (MessageCount == 0)
            {

                switch (communicator.AppTheme.theame)
                {
                    case "Light":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Light\\No_Dot.png";
                        break;
                    case "Dark":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Light\\No_Dot.png";
                        break;
                    case "Blue":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Blue\\No_Dot.png";
                        break;
                    case "Green":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Green\\No_Dot.png";
                        break;
                    case "Red":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Red\\No_Dot.png";
                        break;
                    case "CyberPunk":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_CyberPunk\\No_Dot.png";
                        break;
                    case "Matrix":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Matrix\\No_Dot.png";
                        break;
                    case "Solarized Dark":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_SolarizedDark\\No_Dot.png";
                        break;
                    case "Solarized Light":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_SolarizedLight\\No_Dot.png";
                        break;
                    case "Vintage":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Vintage\\No_Dot.png";
                        break;
                    case "Neon":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Matrix\\No_Dot.png";
                        break;
                    case "Pastel":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Vintage\\No_Dot.png";
                        break;

                        // Handle other themes if needed
                }
            }
            else
            {
                switch (communicator.AppTheme.theame)
                {
                    case "Light":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Light\\Dot.png";
                        break;
                    case "Dark":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Light\\Dot.png";
                        break;
                    case "Blue":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Blue\\Dot.png";
                        break;
                    case "Green":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Green\\Dot.png";
                        break;
                    case "Red":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Red\\Dot.png";
                        break;
                    case "CyberPunk":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_CyberPunk\\Dot.png";
                        break;
                    case "Matrix":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Matrix\\Dot.png";
                        break;
                    case "Solarized Dark":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_SolarizedDark\\Dot.png";
                        break;
                    case "Solarized Light":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_SolarizedLight\\Dot.png";
                        break;
                    case "Vintage":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Vintage\\Dot.png";
                        break;
                    case "Neon":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Matrix\\Dot.png";
                        break;
                    case "Pastel":
                        mailImage = "C:\\githubDemo\\data\\MailImages\\mail_image_Vintage\\Dot.png";
                        break;

                        // Handle other themes if needed
                }
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(mailImage, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Cache image for better performance
            bitmap.EndInit();

            MailImage.Source = bitmap;

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
            LoadMessagesCount();
        }

        private void LoadMessagesCount()
        {
            string projectsListCode = ((int)MessageCodes.MC_GET_MSG_COUNT_REQUEST).ToString();
            communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string response = communicator.ReceiveData();
            string responseCode = response.Substring(0, 3);

            if (responseCode == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
            {
                response = communicator.ReceiveData();
                responseCode = response.Substring(0, 3);
            }
            if (responseCode == ((int)MessageCodes.MC_GET_MESSAGES_RESP).ToString())
            {
                MessageCount = int.Parse(response.Substring(3, 5));
                Dispatcher.Invoke(() =>
                {
                    msgCountTextBlock.Text = MessageCount.ToString();
                });
                if (MessageCount > 0)
                {
                    msgCountTextBlock.Visibility = Visibility.Visible;
                    imageMode = "dot";
                }
                else
                {
                    msgCountTextBlock.Visibility = Visibility.Collapsed;
                    imageMode = "no dot";
                }
            }
        }

        private void LoadProfileInfo()
        {
            string profileInfoCode = ((int)MessageCodes.MC_PROFILE_INFO_REQUEST).ToString();
            communicator.SendData($"{profileInfoCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string profileRep = communicator.ReceiveData();
            string profileRepCode = profileRep.Substring(0, 3);

            if (profileRepCode == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
            {
                profileRep = communicator.ReceiveData();
                profileRepCode = profileRep.Substring(0, 3);
            }

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
                    AddProjectBtn.Visibility = Visibility.Visible;
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
                    AddProjectBtn.Visibility = Visibility.Collapsed;
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
            if (friendsRep.Substring(0,3) == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
            {
                friendsRep = communicator.ReceiveData();
            }

            HandleLoadFriendsList(friendsRep);
        }

        private void HandleLoadFriendsList(string update)
        {
            string friendsRepCode = update.Substring(0, 3);

            if (friendsRepCode == ((int)MessageCodes.MC_FRIENDS_LIST_RESP).ToString())
            {
                int index = 3;
                ObservableCollection<User> updatedFriends = new ObservableCollection<User>();

                while (index < update.Length)
                {
                    int friendNameLen = int.Parse(update.Substring(index, 5));
                    index += 5;
                    string friendName = update.Substring(index, friendNameLen);
                    index += friendNameLen;
                    string onlineStatus = update.Substring(index, 1);
                    index += 1;

                    bool isFriend = true;
                    bool isFriendRequest = false;

                    if (onlineStatus == "0" || onlineStatus == "1")
                    {
                        isFriend = true;
                        isFriendRequest = false;
                    }
                    else if (onlineStatus == "3")
                    {
                        isFriend = false;
                        isFriendRequest = true;

                        if (theame == "Light")
                        {
                            onlineStatus = "4";
                        }
                    }

                    updatedFriends.Add(new User
                    {
                        Name = friendName,
                        Status = ParseStatus(onlineStatus).ToString(),
                        IsFriend = isFriend,
                        IsFriendRequest = isFriendRequest,
                    });
                }

                // Update the Friends collection on the UI thread
                Dispatcher.Invoke(() =>
                {

                    // Clear existing items
                    if (Friends.Any())
                    {
                        Friends.Clear();
                    }
                    // Add updated friends to collection
                    foreach (var friend in updatedFriends)
                    {
                        Friends.Add(friend);
                    }

                    // Sort the updated list
                    SortFriendsList(Friends);
                });
            }
        }

        private void LoadProjectsList()
        {
            string projectsListCode = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
            communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string projectsRep = communicator.ReceiveData();
            HandleLoadProjectsList(projectsRep);
        }

        private void GetProjectsList()
        {
            string projectsListCode = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
            communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

        }

        private void HandleLoadProjectsList(string update)
        {
            string projectsRepCode = update.Substring(0, 3);
            string projectsRep = update.Substring(3);

            if (projectsRepCode == ((int)MessageCodes.MC_PROJECTS_LIST_RESP).ToString())
            {
                int index = 0;
                ObservableCollection<ProjectInfo> updatedProjects = new ObservableCollection<ProjectInfo>();
                while (index < projectsRep.Length)
                {
                    int projectNameLen = int.Parse(projectsRep.Substring(index, 5));
                    index += 5;
                    string projectName = projectsRep.Substring(index, projectNameLen);
                    index += projectNameLen;

                    int roleLen = int.Parse(projectsRep.Substring(index, 5));
                    index += 5;
                    string role = projectsRep.Substring(index, roleLen);
                    index += roleLen;

                    int idLen = int.Parse(projectsRep.Substring(index, 5));
                    index += 5;
                    int id = int.Parse(projectsRep.Substring(index, idLen));
                    index += idLen;

                    updatedProjects.Add(new ProjectInfo
                    {
                        ProjectName = projectName,
                        Role = role,
                        ProjectId = id
                    });
                }

                Dispatcher.Invoke(() =>
                {

                    // Clear existing items
                    if (Projects.Any())
                    {
                        Projects.Clear();
                    }
                    // Add updated friends to collection
                    foreach (var project in updatedProjects)
                    {
                        Projects.Add(project);
                    }

                    // Sort the updated list
                    SortProjectsList(projects);
                });
            }
        }

        private async void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    if (waitingForImage)
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
                            HandleLoadProjectsList(update);
                            break;
                        case "147":
                            GetProjectsList();
                            break;
                        case "241":
                            HandleReciveProjects(update);
                            break;
                        case "242":
                            HandleLeaveProject(update);
                            break;
                        case "223":
                            HandleAddFriend(update);
                            break;
                        case "236":
                            HandleApproveFriendReq(update);
                            break;
                        case "237":
                            HandleRejectFriendReq(update);
                            break;
                        case "238":
                            HandleAddFriendReq(update);
                            break;
                        case "224":
                            HandleRemoveFriend(update);
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
                        case "254":
                            HandleMoveToMessagesWindow(update);
                            break;
                        case "300":
                            HandleDisconnect(update);
                            break;
                        case "251":
                            HandleMoveToSettings(update);
                            break;
                        case "256":
                            HandleAddMsg(update);
                            break;
                        case "307":
                            break;
                        default:
                            throw new InvalidOperationException($"{code}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "An existing connection was forcibly closed by the remote host.")
                { 
                    MessageBox.Show($"Error receiving server updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    string msg = await Task.Run(() => communicator.ReceiveData());
                }
            }
        }

        private void HandleAddMsg(string msg)
        {
            try
            {

                Dispatcher.Invoke(() =>
                {
                    MessageCount++;

                    if (MessageCount == 1)
                    {
                        imageMode = "dot";
                        msgCountTextBlock.Visibility = Visibility.Visible;
                        openMailImage();

                    }

                    msgCountTextBlock.Text = MessageCount.ToString();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Remove User response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleLeaveProject(string msg)
        {
            try
            {
                string projectsListCode = ((int)MessageCodes.MC_PROJECTS_LIST_REQUEST).ToString();
                communicator.SendData($"{projectsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Remove User response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleAddFriend(string update)
        {
            try
            {
                int nameIndex = 3;
                int userNameLen = int.Parse(update.Substring(nameIndex, 5));
                nameIndex += 5;
                string userName = update.Substring(nameIndex, userNameLen);
                nameIndex += userNameLen;
                string status = update.Substring(nameIndex);

                if (!inSearch)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Check if Friends collection is initialized
                        if (Friends == null)
                            Friends = new ObservableCollection<User>();

                        // Add new user to the Friends collection
                        Friends.Add(new User
                        {
                            Name = userName,
                            Status = ParseStatus(status).ToString(),
                            IsFriend = true,
                            IsFriendRequest = false,
                        });

                        // Sort the Friends list
                        SortFriendsList(Friends);
                    });
                }
                else
                {
                }
                Dispatcher.Invoke(() =>
                {
                    if (userName == displayedUserProfile.UserName)
                    {
                        addFriendBtn.Visibility = Visibility.Collapsed;
                        addFriendText.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Add Friend response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleAddFriendReq(string update)
        {
            try
            {
                int nameIndex = 3;
                int userNameLen = int.Parse(update.Substring(nameIndex, 5));
                nameIndex += 5;
                string userName = update.Substring(nameIndex, userNameLen);
                nameIndex += userNameLen;
                string status = update.Substring(nameIndex);

                Dispatcher.Invoke(() =>
                {
                    // Check if Friends collection is initialized
                    if (Friends == null)
                        Friends = new ObservableCollection<User>();

                    // Check if the friend request already exists
                    bool alreadyExists = Friends.Any(f => f.Name == userName && f.IsFriendRequest);

                    if (!alreadyExists)
                    {
                        // Add new user to the Friends collection
                        Friends.Add(new User
                        {
                            Name = userName,
                            Status = ParseStatus(status).ToString(),
                            IsFriend = false,
                            IsFriendRequest = true,
                        });

                        // Sort the Friends list
                        SortFriendsList(Friends);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Add Friend response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleRemoveFriend(string msg)
        {
            try
            {
                string friendsListCode = ((int)MessageCodes.MC_FRIENDS_LIST_REQUEST).ToString();
                communicator.SendData($"{friendsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Remove User response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleApproveFriendReq(string update)
        {
            try
            {
                int nameIndex = 3;
                int userNameLen = int.Parse(update.Substring(nameIndex, 5));
                nameIndex += 5;
                string userName = update.Substring(nameIndex, userNameLen);
                nameIndex += userNameLen;
                string status = update.Substring(nameIndex);

                Dispatcher.Invoke(() =>
                {
                    if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                    {
                        User userToUpdate = friends.FirstOrDefault(u => u.Name == userName);
                        if (userToUpdate != null)
                        {
                            friends.Remove(userToUpdate);
                            friends.Add(new User
                            {
                                Name = userName,
                                Status = ParseStatus(status).ToString(),
                                IsFriend = true,
                                IsFriendRequest = false,
                            });
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

        private void HandleRejectFriendReq(string update)
        {
            try
            {
                int nameIndex = 3;
                int userNameLen = int.Parse(update.Substring(nameIndex, 5));
                nameIndex += 5;
                string userName = update.Substring(nameIndex, userNameLen);
                nameIndex += userNameLen;

                string friendsListCode = ((int)MessageCodes.MC_FRIENDS_LIST_REQUEST).ToString();
                communicator.SendData($"{friendsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Login response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            int index = 3;

            int projectLength = int.Parse(update.Substring(index, 5));
            index += 5;
            string ProjectName = update.Substring(index, projectLength);
            index += projectLength;

            int projectIdLength = int.Parse(update.Substring(index, 5));
            index += 5;
            int projectId = int.Parse(update.Substring(index, projectIdLength));
            index += projectIdLength;

            int codeLanLen = int.Parse(update.Substring(index, 5));
            index += 5;
            string codeLan = update.Substring(index, codeLanLen);
            index += codeLanLen;

            string mode = update.Substring(index);
            bool isEditable = mode == "true" ? true : false;

            Dispatcher.Invoke(() =>
            {
                ProjectDirectory TextEditorWindow = new ProjectDirectory(communicator, ProjectName, projectId, codeLan, isEditable);
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
                startIndex += nameLength + 5;

                string onlineStatus = update.Substring(startIndex, 1);
                startIndex += 1;

                Dispatcher.Invoke(() =>
                {
                    if (lstFriends.ItemsSource is ObservableCollection<User> friends)
                    {
                        // Add the logged-in user with updated status
                        friends.Add(new User
                        {
                            Name = userName,
                            Status = ParseStatus(onlineStatus).ToString(),
                            IsFriend = false,
                            IsFriendRequest = false,
                        });
                        // Optionally, sort the friends list after adding
                        SortFriendsList(friends);
                    }
                });

            }
        }

        private void HandleMoveToCreateWindow(string update)
        {
            int index = 3;
            int id = -1;
            int nameLen = int.Parse(update.Substring(index, 5));
            index += 5;
            string name = update.Substring(index, nameLen);
            index += nameLen;
            if (nameLen > 0)
            {
                int iDLen = int.Parse(update.Substring(index, 5));
                index += 5;
                id = int.Parse(update.Substring(index, iDLen));
                index += iDLen;

            }
            string mode = update.Substring(index);

            isListeningToServer = false;
            inCreate = true;
            Dispatcher.Invoke(() =>
            {
                //AddProjectWindow addProjectWindow = new AddProjectWindow(communicator);
                DisableAllButtonsAndLists();
                AddProjectWindow addProjectWindow = new AddProjectWindow(communicator, mode, name, id);
                addProjectWindow.Closed += CreateWindow_Closed; // Attach the event handler
                addProjectWindow.Show();
            });
        }

        private void CreateWindow_Closed(object sender, EventArgs e)
        {
            EnableAllButtonsAndLists();
            start();
            isListeningToServer = true;
            inCreate = false;
            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();
        }

        private void HandleMoveToMessagesWindow(string update)
        {
            isListeningToServer = false;
            inMessagesMode = true;
            Dispatcher.Invoke(() =>
            {
                DisableAllButtonsAndLists();
                MessagesWindow messagesWindow = new MessagesWindow(communicator);
                messagesWindow.Closed += MessagesWindow_Closed; // Attach the event handler
                messagesWindow.Show();
            });
        }

        private void MessagesWindow_Closed(object sender, EventArgs e)
        {
            EnableAllButtonsAndLists();
            start();
            openMailImage();
            isListeningToServer = true;
            inMessagesMode = false;
            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();
        }

        private void HandleMoveToSettings(string update)
        {
            isListeningToServer = false;
            inSettings = true;
            Dispatcher.Invoke(() =>
            {
                DisableAllButtonsAndLists();
                settingsWindow messagesWindow = new settingsWindow(communicator);
                messagesWindow.Closed += SettingsWindow_Closed; // Attach the event handler
                messagesWindow.Show();
            });
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            isListeningToServer = true;
            inSettings = false;

            communicator.ApplyTheme(this);
            theame = communicator.AppTheme.theame;

            EnableAllButtonsAndLists();

            start();
            openMailImage();
            msgBtn.Background = Brushes.Transparent;
            
            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();
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
                            friends.Add(new User
                            {
                                Name = userName,
                                Status = ParseStatus("0").ToString(),
                                IsFriend = true,
                                IsFriendRequest = false,
                            });
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
                            friends.Add(new User
                            {
                                Name = userName,
                                Status = ParseStatus("1").ToString(),
                                IsFriend = true,
                                IsFriendRequest = false,
                            });
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

                string friendStatus = msg.Substring(bioLenPos + 5 + bioLen);
                bool isFriend = (friendStatus == "0") ? (true) : (false);
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
                        AddProjectBtn.Visibility = Visibility.Visible;
                        return;
                    }
                    else if (inSearch && !isFriend)
                    {
                        displayedUserProfile.IsCurrentUserProfile = false;
                        editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                        backButton.Visibility = Visibility.Visible;
                        BioTextBox.Visibility = Visibility.Collapsed;
                        BioTextBlock.Visibility = Visibility.Visible;
                        addFriendText.Visibility = Visibility.Visible;
                        addFriendBtn.Visibility = Visibility.Visible;
                        closeSerachBtn.Visibility = Visibility.Visible;
                        AddProjectBtn.Visibility = Visibility.Collapsed;
                        return;
                    }
                    else if (inSearch && isFriend)
                    {
                        displayedUserProfile.IsCurrentUserProfile = false;
                        editButton.Visibility = Visibility.Collapsed; // Hide edit button for other users' profiles
                        backButton.Visibility = Visibility.Visible;
                        BioTextBox.Visibility = Visibility.Collapsed;
                        BioTextBlock.Visibility = Visibility.Visible;
                        addFriendText.Visibility = Visibility.Collapsed;
                        addFriendBtn.Visibility = Visibility.Collapsed;
                        closeSerachBtn.Visibility = Visibility.Visible;
                        AddProjectBtn.Visibility = Visibility.Collapsed;
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
                        AddProjectBtn.Visibility = Visibility.Collapsed;
                        return;
                    }
                });
            }
        }

        private void HandleReciveProjects(string msg)
        {
            string projectsRepCode = msg.Substring(0, 3);

            Dispatcher.Invoke(() =>
            {
                if (lstProjects.ItemsSource is ObservableCollection<ProjectInfo> projects)
                {
                    projects.Clear();
                }
            });

            if (projectsRepCode == ((int)MessageCodes.MC_USER_PROJECTS_LIST_RESP).ToString() && msg.Length > 3)
            {
                int index = 3;
                ObservableCollection<ProjectInfo> projects = new ObservableCollection<ProjectInfo>();
                while (index < msg.Length)
                {
                    int projectNameLen = int.Parse(msg.Substring(index, 5));
                    index += 5;
                    string projectName = msg.Substring(index, projectNameLen);
                    index += projectNameLen;

                    int projectIdLength = int.Parse(msg.Substring(index, 5));
                    index += 5;
                    int projectId = int.Parse(msg.Substring(index, projectIdLength));
                    index += projectIdLength;
                    Dispatcher.Invoke(() =>
                    {
                        if (lstProjects.ItemsSource is ObservableCollection<ProjectInfo> projects)
                        {
                            projects.Add(new ProjectInfo
                            {
                                ProjectName = projectName,
                                Role = "",
                                ProjectId = projectId
                            });
                            SortProjectsList(projects);
                        }
                    });
                }
            }
        }

        private void HandleLogout(string update)
        {
            disconnect = false;
            isListeningToServer = false;

            communicator.ClearCredentials();
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
            if (!inMessagesMode || !inCreate || !inSettings)
            {
                e.Handled = true;
                var selectedFriend = lstFriends.SelectedItem as User;
                if (selectedFriend != null)
                {
                    string profileInfoCode = ((int)MessageCodes.MC_PROFILE_INFO_REQUEST).ToString();
                    string userProjects = ((int)MessageCodes.MC_USER_PROJECTS_LIST_REQUEST).ToString();
                    communicator.SendData($"{profileInfoCode}{selectedFriend.Name.Length:D5}{selectedFriend.Name}");
                    communicator.SendData($"{userProjects}{selectedFriend.Name.Length:D5}{selectedFriend.Name}");
                }
            }
        }

        private void LstProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!inMessagesMode || !inCreate || !inSettings)
            {
                e.Handled = true;
                var selectedProject = lstProjects.SelectedItem as ProjectInfo;
                if (selectedProject != null)
                {
                    string joinProjectCode = ((int)MessageCodes.MC_ENTER_PROJECT_REQUEST).ToString();
                    communicator.SendData($"{joinProjectCode}{selectedProject.ProjectId.ToString().Length:D5}{selectedProject.ProjectId}");
                }
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
            var selectedProject = lstProjects.SelectedItem as ProjectInfo;
            if (selectedProject != null)
            {
                string code = ((int)MessageCodes.MC_DELETE_PROJECT_REQUEST).ToString();
                communicator.SendData($"{code}{selectedProject.ProjectId.ToString().Length:D5}{selectedProject.ProjectId}");
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

                displayedUserProfile = new UserProfile { UserName = userNameResp, Email = email, Bio = bio };

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
            AddProjectBtn.Visibility = Visibility.Visible;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            string code = ((int)MessageCodes.MC_SETTINGS_REQUEST).ToString();
            communicator.SendData($"{code}");
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
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
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
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
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
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                User user = button.CommandParameter as User;
                if (user != null)
                {
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

        private void SortProjectsList(ObservableCollection<ProjectInfo> projects)
        {
            var sortedProject = new ObservableCollection<ProjectInfo>(projects.OrderBy(u => u.ProjectName));
            lstProjects.ItemsSource = sortedProject;
        }

        private Status ParseStatus(string statusCode)
        {
            return statusCode switch
            {
                "0" => Status.Offline_defualt,
                "1" => Status.Online_defualt,
                "2" => Status.search_defualt,
                "3" => Status.search_defualt,
                "4" => Status.search_Light,
                _ => throw new ArgumentException($"Invalid status code: {statusCode}"),
            };
        }

        public class UserProfile : INotifyPropertyChanged
        {
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

        private void LeaveProject_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string addFriendCode = ((int)MessageCodes.MC_LEAVE_PROJECT_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{project.ProjectId.ToString().Length:D5}{project.ProjectId}{displayedUserProfile.UserName.Length:D5}{displayedUserProfile.UserName}");
                }
            }
        }

        private void DeclineInvite_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string addFriendCode = ((int)MessageCodes.MC_DECLINE_PROJECT_INVITE_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{project.ProjectId.ToString().Length:D5}{project.ProjectId}{displayedUserProfile.UserName.Length:D5}{displayedUserProfile.UserName}");

                }
            }
        }

        private void AcceptInvite_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string addFriendCode = ((int)MessageCodes.MC_ACCEPT_PROJECT_INVITE_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{project.ProjectId.ToString().Length:D5}{project.ProjectId}{displayedUserProfile.UserName.Length:D5}{displayedUserProfile.UserName}{project.Role.Length:D5}{project.Role}");

                }
            }
        }

        private void ViewProjectInfo_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string code = ((int)MessageCodes.MC_VIEW_PROJECT_INFO_REQUEST).ToString();
                    string msg = $"{code}{project.ProjectId.ToString().Length:D5}{project.ProjectId}";
                    communicator.SendData(msg);
                }
            }
        }

        private void EditProjectInfo_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string code = ((int)MessageCodes.MC_EDIT_PROJECT_INFO_REQUEST).ToString();
                    communicator.SendData($"{code}{project.ProjectId.ToString().Length:D5}{project.ProjectId}");
                }
            }
        }

        private void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                ProjectInfo project = button.CommandParameter as ProjectInfo;
                if (project != null)
                {
                    string code = ((int)MessageCodes.MC_DELETE_PROJECT_REQUEST).ToString();
                    communicator.SendData($"{code}{project.ProjectId.ToString().Length:D5}{project.ProjectId}");
                }
            }
        }

        private void Messages_Click(object sender, RoutedEventArgs e)
        {
            string code = ((int)MessageCodes.MC_MOVE_TO_MESSAGES_REQUEST).ToString();
            communicator.SendData($"{code}");
        }

        public class User
        {
            private string name;
            private string status;
            private bool isFriend;
            private bool isFriendRequest;

            public string Name
            {
                get => name;
                set
                {
                    name = value;
                    OnPropertyChanged();
                }
            }

            public string Status
            {
                get => status;
                set
                {
                    status = value;
                    OnPropertyChanged();
                }
            }

            public bool IsFriend
            {
                get => isFriend;
                set
                {
                    isFriend = value;
                    OnPropertyChanged();
                }
            }

            public bool IsFriendRequest
            {
                get => isFriendRequest;
                set
                {
                    isFriendRequest = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public ObservableCollection<User> Friends
        {
            get => friends;
            set
            {
                friends = value;
                OnPropertyChanged(); // Notify property changed when the collection reference changes
            }
        }

        public class ProjectInfo
        {
            public string ProjectName { get; set; }
            public string Role { get; set; }
            public int ProjectId { get; set; }
        }

        public ObservableCollection<ProjectInfo> Projects
        {
            get => projects;
            set
            {
                projects = value;
                OnPropertyChanged(); // Notify property changed when the collection reference changes
            }
        }

        public event PropertyChangedEventHandler PropertyChangeda;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public enum Status
        {
            Offline_defualt,
            Online_defualt,
            search_defualt,
            search_Light
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

    public class RoleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string role = value as string;
            return role == "invite" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AccessToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string role = value as string;
            return role == "admin" || role == "creator" || role == "participant" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AdminToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string role = value as string;
            return role == "admin" || role == "creator" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RegularToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string role = value as string;
            return role == "admin" || role == "creator" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }

    public class CreatorToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string role = value as string;
            return role == "creator" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}