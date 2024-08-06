using Microsoft.VisualBasic;
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
    public partial class IssueDataWindow : Window
    {
        public string IssueData { get; set; }
        public ObservableCollection<User> SearchResults { get; set; }
        public ObservableCollection<User> SelectedUsers { get; set; }
        public Issue CurrentIssue { get; set; }
        public List<User> projectUsers { get; set; }
        Communicator communicator;
        public int projectId;
        public int issueId;
        public bool disconnect = true;

        public IssueDataWindow(Communicator communicator, int projectId, int IssueId)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            DataContext = this;
            this.communicator = communicator;
            this.projectId = projectId;
            SearchResults = new ObservableCollection<User>();
            SelectedUsers = new ObservableCollection<User>();

            SearchResultsListBox.ItemsSource = SearchResults;
            SelectedUsersListBox.ItemsSource = SelectedUsers;

            projectUsers = new List<User>();
            this.issueId = IssueId;
            if (issueId != -1)
            {
                string issueCode = ((int)MessageCodes.MC_GET_ISSUE_REQUEST).ToString();
                communicator.SendData($"{issueCode}{issueId}");

                string update = communicator.ReceiveData();

                string issueRepCode = update.Substring(0, 3);
                string issueRep = update.Substring(3);

                if (issueRepCode == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
                {
                    update = communicator.ReceiveData();
                    issueRepCode = update.Substring(0, 3);
                }

                int currentIndex = 0;

                int dataLen = int.Parse(issueRep.Substring(currentIndex, 5));
                currentIndex += 5;
                string data = issueRep.Substring(currentIndex, dataLen);
                currentIndex += dataLen;

                // Parse user assignment
                int userAssignmentLen = int.Parse(issueRep.Substring(currentIndex, 5));
                currentIndex += 5;
                List<User> userAssignments = new List<User>();
                int userIndex = 0;

                while (userIndex < userAssignmentLen)
                {
                    int userNameLen = int.Parse(issueRep.Substring(currentIndex, 5));
                    currentIndex += 5;
                    userIndex += 5;
                    string userName = issueRep.Substring(currentIndex, userNameLen);
                    currentIndex += userNameLen;
                    userIndex += 5;
                    userAssignments.Add(new User
                    {
                        Name = userName,
                    });
                }

                // Parse date
                int dateLen = int.Parse(issueRep.Substring(currentIndex, 5));
                currentIndex += 5;
                string dateString = issueRep.Substring(currentIndex, dateLen);
                currentIndex += dateLen;
                DateTime date = DateTime.Parse(dateString);


                int idLen = int.Parse(issueRep.Substring(currentIndex, 5));
                currentIndex += 5;
                int id = int.Parse(issueRep.Substring(currentIndex, idLen));
                currentIndex += idLen;

                // Create Issue object
                Issue newIssue = new Issue
                {
                    Id = id,
                    Detail = data,
                    Users = userAssignments,
                    Date = date,
                };

                CurrentIssue = newIssue;
                IssueData = newIssue.Detail;
                IssueDateCalendar.SelectedDate = newIssue.Date;
                IssueDateCalendar.DisplayDate = newIssue.Date;

                foreach (var user in newIssue.Users)
                {
                    SelectedUsers.Add(user);
                }
                
                CreateBtn.Visibility = Visibility.Collapsed;
                EditBtn.Visibility = Visibility.Visible;
            }
            else
            {
                CreateBtn.Visibility = Visibility.Visible;
                EditBtn.Visibility = Visibility.Collapsed;
            }

            string projectParticipants = ((int)MessageCodes.MC_GET_PROJECT_PATICIPANTS_REQUEST).ToString();
            communicator.SendData($"{projectParticipants}{projectId}");

            string response = communicator.ReceiveData();

            string projectsRepCode = response.Substring(0, 3);
            string projectsRep = response.Substring(3);

            if (projectsRepCode == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
            {
                response = communicator.ReceiveData();
                projectsRepCode = response.Substring(0, 3);
            }

            if (projectsRepCode == ((int)MessageCodes.MC_GET_PROJECT_PATICIPANTS_RESP).ToString())
            {
                int index = 0;
                while (index < projectsRep.Length)
                {
                    int userNameLen = int.Parse(projectsRep.Substring(index, 5));
                    index += 5;
                    string userName = projectsRep.Substring(index, userNameLen);
                    index += userNameLen;

                    projectUsers.Add(new User
                    {
                        Name = userName,
                    });
                }
            }

            communicator.ApplyTheme(this);
        }

        private void UserSearchTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            var query = UserSearchTextBox.Text.ToLower();
            if (query.Length > 0)
            {
                var filteredUsers = projectUsers
            .           Where(u => !string.IsNullOrEmpty(u.Name) &&
                        u.Name.ToLower().Contains(query) &&
                        u.Name.ToLower() != communicator.UserName.ToLower()).ToList();

                Dispatcher.Invoke(() =>
                {
                    SearchResults.Clear();
                    borderSerachBlock.Visibility = filteredUsers.Any() ? Visibility.Visible : Visibility.Hidden;

                    foreach (var user in filteredUsers)
                    {
                        SearchResults.Add(new User
                        {
                            Name = user.Name,
                        });
                    }
                });
            }
            else
            {
                SearchResults.Clear();
                borderSerachBlock.Visibility = Visibility.Hidden;
            }
        }

        private void SearchResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SearchResultsListBox.SelectedItem is User selectedUser && !SelectedUsers.Any(u => u.Name == selectedUser.Name))
            {
                SelectedUsers.Add(new User
                {
                    Name = selectedUser.Name,
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

        private void CreateBtn_Click(object sender, RoutedEventArgs e)
        {
            // Create logic here
            // Validate data and close the window
            if (ValidateData())
            {
                string issueData = IssueDataTextBox.Text;
                DateTime? selectedDate = IssueDateCalendar.SelectedDate;
                string date = selectedDate.Value.ToString("MM/dd/yyyy");

                string selectedUsersData = "";
                foreach (var user in SelectedUsers)
                {
                    selectedUsersData += $"{user.Name.Length:D5}{user.Name}";
                }

                string createIssueCode = ((int)MessageCodes.MC_ADD_TASK_REQUEST).ToString();

                communicator.SendData($"{createIssueCode}{issueData.Length:D5}{issueData}{selectedUsersData.Length:D5}" +
                    $"{selectedUsersData}{date.Length:D5}{date}");

                string update = communicator.ReceiveData();
                string code = update.Substring(0, 3);

                if (code == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
                {
                    update = communicator.ReceiveData();
                    code = update.Substring(0, 3);
                }

                if (code == ((int)MessageCodes.MC_BACK_TO_TO_DO_LIST_PAGE_RESP).ToString())
                {
                    Dispatcher.Invoke(() =>
                    {
                        Close();
                    });
                }
                else if (code == ((int)MessageCodes.MC_ERROR_RESP).ToString())
                {
                    HandleError(update);
                }
            }
            else
            {
                ErrorTextBlock.Text = "Please complete all required fields.";
                ErrorTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            // Edit logic here
            // Validate data and close the window
            if (ValidateData())
            {
                string issueData = IssueDataTextBox.Text;
                DateTime? selectedDate = IssueDateCalendar.SelectedDate; 
                string date = selectedDate.Value.ToString("MM/dd/yyyy");

                string selectedUsersData = "";
                foreach (var user in SelectedUsers)
                {
                    selectedUsersData += $"{user.Name.Length:D5}{user.Name}";
                }

                string createIssueCode = ((int)MessageCodes.MC_MODIFY_ISSUE_REQUEST).ToString();

                communicator.SendData($"{createIssueCode}{issueData.Length:D5}{issueData}{selectedUsersData.Length:D5}" +
                    $"{selectedUsersData}{date.Length:D5}{date}{issueId}");

                string update = communicator.ReceiveData();
                string code = update.Substring(0, 3);

                if (code == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
                {
                    update = communicator.ReceiveData();
                    code = update.Substring(0, 3);
                }

                if (code == ((int)MessageCodes.MC_BACK_TO_TO_DO_LIST_PAGE_RESP).ToString())
                {
                    Dispatcher.Invoke(() =>
                    {
                        Close();
                    });
                }
                else if (code == ((int)MessageCodes.MC_ERROR_RESP).ToString())
                {
                    HandleError(update);
                }
            }
            else
            {
                ErrorTextBlock.Text = "Please complete all required fields.";
                ErrorTextBlock.Visibility = Visibility.Visible;
            }
        }

        private bool ValidateData()
        {
            // Implement your validation logic here
            return !string.IsNullOrEmpty(IssueDataTextBox.Text) && IssueDateCalendar.SelectedDate != null;
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
    }

    public class User : INotifyPropertyChanged
    {
        private string _name;

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

