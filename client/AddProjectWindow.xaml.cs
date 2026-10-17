using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace client_side
{
    // Create a project, or edit / view an existing one's name and members.
    // mode: "create", "creator" or "editor" (may edit), "viewer" (read-only).
    // Server replies arrive through the home page's listener (IServerMessageHandler).
    public partial class AddProjectWindow : Window, IServerMessageHandler
    {
        private readonly Communicator communicator;
        private readonly string mode;
        private readonly int projectId;
        private readonly string originalName;
        private bool awaitingInfo, awaitingFriends, awaitingSave;
        private readonly List<string> friends = new List<string>();

        public ObservableCollection<Member> Members { get; } = new ObservableCollection<Member>();

        private bool IsCreate => mode == "create";
        private bool CanEdit => mode == "create" || mode == "creator" || mode == "editor";

        public AddProjectWindow(Communicator communicator, string mode, string projectName, int projectId)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            this.mode = mode;
            this.projectId = projectId;
            originalName = projectName;
            SelectedUsersListBox.ItemsSource = Members;
            Members.CollectionChanged += (_, _) => UpdateMembersUi();

            Title = IsCreate ? "New project" : CanEdit ? $"Edit {projectName}" : projectName;
            txtHeading.Text = IsCreate ? "New project" : CanEdit ? "Edit project" : "Project info";
            craeteBtn.Content = IsCreate ? "Create" : "Save";
            craeteBtn.Visibility = CanEdit ? Visibility.Visible : Visibility.Collapsed;
            cancelBtn.Content = CanEdit ? "Cancel" : "Close";
            ProjectNameTextBox.Text = projectName;
            ProjectNameTextBox.IsReadOnly = !CanEdit;
            CodeLanguageComboBox.IsEnabled = IsCreate; // a project's language is fixed once created
            CodeLanguageComboBox.SelectedIndex = 0;
            addMemberPanel.Visibility = CanEdit ? Visibility.Visible : Visibility.Collapsed;
            txtMembersHint.Visibility = CanEdit ? Visibility.Visible : Visibility.Collapsed;

            communicator.ApplyTheme(this);
            Loaded += (_, _) => { if (CanEdit) ProjectNameTextBox.Focus(); };

            if (!IsCreate)
            {
                awaitingInfo = true;
                Send(MessageCodes.MC_GET_PROJECT_INFO_REQUEST, Wire.Field(projectName));
            }
            if (CanEdit)
            {
                awaitingFriends = true;
                Send(MessageCodes.MC_FRIENDS_LIST_REQUEST, Wire.Field(communicator.UserName));
            }
            UpdateMembersUi();
        }

        private void Send(MessageCodes code, string body)
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

        public bool HandleServerMessage(string update)
        {
            switch (update.Substring(0, 3))
            {
                case "248" when awaitingInfo:
                    awaitingInfo = false;
                    ShowProjectInfo(update);
                    return true;
                case "221" when awaitingFriends:
                    awaitingFriends = false;
                    ShowFriends(update);
                    return true;
                case "240" when awaitingSave:            // saved
                    awaitingSave = false;
                    Close();
                    return true;
                case "200" when awaitingSave || awaitingInfo:
                    awaitingSave = awaitingInfo = false;
                    ErrorTextBlock.Text = update.Substring(3);
                    craeteBtn.IsEnabled = true;
                    return true;
                default:
                    return false;
            }
        }

        // "248<name><members: name+roleDigit ...><language><private flag>"
        private void ShowProjectInfo(string update)
        {
            var reader = new WireReader(update);
            ProjectNameTextBox.Text = reader.Field();
            var members = new WireReader(reader.Field(), 0);
            string language = reader.Field();

            Members.Clear();
            while (!members.AtEnd)
            {
                string name = members.Field();
                string code = members.Take(1);
                string role = code switch { "2" => "creator", "1" or "4" => "admin", _ => "participant" };
                bool invited = code == "3" || code == "4"; // invite not accepted yet
                Members.Add(new Member { Name = name, Role = role, CanChange = CanEdit && role != "creator", Invited = invited });
            }
            var item = CodeLanguageComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == language);
            if (item != null) CodeLanguageComboBox.SelectedItem = item;
            RefreshFriendChoices();
        }

        private void ShowFriends(string update)
        {
            var reader = new WireReader(update);
            friends.Clear();
            while (!reader.AtEnd)
            {
                friends.Add(reader.Field());
                reader.Take(1); // online flag
            }
            RefreshFriendChoices();
        }

        private void RefreshFriendChoices()
        {
            var available = friends.Where(f => Members.All(m => m.Name != f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            FriendsComboBox.ItemsSource = available;
            FriendsComboBox.SelectedIndex = available.Count > 0 ? 0 : -1;
            FriendsComboBox.IsEnabled = available.Count > 0;
            FriendsComboBox.ToolTip = available.Count > 0 ? "Only friends can be invited" : "No more friends to invite";
        }

        private void UpdateMembersUi()
        {
            txtNoMembers.Visibility = Members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddMember_Click(object sender, RoutedEventArgs e)
        {
            if (FriendsComboBox.SelectedItem is string name)
            {
                Members.Add(new Member { Name = name, Role = "participant", CanChange = true });
                RefreshFriendChoices();
            }
        }

        private void RemoveUser_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is Member member && member.CanChange)
            {
                Members.Remove(member);
                RefreshFriendChoices();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = ProjectNameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                ErrorTextBlock.Text = "Give the project a name.";
                return;
            }
            if (CodeLanguageComboBox.SelectedItem is not ComboBoxItem languageItem)
            {
                ErrorTextBlock.Text = "Pick a language.";
                return;
            }
            string language = (string)languageItem.Tag;
            string members = string.Concat(Members.Select(m =>
                Wire.Field(m.Name) + (m.Role == "creator" ? "2" : m.Role == "admin" ? "1" : "0")));

            ErrorTextBlock.Text = "";
            craeteBtn.IsEnabled = false;
            awaitingSave = true;
            if (IsCreate)
            {
                Send(MessageCodes.MC_CREATE_PROJECT_REQUEST, Wire.Field(name) + Wire.Field(members) + Wire.Field(language) + "False");
            }
            else
            {
                Send(MessageCodes.MC_MODIFY_PROJECT_INFO_REQUEST,
                     Wire.Field(projectId.ToString()) + Wire.Field(name) + Wire.Field(members) + Wire.Field(language) + "False");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        public class Member : INotifyPropertyChanged
        {
            private string role;
            public string Name { get; set; }
            public bool CanChange { get; set; }
            public bool Invited { get; set; }
            public string StatusLabel => Invited ? "  (invited)" : "";
            public string Role
            {
                get => role;
                set { role = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role))); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
