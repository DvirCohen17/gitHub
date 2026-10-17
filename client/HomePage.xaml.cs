using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace client_side
{
    // Home: your profile, your projects, friends and people search. Also shows
    // other users' profiles. Create-project, Messages and Settings open on top of
    // it; this window keeps the only socket listener and forwards their replies.
    public partial class HomePage : Window
    {
        private readonly Communicator communicator;
        private Thread receiveServerUpdatesThread;
        private volatile bool isListeningToServer = true;
        private bool disconnect = true;

        public ObservableCollection<ProjectInfo> Projects { get; } = new ObservableCollection<ProjectInfo>();
        public ObservableCollection<Person> People { get; } = new ObservableCollection<Person>();

        private string displayedUser;          // whose profile is shown
        private bool displayedIsFriend;
        private bool inSearch;
        private int messageCount;
        private Window activeChild;            // create project / messages / settings
        private readonly DispatcherTimer searchTimer;

        private bool ViewingSelf => displayedUser == communicator.UserName;

        public HomePage(Communicator communicator)
        {
            InitializeComponent();
            MainFrame.Attach(this); // same size and place as the previous screen
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            displayedUser = communicator.UserName;
            lstProjects.ItemsSource = Projects;
            lstFriends.ItemsSource = People;

            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += (_, _) => { searchTimer.Stop(); RunSearch(); };

            communicator.ApplyTheme(this);
            communicator.ThemeChanged += OnThemeChanged;
            Closing += HomePage_CloseFile;
            Closed += (_, _) => communicator.ThemeChanged -= OnThemeChanged;

            StartListening();
            RefreshAll();
        }

        private void OnThemeChanged(object sender, EventArgs e) => Dispatcher.Invoke(() => communicator.ApplyTheme(this));

        private void StartListening()
        {
            isListeningToServer = true;
            receiveServerUpdatesThread = new Thread(ReceiveServerUpdates) { IsBackground = true };
            receiveServerUpdatesThread.Start();
        }

        private void Send(MessageCodes code, string body = "")
        {
            try
            {
                communicator.SendData($"{(int)code}{body}");
            }
            catch (Exception ex)
            {
                communicator.HandleListenerError(ex);
            }
        }

        private void SetStatus(string text, bool isError = false)
        {
            txtStatus.Text = text;
            txtStatus.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Theme.Danger" : "Theme.Muted");
        }

        private void RefreshAll()
        {
            Send(MessageCodes.MC_PROFILE_INFO_REQUEST, Wire.Field(displayedUser));
            if (ViewingSelf)
            {
                Send(MessageCodes.MC_PROJECTS_LIST_REQUEST, Wire.Field(communicator.UserName));
            }
            else
            {
                Send(MessageCodes.MC_USER_PROJECTS_LIST_REQUEST, Wire.Field(displayedUser));
            }
            if (inSearch)
            {
                RunSearch();
            }
            else
            {
                Send(MessageCodes.MC_FRIENDS_LIST_REQUEST, Wire.Field(communicator.UserName));
            }
            Send(MessageCodes.MC_GET_MSG_COUNT_REQUEST, Wire.Field(communicator.UserName));
        }

        // =====================================================================
        // Server messages
        // =====================================================================

        private void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    string update = communicator.ReceiveData();
                    Dispatcher.Invoke(() => HandleMessage(update));
                }
            }
            catch (Exception ex)
            {
                if (isListeningToServer)
                {
                    communicator.HandleListenerError(ex);
                }
            }
        }

        private void HandleMessage(string update)
        {
            if (update.Length < 3)
            {
                return;
            }
            try
            {
                if (activeChild is IServerMessageHandler child && child.HandleServerMessage(update))
                {
                    return;
                }

                switch (update.Substring(0, 3))
                {
                    case "220": HandleProfile(update); break;                        // profile
                    case "222": HandleMyProjects(update); break;                     // my projects (with role)
                    case "241": HandleUserProjects(update); break;                   // someone else's projects
                    case "221": if (!inSearch) HandlePeople(update, false); break;   // friends
                    case "225": if (inSearch) HandlePeople(update, true); break;     // search results
                    case "253": HandleMessageCount(update); break;
                    case "256": SetMessageCount(messageCount + 1); break;            // new message
                    case "147": case "242":                                          // project list changed / left
                        if (ViewingSelf) Send(MessageCodes.MC_PROJECTS_LIST_REQUEST, Wire.Field(communicator.UserName));
                        break;
                    case "223": case "224": case "236": case "237":                  // friends changed
                    case "401": case "403": case "300":                              // someone logged in / out
                        if (!inSearch) Send(MessageCodes.MC_FRIENDS_LIST_REQUEST, Wire.Field(communicator.UserName));
                        if (update.StartsWith("224")) RefreshAll();
                        break;
                    case "238": SetStatus($"{new WireReader(update).Field()} sent you a friend request - see Messages."); break;
                    case "276": SetStatus($"Friend request sent to {update.Substring(3)}."); addFriendBtn.Visibility = Visibility.Collapsed; break;
                    case "227": SetStatus("Bio saved."); break;
                    case "240": if (ViewingSelf) Send(MessageCodes.MC_PROJECTS_LIST_REQUEST, Wire.Field(communicator.UserName)); break;
                    case "305": HandleEnterProject(update); break;
                    case "229": OpenChild(CreateProjectWindow(update)); break;
                    case "254": ReplaceWith(new MessagesWindow(communicator)); break;
                    case "251": ReplaceWith(new settingsWindow(communicator)); break;
                    case "406": HandleLogout(); break;
                    case "200": SetStatus(update.Substring(3), true); break;
                    default: break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't process a message from the server ({ex.Message}).", true);
            }
        }

        // ---- profile

        private void HandleProfile(string update)
        {
            var reader = new WireReader(update);
            string name = reader.Field();
            string email = reader.Field();
            string bio = reader.Field();
            string relation = reader.Rest(); // "1" = you, "0" = friend, "" = not a friend
            if (name != displayedUser)
            {
                return; // reply for a profile we navigated away from
            }
            displayedIsFriend = relation == "0";

            txtProfileName.Text = name;
            txtAvatar.Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
            txtProfileEmail.Text = email;
            bool self = ViewingSelf;
            Title = self ? "Profile Page" : $"{name} - Profile";
            bioEditPanel.Visibility = self ? Visibility.Visible : Visibility.Collapsed;
            BioTextBlock.Visibility = self ? Visibility.Collapsed : Visibility.Visible;
            if (self)
            {
                if (!BioTextBox.IsKeyboardFocused) BioTextBox.Text = bio;
            }
            else
            {
                BioTextBlock.Text = string.IsNullOrWhiteSpace(bio) ? "(no bio)" : bio;
            }
            backButton.Visibility = self ? Visibility.Collapsed : Visibility.Visible;
            addFriendBtn.Visibility = !self && !displayedIsFriend ? Visibility.Visible : Visibility.Collapsed;
            removeFriendBtn.Visibility = !self && displayedIsFriend ? Visibility.Visible : Visibility.Collapsed;
            AddProjectBtn.Visibility = self ? Visibility.Visible : Visibility.Collapsed;
            txtProjectsTitle.Text = self ? "My projects" : $"{name}'s projects";
        }

        private void ShowProfile(string userName)
        {
            displayedUser = userName;
            RefreshAll();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => ShowProfile(communicator.UserName);

        private void EditBio_Click(object sender, RoutedEventArgs e)
        {
            Send(MessageCodes.MC_EDIT_PROFILE_INFO_REQUEST, Wire.Field(BioTextBox.Text.Trim()));
        }

        // ---- projects

        private void HandleMyProjects(string update)
        {
            if (!ViewingSelf) return;
            var reader = new WireReader(update);
            var list = new List<ProjectInfo>();
            while (!reader.AtEnd)
            {
                string name = reader.Field();
                string role = reader.Field();
                int id = int.Parse(reader.Field());
                list.Add(new ProjectInfo { ProjectName = name, Role = role, ProjectId = id, IsMine = true });
            }
            ShowProjects(list, "You're not in any projects yet.\nCreate one with \"+ New project\", or accept an invite from Messages.");
        }

        private void HandleUserProjects(string update)
        {
            if (ViewingSelf) return;
            var reader = new WireReader(update);
            var list = new List<ProjectInfo>();
            while (!reader.AtEnd)
            {
                string name = reader.Field();
                int id = int.Parse(reader.Field());
                list.Add(new ProjectInfo { ProjectName = name, Role = "", ProjectId = id, IsMine = false });
            }
            ShowProjects(list, $"{displayedUser} isn't in any projects.");
        }

        private void ShowProjects(List<ProjectInfo> list, string emptyText)
        {
            Projects.Clear();
            foreach (var p in list.OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase))
            {
                Projects.Add(p);
            }
            txtNoProjects.Text = emptyText;
            txtNoProjects.Visibility = Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static ProjectInfo ProjectOf(object sender) => (sender as FrameworkElement)?.Tag as ProjectInfo;

        private void OpenProject(ProjectInfo project)
        {
            if (project != null)
            {
                SetStatus($"Opening {project.ProjectName}...");
                Send(MessageCodes.MC_ENTER_PROJECT_REQUEST, Wire.Field(project.ProjectId.ToString()));
            }
        }

        // Use the item under the mouse (the selection can be momentarily empty while the list refreshes).
        private static T ItemUnderMouse<T>(MouseButtonEventArgs e) where T : class =>
            (e.OriginalSource as FrameworkElement)?.DataContext as T;

        private void LstProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null)
            {
                return; // double-clicking one of the row's buttons
            }
            OpenProject(ItemUnderMouse<ProjectInfo>(e) ?? lstProjects.SelectedItem as ProjectInfo);
        }

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null && element is not T)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return element as T;
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenProject(ProjectOf(sender));

        private void EditProjectInfo_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectOf(sender) is ProjectInfo p)
                Send(MessageCodes.MC_EDIT_PROJECT_INFO_REQUEST, Wire.Field(p.ProjectId.ToString()));
        }

        private void ViewProjectInfo_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectOf(sender) is ProjectInfo p)
                Send(MessageCodes.MC_VIEW_PROJECT_INFO_REQUEST, Wire.Field(p.ProjectId.ToString()));
        }

        private void LeaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectOf(sender) is ProjectInfo p && Confirm($"Leave \"{p.ProjectName}\"? You'll need a new invite to rejoin.", "Leave project"))
            {
                Send(MessageCodes.MC_LEAVE_PROJECT_REQUEST, Wire.Field(p.ProjectId.ToString()) + Wire.Field(communicator.UserName));
            }
        }

        private void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectOf(sender) is ProjectInfo p &&
                Confirm($"Delete \"{p.ProjectName}\" with all its files, chat and tasks for everyone? This can't be undone.", "Delete project"))
            {
                Send(MessageCodes.MC_DELETE_PROJECT_REQUEST, Wire.Field(p.ProjectId.ToString()));
            }
        }

        private bool Confirm(string text, string title) =>
            MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private void AddProject_Click(object sender, RoutedEventArgs e) => Send(MessageCodes.MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST);

        private void HandleEnterProject(string update)
        {
            var reader = new WireReader(update);
            string projectName = reader.Field();
            int projectId = int.Parse(reader.Field());
            string codeLan = reader.Field();
            bool isEditable = reader.Rest() == "true";

            ReplaceWith(new ProjectDirectory(communicator, projectName, projectId, codeLan, isEditable));
        }

        // Another main screen takes over this window's place and the connection.
        private void ReplaceWith(Window next)
        {
            disconnect = false;
            isListeningToServer = false;
            next.Show();
            Close();
        }

        // "229<name><id><mode>" (create: "00000create")
        private Window CreateProjectWindow(string update)
        {
            var reader = new WireReader(update);
            string name = reader.Field();
            int id = -1;
            string mode = "create";
            if (name.Length > 0)
            {
                id = int.Parse(reader.Field());
                mode = reader.Rest();
            }
            return new AddProjectWindow(communicator, mode, name, id);
        }

        // ---- people

        private void HandlePeople(string update, bool isSearch)
        {
            var reader = new WireReader(update);
            var list = new List<Person>();
            while (!reader.AtEnd)
            {
                string name = reader.Field();
                char status = reader.Take(1)[0];
                list.Add(new Person { Name = name, Status = status });
            }
            People.Clear();
            foreach (var person in list.OrderByDescending(p => p.Status == '1').ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                People.Add(person);
            }
            txtPeopleTitle.Text = isSearch ? "Search results" : $"Friends ({list.Count(p => p.Status == '1')} online)";
            txtNoPeople.Text = isSearch ? "Nobody found. Search matches the start of a name."
                                        : "No friends yet. Search for people above and add them.";
            txtNoPeople.Visibility = People.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchPlaceholder.Visibility = searchBarTextBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            searchTimer.Stop();
            if (searchBarTextBox.Text.Trim().Length == 0)
            {
                ClearSearch();
            }
            else
            {
                searchTimer.Start(); // search once typing pauses, not on every key
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { searchTimer.Stop(); RunSearch(); }
            else if (e.Key == Key.Escape) { searchBarTextBox.Clear(); }
        }

        private void RunSearch()
        {
            string query = searchBarTextBox.Text.Trim();
            if (query.Length == 0)
            {
                return;
            }
            inSearch = true;
            closeSerachBtn.Visibility = Visibility.Visible;
            Send(MessageCodes.MC_SEARCH_REQUEST, Wire.Field(query));
        }

        private void closeSearch_Click(object sender, RoutedEventArgs e) => searchBarTextBox.Clear();

        private void ClearSearch()
        {
            if (!inSearch) return;
            inSearch = false;
            closeSerachBtn.Visibility = Visibility.Collapsed;
            Send(MessageCodes.MC_FRIENDS_LIST_REQUEST, Wire.Field(communicator.UserName));
        }

        private void LstFriends_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((ItemUnderMouse<Person>(e) ?? lstFriends.SelectedItem as Person) is Person person)
            {
                ShowProfile(person.Name);
            }
        }

        private void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            Send(MessageCodes.MC_ADD_FRIEND_REQUEST, Wire.Field(displayedUser));
        }

        private void RemoveDisplayedFriend_Click(object sender, RoutedEventArgs e)
        {
            if (Confirm($"Remove {displayedUser} from your friends?\n\nThis also removes {displayedUser} from your projects, and you from theirs.", "Remove friend"))
            {
                Send(MessageCodes.MC_REMOVE_FRIEND_REQUEST, Wire.Field(communicator.UserName) + Wire.Field(displayedUser));
            }
        }

        // ---- messages count, child windows, logout

        private void HandleMessageCount(string update)
        {
            if (update.Length == 8 && int.TryParse(update.Substring(3), out int count))
            {
                SetMessageCount(count);
            }
        }

        private void SetMessageCount(int count)
        {
            messageCount = count;
            msgBtn.Content = count > 0 ? $"Messages ({count})" : "Messages";
            msgBtn.FontWeight = count > 0 ? FontWeights.Bold : FontWeights.Normal;
        }

        private void Messages_Click(object sender, RoutedEventArgs e) => Send(MessageCodes.MC_MOVE_TO_MESSAGES_REQUEST);

        private void Settings_Click(object sender, RoutedEventArgs e) => Send(MessageCodes.MC_SETTINGS_REQUEST);

        private void OpenChild(Window child)
        {
            activeChild = child;
            child.Owner = this;
            IsEnabled = false;
            child.Closed += (_, _) =>
            {
                activeChild = null;
                IsEnabled = true;
                Activate();
                communicator.ApplyTheme(this);
                RefreshAll();
            };
            child.Show();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e) => Send(MessageCodes.MC_LOGOUT_REQUEST);

        private void HandleLogout()
        {
            disconnect = false;
            isListeningToServer = false;
            communicator.ClearCredentials();
            new LoginWindow(communicator).Show();
            Close();
        }

        private void HomePage_CloseFile(object sender, EventArgs e)
        {
            isListeningToServer = false;
            if (disconnect)
            {
                Send(MessageCodes.MC_DISCONNECT, $"{communicator.UserId:D5}");
            }
        }

        // ---- view models

        public class ProjectInfo
        {
            public string ProjectName { get; set; }
            public string Role { get; set; }
            public int ProjectId { get; set; }
            public bool IsMine { get; set; }

            public bool CanEdit => IsMine && (Role == "creator" || Role == "admin");
            public bool CanViewInfo => !CanEdit;
            public bool CanLeave => IsMine && Role != "creator";
            public bool CanDelete => IsMine && Role == "creator";
            public string RoleLabel => IsMine ? (string.IsNullOrEmpty(Role) ? "member" : Role) : "view only";
        }

        public class Person
        {
            public string Name { get; set; }
            public char Status { get; set; } // '1' online friend, '0' offline friend, '2' not a friend
            public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

            public string StatusLabel => Status switch { '1' => "online", '0' => "offline", _ => "" };
            public Brush StatusBrush => Status switch { '1' => Brushes.LimeGreen, '0' => Brushes.Gray, _ => Brushes.Transparent };
        }
    }
}
