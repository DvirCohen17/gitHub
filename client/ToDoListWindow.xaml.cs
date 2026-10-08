using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace client_side
{
    // A project's tasks. The window keeps listening while a task is being edited
    // (the editor is a dialog), so changes made by teammates show up right away.
    public partial class ToDoListWindow : Window
    {
        private readonly Communicator communicator;
        private readonly int projectId;
        private Thread receiveServerUpdatesThread;
        private volatile bool isListeningToServer = true;
        private bool disconnect = true;
        private bool showCompletedIssues;
        private readonly List<string> members = new List<string>();

        public ObservableCollection<Issue> Issues { get; } = new ObservableCollection<Issue>();

        public ToDoListWindow(Communicator communicator, int projectId)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            this.projectId = projectId;
            IssueListView.ItemsSource = Issues;

            communicator.ApplyTheme(this);
            Closing += ToDoList_CloseFile;

            receiveServerUpdatesThread = new Thread(ReceiveServerUpdates) { IsBackground = true };
            receiveServerUpdatesThread.Start();

            Send(MessageCodes.MC_GET_PROJECT_PATICIPANTS_REQUEST, projectId.ToString());
            LoadIssues();
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
            txtStatus.Foreground = isError ? Brushes.IndianRed : (communicator.AppTheme?.TextColor ?? Brushes.White);
            txtStatus.Opacity = isError ? 1 : 0.65;
        }

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
                switch (update.Substring(0, 3))
                {
                    case "262": if (!showCompletedIssues) ShowIssues(update, false); break; // open tasks
                    case "263": if (showCompletedIssues) ShowIssues(update, true); break;   // done tasks
                    case "264": HandleMembers(update); break;
                    case "258": case "271": case "270": LoadIssues(); break;                 // added / changed / saved
                    case "259": case "260": case "261": LoadIssues(); break;                 // done / reopened / deleted
                    case "269": HandleMoveToProjectWindow(update); break;
                    case "200": SetStatus(update.Substring(3), true); break;
                    default: break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't process a message from the server ({ex.Message}).", true);
            }
        }

        private void HandleMembers(string update)
        {
            var reader = new WireReader(update);
            members.Clear();
            while (!reader.AtEnd)
            {
                members.Add(reader.Field());
            }
        }

        private void ShowIssues(string update, bool completed)
        {
            var reader = new WireReader(update);
            var loaded = new List<Issue>();
            while (!reader.AtEnd)
            {
                loaded.Add(Issue.Read(reader, completed));
            }
            Issues.Clear();
            foreach (var issue in loaded.OrderBy(i => i.Date).ThenBy(i => i.Id))
            {
                Issues.Add(issue);
            }
            txtEmpty.Text = completed ? "No finished tasks yet." : "No open tasks. Use \"+ New task\" to add one.";
            txtEmpty.Visibility = Issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetStatus($"{Issues.Count} {(completed ? "finished" : "open")} task{(Issues.Count == 1 ? "" : "s")}.");
        }

        private void LoadIssues()
        {
            Send(showCompletedIssues ? MessageCodes.MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST : MessageCodes.MC_GET_CURRENT_PROJECT_ISSUES_REQUEST,
                 projectId.ToString());
        }

        private void CurrentIssues_Click(object sender, RoutedEventArgs e)
        {
            if (communicator == null) return; // fired during InitializeComponent
            showCompletedIssues = false;
            LoadIssues();
        }

        private void CompletedIssues_Click(object sender, RoutedEventArgs e)
        {
            showCompletedIssues = true;
            LoadIssues();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadIssues();

        // Click (not Checked/Unchecked): only react to the user, not to the list being filled in.
        private void CompletedCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.Tag is Issue issue)
            {
                bool done = box.IsChecked == true;
                Send(done ? MessageCodes.MC_MARK_TASK_AS_COMPLETED_REQUEST : MessageCodes.MC_MARK_TASK_AS_NOT_COMPLETED_REQUEST, issue.Id.ToString());
                SetStatus(done ? "Moved to Done." : "Moved back to Open.");
            }
        }

        private void DeleteIssue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Issue issue &&
                MessageBox.Show(this, $"Delete this task?\n\n{issue.Detail}", "Delete task",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Send(MessageCodes.MC_DELETE_TASK_REQUEST, issue.Id.ToString());
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new IssueDataWindow(this, members, null);
            if (dialog.ShowDialog() == true)
            {
                Send(MessageCodes.MC_ADD_TASK_REQUEST, Wire.Field(dialog.Detail) + Wire.Field(dialog.EncodedAssignees) + Wire.Field(dialog.DueDate));
                SetStatus("Saving...");
            }
        }

        private void EditIssue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Issue issue)
            {
                Edit(issue);
            }
        }

        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.Tag is Issue issue && issue.CanEdit)
            {
                Edit(issue);
            }
        }

        private void Edit(Issue issue)
        {
            var dialog = new IssueDataWindow(this, members, issue);
            if (dialog.ShowDialog() == true)
            {
                Send(MessageCodes.MC_MODIFY_ISSUE_REQUEST,
                     Wire.Field(dialog.Detail) + Wire.Field(dialog.EncodedAssignees) + Wire.Field(dialog.DueDate) + issue.Id);
                SetStatus("Saving...");
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Send(MessageCodes.MC_MOVE_TO_PROJECT_PAGE_REQUEST, projectId.ToString());
        }

        private void HandleMoveToProjectWindow(string update)
        {
            var reader = new WireReader(update);
            string projectName = reader.Field();
            int id = int.Parse(reader.Field());
            string codeLan = reader.Field();
            bool isEditable = reader.Rest() == "true";

            disconnect = false;
            isListeningToServer = false;
            new ProjectDirectory(communicator, projectName, id, codeLan, isEditable).Show();
            Close();
        }

        private void ToDoList_CloseFile(object sender, EventArgs e)
        {
            isListeningToServer = false;
            if (disconnect)
            {
                Send(MessageCodes.MC_DISCONNECT, $"{communicator.UserId:D5}");
            }
        }
    }

    public class Issue
    {
        public int Id { get; set; }
        public string Detail { get; set; }
        public List<User> Users { get; set; } = new List<User>();
        public DateTime Date { get; set; }
        public bool IsCompleted { get; set; }

        public bool CanEdit => !IsCompleted;
        public string AssigneesLabel => Users.Count == 0 ? "Not assigned" : "Assigned: " + string.Join(", ", Users.Select(u => u.Name));
        public bool IsOverdue => !IsCompleted && Date.Date < DateTime.Today;
        public string DueLabel => (IsOverdue ? "Overdue - was due " : "Due ") + Date.ToString("ddd, d MMM yyyy", CultureInfo.CurrentCulture);
        public Brush DueBrush => IsOverdue ? Brushes.IndianRed : Brushes.Gray;

        // Dates are sent as yyyy-MM-dd; older tasks were stored as MM/dd/yyyy.
        public const string DateFormat = "yyyy-MM-dd";

        public static DateTime ParseDate(string text)
        {
            if (DateTime.TryParseExact(text, new[] { DateFormat, "MM/dd/yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : DateTime.Today;
        }

        public static Issue Read(WireReader reader, bool completed)
        {
            string detail = reader.Field();
            var assignees = new WireReader(reader.Field(), 0);
            var users = new List<User>();
            while (!assignees.AtEnd)
            {
                users.Add(new User { Name = assignees.Field() });
            }
            DateTime date = ParseDate(reader.Field());
            int id = int.Parse(reader.Field());
            return new Issue { Id = id, Detail = detail, Users = users, Date = date, IsCompleted = completed };
        }
    }
}
