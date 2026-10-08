using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace client_side
{
    // Create / edit a task. Purely a form: the to-do list window sends the result.
    public partial class IssueDataWindow : Window
    {
        public string Detail { get; private set; } = "";
        public string DueDate { get; private set; } = "";
        public string EncodedAssignees { get; private set; } = ""; // run of <5-digit byte length><name>

        private readonly List<MemberChoice> choices;

        public IssueDataWindow(Window owner, IEnumerable<string> projectMembers, Issue existing)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            Owner = owner;
            Background = owner.Background;
            Foreground = owner.Foreground;

            var assigned = new HashSet<string>(existing?.Users.Select(u => u.Name) ?? Enumerable.Empty<string>());
            // Keep assignees who have since left the project visible (and selected).
            choices = projectMembers.Union(assigned).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new MemberChoice { Name = n, IsSelected = assigned.Contains(n) }).ToList();
            MembersList.ItemsSource = choices;

            if (existing != null)
            {
                Title = "Edit task";
                txtHeading.Text = "Edit task";
                SaveBtn.Content = "Save";
                IssueDataTextBox.Text = existing.Detail;
                IssueDatePicker.SelectedDate = existing.Date;
            }
            else
            {
                Title = "New task";
                IssueDatePicker.SelectedDate = DateTime.Today.AddDays(7);
            }
            Loaded += (_, _) => IssueDataTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string text = IssueDataTextBox.Text.Trim();
            if (text.Length == 0)
            {
                ErrorTextBlock.Text = "Describe the task.";
                return;
            }
            if (IssueDatePicker.SelectedDate == null)
            {
                ErrorTextBlock.Text = "Pick a due date.";
                return;
            }
            Detail = text;
            DueDate = IssueDatePicker.SelectedDate.Value.ToString(Issue.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
            EncodedAssignees = string.Concat(choices.Where(c => c.IsSelected).Select(c => Wire.Field(c.Name)));
            DialogResult = true;
        }

        public class MemberChoice
        {
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }
    }

    public class User
    {
        public string Name { get; set; }
    }
}
