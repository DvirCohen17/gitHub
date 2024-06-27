using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace client_side
{
    public partial class AddProjectWindow : Window
    {
        private Communicator communicator;

        public AddProjectWindow(Communicator communicator)
        {
            InitializeComponent();
            this.communicator = communicator;
        }

        private async void CreateProject_Click(object sender, RoutedEventArgs e)
        {
            // Validate fields
            if (string.IsNullOrEmpty(ProjectNameTextBox.Text))
            {
                ShowErrorMessage("Please enter a project name.");
                return;
            }

            /*
            if (string.IsNullOrEmpty(UserNamesTextBox.Text))
            {
                ShowErrorMessage("Please enter project friends separated by commas.");
                return;
            }
            */

            if (CodeLanguageComboBox.SelectedItem == null)
            {
                ShowErrorMessage("Please select a code language.");
                return;
            }

            // Gather project data
            string projectName = ProjectNameTextBox.Text;
            string projectFriends = UserNamesTextBox.Text;
            string codeLanguage = ((ComboBoxItem)CodeLanguageComboBox.SelectedItem).Content.ToString();
            bool isPublic = PublicCheckBox.IsChecked ?? false;

            // Example: Send data to server and handle response
            string createProjectCode = ((int)MessageCodes.MC_CREATE_PROJECT_REQUEST).ToString();

            string friendsUpdated = "";
            int index = 0;
            while (index < projectFriends.Length)
            {
                // Find next comma or end of string
                int endIndex = projectFriends.IndexOf(',', index);
                if (endIndex == -1)
                    endIndex = projectFriends.Length;

                // Extract the name
                string name = projectFriends.Substring(index, endIndex - index).Trim();

                // Append the formatted string to friendsUpdated
                friendsUpdated += $"{name.Length:D5}{name}";

                // Move index to the next name (skip comma)
                index = endIndex + 1;
            }
            
            communicator.SendData($"{createProjectCode}{projectName.Length:D5}{projectName}{friendsUpdated.Length:D5}" +
                $"{projectFriends}{codeLanguage.Length:D5}{codeLanguage}{isPublic}");

            string createResp = communicator.ReceiveData();
            string createRepCode = createResp.Substring(0, 3);

            if (createRepCode == ((int)MessageCodes.MC_CREATE_PROJECT_RESP).ToString()) //&& createResp.Length > 3
            {
                HomePage filesWindow = new HomePage(communicator);
                filesWindow.Show();
                Close();
            }
            else
            {
                ShowErrorMessage(createResp.Substring(3));
            }
        }

        private void ShowErrorMessage(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            HomePage filesWindow = new HomePage(communicator);
            filesWindow.Show();
            Close();
        }
    }
}
