using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Input;
using static client_side.HomePage;
using System.Threading;
using System.Threading.Tasks;
using static client_side.ProjectDirectory;
using System.Windows.Threading;
using System.Linq;

namespace client_side
{
    public partial class ToDoListWindow : Window
    {
        private ObservableCollection<Issue> issues;
        private bool showCompletedIssues = false;
        private const double PanelWidth = 200;
        private bool isPanelOpen = true;
        Communicator communicator;
        private bool disconnect = true;
        private bool inViewMode = false;
        private bool isListeningToServer = true;

        private int projectId;

        private Thread receiveServerUpdatesThread;

        public ObservableCollection<Issue> Issues
        {
            get => issues;
            set
            {
                issues = value;
                OnPropertyChanged(nameof(Issues));
            }
        }

        public ToDoListWindow(Communicator communicator, int projectId)
        {
            InitializeComponent();
            DataContext = this;
            Issues = new ObservableCollection<Issue>();
            IssueListView.ItemsSource = Issues;
            IssueListView.MouseDoubleClick += LstIssues_MouseDoubleClick;

            this.communicator = communicator;
            this.projectId = projectId;

            communicator.ApplyTheme(this);
            EnableAllButtonsAndLists();

            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();
            LoadIssues(0);

            Closing += ToDoList_CloseFile; // Hook up the closing event handler
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
            RefreshBtn.IsEnabled = isEnabled;
            AddIssueBtn.IsEnabled = isEnabled;
            ToggleMenuButton.IsEnabled = isEnabled;
            CurrentIsueBtn.IsEnabled = isEnabled;
            CompletedIsueBtn.IsEnabled = isEnabled;
            ExitBtn.IsEnabled = isEnabled;

            foreach (var item in IssueListView.Items)
            {
                ListViewItem container = IssueListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                if (container != null)
                {
                    SetButtonsInContainerEnabledState(container, isEnabled);
                }
            }
        }

        private void EnableDoubleClickEvents()
        {
            IssueListView.MouseDoubleClick += LstIssues_MouseDoubleClick;
        }

        private void DisableDoubleClickEvents()
        {
            IssueListView.MouseDoubleClick -= LstIssues_MouseDoubleClick;
        }

        private void SetButtonsInContainerEnabledState(DependencyObject container, bool isEnabled)
        {
            foreach (var child in GetChildren(container))
            {
                if (child is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = isEnabled;
                }
                else if (child is System.Windows.Controls.CheckBox checkBox)
                {
                    checkBox.IsEnabled = isEnabled;
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
                        case "265":
                            HandleMoveToIssueDataWindow(update);
                            break;
                        case "261":
                            HandleDeleteIssue(update);
                            break;
                        case "260" or "259":
                            HandleDeleteIssue(update);
                            break;
                        case "262":
                            HandleLoadIssues(update, 0);
                            break;
                        case "263":
                            HandleLoadIssues(update, 1);
                            break;
                        case "269":
                            HandleMoveToProjectWindow(update);
                            break;
                        case "258" or "271":
                            HandleReLoadIssues();
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
                MessageBox.Show($"Error receiving server updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                string msg = await Task.Run(() => communicator.ReceiveData());
            }
        }

        private void SortIssues(ObservableCollection<Issue> issueList)
        {
            var sortedIssues = new ObservableCollection<Issue>(issueList.OrderBy(issue => issue.Id).ToList());
            IssueListView.ItemsSource = sortedIssues;
        }

        private void HandleMoveToIssueDataWindow(string update)
        {
            isListeningToServer = false;
            inViewMode = true;

            int issueId = int.Parse(update.Substring(3));

            Dispatcher.Invoke(() =>
            {
                DisableAllButtonsAndLists();
                IssueDataWindow issueDataWindow = new IssueDataWindow(communicator, projectId, issueId);
                issueDataWindow.Closed += IssueWindow_Closed; // Attach the event handler
                issueDataWindow.Show();
            });
        }

        private void HandleDeleteIssue(string update)
        {
            int deletedIssueId = int.Parse(update.Substring(3));

            Dispatcher.Invoke(() =>
            {
                if (IssueListView.ItemsSource is ObservableCollection<Issue> issueList)
                {
                    // Find the index of the issue to delete
                    int deleteIndex = issueList.ToList().FindIndex(issue => issue.Id == deletedIssueId);

                    // If the issue is found, remove it from the list
                    if (deleteIndex != -1)
                    {
                        issueList.RemoveAt(deleteIndex);

                        SortIssues(issueList);

                        // Set the ListBox's ItemsSource again to trigger the update
                        IssueListView.ItemsSource = null;
                        IssueListView.ItemsSource = issueList;
                    }
                }
            });
        }

        private void HandleMoveToProjectWindow(string update)
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

        private void HandleLoadIssues(string update, int mode)
        {
            int currentIndex = 3;

            Dispatcher.Invoke(() =>
            {
                Issues.Clear(); // Clear existing issues
            });
            while (currentIndex < update.Length)
            {
                // Parse data length and data
                int dataLen = int.Parse(update.Substring(currentIndex, 5));
                currentIndex += 5;
                string data = update.Substring(currentIndex, dataLen);
                currentIndex += dataLen;

                // Parse user assignment
                int userAssignmentLen = int.Parse(update.Substring(currentIndex, 5));
                currentIndex += 5;
                List<User> userAssignments = new List<User>();
                int userIndex = 0;

                while (userIndex < userAssignmentLen)
                {
                    int userNameLen = int.Parse(update.Substring(currentIndex, 5));
                    currentIndex += 5;
                    userIndex += 5;
                    string userName = update.Substring(currentIndex, userNameLen);
                    currentIndex += userNameLen;
                    userIndex += 5;
                    userAssignments.Add(new User
                    {
                        Name = userName,
                    });
                }

                // Parse date
                int dateLen = int.Parse(update.Substring(currentIndex, 5));
                currentIndex += 5;
                string dateString = update.Substring(currentIndex, dateLen);
                currentIndex += dateLen;
                DateTime date = DateTime.Parse(dateString);


                int idLen = int.Parse(update.Substring(currentIndex, 5));
                currentIndex += 5;
                int id = int.Parse(update.Substring(currentIndex, idLen));
                currentIndex += idLen;

                if(dataLen > 15)
                {
                    data = data.Substring(0, 12) + "...";
                }

                // Create Issue object
                Issue newIssue = new Issue
                {
                    Id = id,
                    Detail = data,
                    Users = userAssignments,
                    Date = date,
                    IsCompleted = mode == 1 ? true : false,  // Assuming new issues are not completed
                };

                Dispatcher.Invoke(() =>
                {
                    Issues.Add(newIssue);
                });
            }

            Dispatcher.Invoke(() =>
            {
                SortIssues(Issues);
            });
        }

        private void LstIssues_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!inViewMode)
            {
                e.Handled = true;
                var selectedFriend = IssueListView.SelectedItem as Issue;
                if (selectedFriend != null && selectedFriend.IsCompleted == false)
                {
                    string joinProjectCode = ((int)MessageCodes.MC_MOVE_TO_ISSUE_DATA_WINDOW_REQUEST).ToString();
                    communicator.SendData($"{joinProjectCode}{selectedFriend.Id}");
                }
            }
        }

        private void IssueWindow_Closed(object sender, EventArgs e)
        {
            EnableAllButtonsAndLists();
            isListeningToServer = true;
            inViewMode = false;

            receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
            {
                IsBackground = true
            };
            receiveServerUpdatesThread.Start();
            LoadIssues(0);

        }


        private async void ToDoList_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();

                    string fullMessage = $"{chatMessageCode}{communicator.UserId:D5}";

                    //communicator.SendData(fullMessage);

                    // Close the window on the UI thread
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CurrentIssues_Click(object sender, RoutedEventArgs e)
        {
            showCompletedIssues = false;
            string code = ((int)MessageCodes.MC_GET_CURRENT_PROJECT_ISSUES_REQUEST).ToString();
            communicator.SendData($"{code}{projectId}");
        }

        private void CompletedIssues_Click(object sender, RoutedEventArgs e)
        {
            showCompletedIssues = true;
            string code = ((int)MessageCodes.MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST).ToString();
            communicator.SendData($"{code}{projectId}");
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is Issue issue)
            {
                issue.IsCompleted = true;
                string code = ((int)MessageCodes.MC_MARK_TASK_AS_COMPLETED_REQUEST).ToString();
                communicator.SendData($"{code}{issue.Id}");
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is Issue issue)
            {
                issue.IsCompleted = false;

                string code = ((int)MessageCodes.MC_MARK_TASK_AS_NOT_COMPLETED_REQUEST).ToString();
                communicator.SendData($"{code}{issue.Id}");

            }
        }

        private void LoadIssues(int mode)
        {
            if (!showCompletedIssues)
            {
                string code = ((int)MessageCodes.MC_GET_CURRENT_PROJECT_ISSUES_REQUEST).ToString();
                communicator.SendData($"{code}{projectId}");
            }
            else
            {
                string code = ((int)MessageCodes.MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST).ToString();
                communicator.SendData($"{code}{projectId}");
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadIssues(showCompletedIssues ? 1 : 0);
        }

        private void HandleReLoadIssues()
        {
            if (!showCompletedIssues)
            {
                LoadIssues(0);
            }
        }

        private void DeleteIssue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                if (button.Tag is Issue IssueToRemove)
                {
                    string code = ((int)MessageCodes.MC_DELETE_TASK_REQUEST).ToString();
                    communicator.SendData($"{code}{IssueToRemove.Id}");
                }
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            string joinProjectCode = ((int)MessageCodes.MC_MOVE_TO_ISSUE_DATA_WINDOW_REQUEST).ToString();
            communicator.SendData($"{joinProjectCode}-1");
        }

        private void ToggleMenu_Click(object sender, RoutedEventArgs e)
        {
            var panelTransform = SidePanelTransform;
            var slideAnimation = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                FillBehavior = FillBehavior.HoldEnd
            };

            if (isPanelOpen)
            {
                slideAnimation.From = 0;
                slideAnimation.To = -PanelWidth;
                slideAnimation.Completed += (s, a) =>
                {
                    SidePanel.Width = 0;
                    isPanelOpen = false;
                    ToggleMenuButton.Content = "☰";
                };
            }
            else
            {
                SidePanel.Width = PanelWidth;
                slideAnimation.From = -PanelWidth;
                slideAnimation.To = 0;
                slideAnimation.Completed += (s, a) => isPanelOpen = true;
                ToggleMenuButton.Content = "✕";
            }

            panelTransform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
        }
        
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            string chatMessageCode = ((int)MessageCodes.MC_MOVE_TO_PROJECT_PAGE_REQUEST).ToString();

            string fullMessage = $"{chatMessageCode}{projectId}";
            communicator.SendData(fullMessage);
        }

        private void OnPropertyChanged(string propertyName)
        {
            // Implementation for property changed notifications
        }
    }

    public class Issue
    {
        public string Detail { get; set; }
        public List<User> Users { get; set; }
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public bool IsCompleted { get; set; }
    }
}
