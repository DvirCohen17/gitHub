using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;
using ICSharpCode.AvalonEdit;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows.Data;

namespace client_side
{
    /// <summary>
    /// Interaction logic for ProjectDirectory.xaml
    /// </summary>
    public partial class ProjectDirectory : Window
    {
        private string ProjectName;
        private int ProjectId;
        private string currFileName;
        private bool insideFile;
        private string codeLaneguage;
        private Communicator communicator;
        private Thread receiveServerUpdatesThread;

        bool disconnect = true; // if window closed by the user disconnect

        private bool isListeningToServer = true;
        private bool isCapsLockPressed = false;
        private bool isBackspaceHandled = false;

        private ObservableCollection<User> friends { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public ProjectDirectory(Communicator communicator, string projectDir, int projectId, string codeLan, bool isEditable)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            ProjectName = projectDir;
            ProjectId = projectId;
            codeLaneguage = codeLan;
            insideFile = false;

            txtFileContent.IsEnabled = isEditable ? true : false ;
            txtFileName.IsReadOnly = isEditable ? false : true ;
            ApplayNameChangeBtn.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
            closeFileBtn.Visibility = Visibility.Collapsed;
            txtNewFileName.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                Friends = new ObservableCollection<User>();
                lstFriends.ItemsSource = Friends;


                ReceiveInitialChat(projectId);     // Receive initial content from the server
                ReceiveInitialUsers();              // Receive initial content from the server
                ReceiveInitialFiles(projectId);    // Receive initial content from the server

                SetCodeLanguageStyle(codeLaneguage);

                receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
                {
                    IsBackground = true
                };
                receiveServerUpdatesThread.Start();

                //txtFileContent.CaretIndex = communicator.UserFileIndex; 
                Closing += ProjectDirectory_CloseFile; // Hook up the closing event handler

            }
            catch (Exception)
            {
                MessageBox.Show("An error occurred while connecting to the server.");
                DisconnectFromServer();
                Application.Current.Shutdown();
                Environment.Exit(0);
            }
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
        
        private void ReceiveInitialUsers()
        {
            try
            {
                string friendsListCode = ((int)MessageCodes.MC_FRIENDS_LIST_REQUEST).ToString();
                communicator.SendData($"{friendsListCode}{communicator.UserName.Length:D5}{communicator.UserName}");

                string friendsRep = communicator.ReceiveData();
                string friendsRepCode = friendsRep.Substring(0, 3);

                if (friendsRepCode == ((int)MessageCodes.MC_FRIENDS_LIST_RESP).ToString() && friendsRep.Length > 3)
                {
                    int index = 3;
                    ObservableCollection<User> updatedFriends = new ObservableCollection<User>();

                    while (index < friendsRep.Length)
                    {
                        int friendNameLen = int.Parse(friendsRep.Substring(index, 5));
                        index += 5;
                        string friendName = friendsRep.Substring(index, friendNameLen);
                        index += friendNameLen;
                        string onlineStatus = friendsRep.Substring(index, 1);
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
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling GetUsers response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SortFriendsList(ObservableCollection<User> friends)
        {
            var sortedFriends = new ObservableCollection<User>(friends.OrderBy(u => u.Name));
            lstFriends.ItemsSource = sortedFriends;
        }

        private void SortFiles(List<FileViewModel> fileList)
        {
            fileList.Sort((f1, f2) => string.Compare(f1.FileName, f2.FileName, StringComparison.Ordinal));
        }

        private void ReceiveInitialChat(int projectId)
        {
            try
            {
                string code = ((int)MessageCodes.MC_GET_MESSAGES_REQUEST).ToString();
                communicator.SendData($"{code}{projectId}");

                string initialContent = communicator.ReceiveData();
                string codeString = initialContent.Substring(0, 3);

                if (codeString == ((int)MessageCodes.MC_GET_MESSAGES_RESP).ToString() &&
                    initialContent.Length > 3)
                {
                    int currentIndex = 3;

                    while (currentIndex < initialContent.Length)
                    {
                        // Extract data length for each message
                        int dataLength = int.Parse(initialContent.Substring(currentIndex, 5));
                        currentIndex += 5;

                        // Extract data from the response
                        string data = initialContent.Substring(currentIndex, dataLength);
                        currentIndex += dataLength;

                        // Extract user name for each message
                        int userNameLen = int.Parse(initialContent.Substring(currentIndex, 5));
                        currentIndex += 5;

                        string userName = initialContent.Substring(currentIndex, userNameLen);
                        currentIndex += userNameLen;

                        if (userName == communicator.UserName)
                        {
                            AppendChatMessage($"You: {data}");
                        }
                        else
                        {
                            AppendChatMessage($"{userName}: {data}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling GetMessages response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReceiveInitialFiles(int projectId)
        {
            try
            {
                string code = ((int)MessageCodes.MC_GET_PROJECT_FILES_REQUEST).ToString();
                communicator.SendData($"{code}{projectId}");

                string initialContent = communicator.ReceiveData();
                string codeString = initialContent.Substring(0, 3);

                if (codeString == ((int)MessageCodes.MC_GET_PROJECT_FILES_RESP).ToString() &&
                    initialContent.Length > 3)
                {
                    int currentIndex = 3;
                    List<FileViewModel> files = new List<FileViewModel>();

                    while (currentIndex < initialContent.Length)
                    {
                        // Extract data length for each message
                        int fileNameLen = int.Parse(initialContent.Substring(currentIndex, 5));
                        currentIndex += 5;

                        // Extract data from the response
                        string fileName = initialContent.Substring(currentIndex, fileNameLen);
                        currentIndex += fileNameLen;

                        // Create a FileViewModel and add to the list
                        files.Add(new FileViewModel { FileName = fileName });
                    }

                    SortFiles(files);

                    // Bind files list to lstFileList ListBox
                    lstFileList.ItemsSource = files;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling GetMessages response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static string GetNextSourceFileName(List<FileViewModel> fileNames, string extension)
        {
            // Regex pattern to match "source" followed by an optional number and the specified extension
            string pattern = $@"^source(\d*)\{extension}$";
            Regex regex = new Regex(pattern);

            // List to store the numbers extracted from the file names
            List<int> sourceNumbers = new List<int>();

            foreach (var file in fileNames)
            {
                Match match = regex.Match(file.FileName);
                if (match.Success)
                {
                    // If the number is present, parse it; otherwise, consider it as "source0"
                    if (int.TryParse(match.Groups[1].Value, out int number))
                    {
                        sourceNumbers.Add(number);
                    }
                    else
                    {
                        sourceNumbers.Add(0);
                    }
                }
            }

            // Find the highest number and return the next available "source" file name
            int nextNumber = sourceNumbers.Count > 0 ? sourceNumbers.Max() + 1 : 0;
            return nextNumber == 0 ? $"source{extension}" : $"source{nextNumber}{extension}";
        }

        private void TextBoxInput_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if(!insideFile)
                {
                    insideFile = true;

                    // Assuming you have a list of file names in your ListBox
                    List<FileViewModel> fileNames = lstFileList.Items.OfType<FileViewModel>().ToList();

                    // Get the next available "source" file name
                    string nextSourceFileName = GetNextSourceFileName(fileNames, "." + codeLaneguage);

                    // Display or use the next available "source" file name
                    string newFileName = nextSourceFileName; // cahnge later to .codeLan

                    string code = ((int)MessageCodes.MC_CREATE_FILE_REQUEST).ToString();
                    communicator.SendData($"{code}{ProjectId.ToString().Length:D5}{ProjectId}" +
                        $"{newFileName.Length:D5}{newFileName}");

                    code = ((int)MessageCodes.MC_ENTER_FILE_REQUEST).ToString();
                    communicator.SendData($"{code}{newFileName.Length:D5}{newFileName}{communicator.UserId}");
                    txtFileName.Text = newFileName;
                    currFileName = newFileName;
                    closeFileBtn.Visibility = Visibility.Visible;
                }

                // Check if Caps Lock key is pressed
                if (e.Key == Key.CapsLock)
                {
                    isCapsLockPressed = !isCapsLockPressed;
                    return; // Do not send a message for Caps Lock key press
                }

                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    // Ctrl key is pressed
                    if (e.Key == Key.C)
                    {
                        // Ctrl+C (copy) is pressed
                        //CopySelectedText();
                        return;
                    }
                    else if (e.Key == Key.V)
                    {
                        // Ctrl+V (paste) is pressed
                        PasteClipboardContent(false);
                    }
                    else if (e.Key == Key.X)
                    {
                        // Ctrl+X (cut) is pressed
                        cutSelectedText();
                    }
                    else if (e.Key == Key.A)
                    {
                        txtFileContent.CaretOffset = txtFileContent.Text.Length;
                    }
                    else if (e.Key == Key.W)
                    {
                        insideFile = false;
                        string chatMessageCode = ((int)MessageCodes.MC_EXIT_FILE_REQUEST).ToString();
                        string fullMessage = $"{chatMessageCode}";
                        communicator.SendData(fullMessage);
                        txtFileContent.Clear();
                        currFileName = "";
                        txtFileName.Text = "";
                    }
                }
                else if (e.Key == Key.Enter)
                {
                    // Enter key is pressed
                    HandleEnter();
                    e.Handled = true;  // Suppress the default handling
                }
                else if (e.Key == Key.Tab)
                {
                    // Tab key is pressed
                    HandleTab();
                    e.Handled = true;  // Suppress the default handling
                }
                else if (e.Key == Key.Back)
                {
                    e.Handled = true;
                    HandleBackspace();
                }
                else if (e.Key != Key.LeftShift && e.Key != Key.RightShift && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
                {
                    int index = txtFileContent.SelectionStart;
                    int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');
                    int selectionLength = txtFileContent.SelectionLength;

                    string code;

                    if (selectionLength > 0)
                    {
                        // Replace the selected text
                        string selectedText = txtFileContent.Text.Substring(index, selectionLength);
                        char replacementText = GetInputChar(e.Key);
                        txtFileContent.Text = txtFileContent.Text.Remove(index, selectionLength);

                        //selectionLength += txtFileContent.Text.Substring(0, index).Count(c => c == '\n');

                        code = ((int)MessageCodes.MC_REPLACE_REQUEST).ToString();
                        communicator.SendData($"{code}{selectionLength:D5}{replacementText.ToString().Length:D5}{replacementText}{index:D5}{newlineCount:D5}"); // Replace action
                        txtFileContent.CaretOffset = index;
                    }
                    else
                    {
                        char inputChar = GetInputChar(e.Key);

                        if (inputChar != '\0')
                        {
                            string inputString = inputChar.ToString();

                            code = ((int)MessageCodes.MC_INSERT_REQUEST).ToString();
                            communicator.SendData($"{code}{inputString.Length:D5}{inputString}{index:D5}{newlineCount:D5}"); // Insert action
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling key down: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        //           ******** Handlers ********* 
        private void HandleEnter()
        {
            try
            {
                // Get the current caret index
                int index = txtFileContent.CaretOffset;
                int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');

                if (txtFileContent.SelectionLength > 0)
                {
                    // Replace the selected text with a new line
                    index = txtFileContent.SelectionStart;
                    int selectionLength = txtFileContent.SelectionLength;
                    txtFileContent.Text = txtFileContent.Text.Remove(index, selectionLength).Insert(index, "\n");

                    // Send the replace action to the server
                    string code = ((int)MessageCodes.MC_REPLACE_REQUEST).ToString();
                    communicator.SendData($"{code}{selectionLength:D5}00001\n{index:D5}{newlineCount:D5}");

                    // Set the caret index to the position after the inserted new line
                    txtFileContent.CaretOffset = index + 1;
                }
                else
                {
                    // Insert a new line at the caret position
                    txtFileContent.Text = txtFileContent.Text.Insert(index, "\n");

                    // Move the caret to the position after the inserted new line
                    txtFileContent.CaretOffset = index + 1;

                    // Send the server a message about the Enter key press
                    string code = ((int)MessageCodes.MC_INSERT_REQUEST).ToString();
                    communicator.SendData($"{code}00001\n{index:D5}{newlineCount:D5}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Enter key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleTab()
        {
            // Implement the logic to handle the Tab key press
            // For example, you can insert a tab character at the current caret position
            int index = txtFileContent.CaretOffset;
            int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');
            string tabString = "    ";

            // Insert the tab character at the current caret position
            txtFileContent.Text = txtFileContent.Text.Insert(index, tabString);

            // Move the caret position after the inserted tab
            txtFileContent.CaretOffset = index + tabString.Length;

            // Send the Tab action to the server
            string code = ((int)MessageCodes.MC_INSERT_REQUEST).ToString();
            communicator.SendData($"{code}{tabString.Length:D5}{tabString}{index:D5}{newlineCount:D5}");
        }

        private void CopySelectedText()
        {
            // Copy the selected text to the clipboard
            string selectedText = txtFileContent.SelectedText;
            Clipboard.SetText(selectedText);
        }

        private void PasteClipboardContent(bool isButton)
        {
            // Paste the clipboard content at the current caret position
            int index = txtFileContent.CaretOffset;
            int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');
            string clipboardContent = Clipboard.GetText();
            Clipboard.Clear();
            clipboardContent = clipboardContent.Replace("\r\n", "\n");
            if (isButton) // if its comming from the button press it wont insert it automaticly
            {
                txtFileContent.Text = txtFileContent.Text.Insert(index, clipboardContent);
                txtFileContent.CaretOffset = index + clipboardContent.Length;
            }
            else
            {
                // If it's not coming from the button, insert automatically
                txtFileContent.SelectedText = clipboardContent;
                txtFileContent.CaretOffset = index + clipboardContent.Length;
            }

            string code = ((int)MessageCodes.MC_INSERT_REQUEST).ToString();
            communicator.SendData($"{code}{clipboardContent.Length:D5}{clipboardContent}{index:D5}{newlineCount:D5}");
        }

        private void cutSelectedText()
        {
            // Copy the selected text to the clipboard
            CopySelectedText();

            int index = txtFileContent.SelectionStart;
            int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');
            int selectionLength = txtFileContent.SelectionLength;

            string deletedText = txtFileContent.Text.Substring(index, selectionLength);
            txtFileContent.Text = txtFileContent.Text.Remove(index, selectionLength);

            string code = ((int)MessageCodes.MC_DELETE_REQUEST).ToString();
            communicator.SendData($"{code}{selectionLength:D5}{index:D5}{newlineCount:D5}"); // Delete action

            // Maintain the cursor position
            txtFileContent.CaretOffset = index;
        }

        private void HandleBackspace()
        {
            int index = txtFileContent.SelectionStart;
            int newlineCount = txtFileContent.Text.Substring(0, index).Count(c => c == '\n');
            int selectionLength = txtFileContent.SelectionLength;

            string code = ((int)MessageCodes.MC_DELETE_REQUEST).ToString();

            if (selectionLength > 0)
            {
                // Delete the selected text
                string deletedText = txtFileContent.Text.Substring(index, selectionLength);
                txtFileContent.Text = txtFileContent.Text.Remove(index, selectionLength);

                communicator.SendData($"{code}{selectionLength:D5}{index:D5}{newlineCount:D5}"); // Delete action

                // Maintain the cursor position
                txtFileContent.CaretOffset = index;
            }
            else if (index > 0)
            {
                // Check if it's the beginning of the line
                int lineStartIndex = txtFileContent.Text.LastIndexOf(Environment.NewLine, index - 1) + 1;

                if (index == lineStartIndex)
                {
                    // Delete the entire line
                    string deletedLine = txtFileContent.Text.Substring(lineStartIndex, index - lineStartIndex);
                    txtFileContent.Text = txtFileContent.Text.Remove(lineStartIndex, index - lineStartIndex);

                    communicator.SendData($"{code}{deletedLine.Length:D5}{lineStartIndex:D5}{newlineCount:D5}"); // Delete action for the entire line

                    // Maintain the cursor position
                    txtFileContent.CaretOffset = lineStartIndex;
                }
                else
                {
                    // Delete a single character at the current index
                    txtFileContent.Text = txtFileContent.Text.Remove(index - 1, 1);

                    communicator.SendData($"{code}00001{(index - 1):D5}00000"); // Delete action with length 1
                                                                                // Maintain the cursor position
                    txtFileContent.CaretOffset = index - 1;
                }
            }
        }

        private char GetInputChar(Key key)
        {
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            switch (key)
            {
                case Key.A:
                case Key.B:
                case Key.C:
                case Key.D:
                case Key.E:
                case Key.F:
                case Key.G:
                case Key.H:
                case Key.I:
                case Key.J:
                case Key.K:
                case Key.L:
                case Key.M:
                case Key.N:
                case Key.O:
                case Key.P:
                case Key.Q:
                case Key.R:
                case Key.S:
                case Key.T:
                case Key.U:
                case Key.V:
                case Key.W:
                case Key.X:
                case Key.Y:
                case Key.Z:
                    bool isCapsLockPressed = Console.CapsLock;

                    char baseChar = char.ToLower((char)KeyInterop.VirtualKeyFromKey(key));
                    return (isShiftPressed ^ isCapsLockPressed) ? char.ToUpper(baseChar) : baseChar;

                case Key.D0:
                    return isShiftPressed ? ')' : '0';
                case Key.D1:
                    return isShiftPressed ? '!' : '1';
                case Key.D2:
                    return isShiftPressed ? '@' : '2';
                case Key.D3:
                    return isShiftPressed ? '#' : '3';
                case Key.D4:
                    return isShiftPressed ? '$' : '4';
                case Key.D5:
                    return isShiftPressed ? '%' : '5';
                case Key.D6:
                    return isShiftPressed ? '^' : '6';
                case Key.D7:
                    return isShiftPressed ? '&' : '7';
                case Key.D8:
                    return isShiftPressed ? '*' : '8';
                case Key.D9:
                    return isShiftPressed ? '(' : '9';

                case Key.OemComma:
                    return isShiftPressed ? '<' : ',';
                case Key.OemPeriod:
                    return isShiftPressed ? '>' : '.';
                case Key.OemQuestion:
                    return isShiftPressed ? '?' : '/';
                case Key.OemOpenBrackets:
                    return isShiftPressed ? '{' : '[';
                case Key.OemCloseBrackets:
                    return isShiftPressed ? '}' : ']';
                case Key.OemSemicolon:
                    return isShiftPressed ? ':' : ';';
                case Key.OemQuotes:
                    return isShiftPressed ? '"' : '\'';
                case Key.OemPipe:
                    return isShiftPressed ? '|' : '\\';
                case Key.OemTilde:
                    return isShiftPressed ? '~' : '`';
                case Key.OemMinus:
                    return isShiftPressed ? '_' : '-';
                case Key.OemPlus:
                    return isShiftPressed ? '+' : '=';

                case Key.Space:
                    return ' ';

                default:
                    return '\0'; // Invalid key
            }
        }

        private async void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            FileViewModel selectedFile = lstFileList.SelectedItem as FileViewModel;
            if (selectedFile != null)
            {
                if(insideFile)
                {
                    string chatMessageCode = ((int)MessageCodes.MC_EXIT_FILE_REQUEST).ToString();
                    string fullMessage = $"{chatMessageCode}";
                    communicator.SendData(fullMessage);
                }

                string FileName = selectedFile.FileName;
                string code = ((int)MessageCodes.MC_ENTER_FILE_REQUEST).ToString();
                communicator.SendData($"{code}{FileName.Length:D5}{FileName}{communicator.UserId}");
                insideFile = true;
                currFileName = FileName;
                txtFileName.Text = FileName;

                Thread.Sleep(500); // Sleep for 1000 milliseconds (1 second)

                code = ((int)MessageCodes.MC_INITIAL_REQUEST).ToString();
                communicator.SendData($"{code}{ProjectId.ToString().Length:D5}{ProjectId}{FileName.Length:D5}{FileName}");
                closeFileBtn.Visibility = Visibility.Visible;
            }
        }
        
        private void LstFiles_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back)
            {
                removeFile(sender, e);
            }
        }

        private void removeFile(object sender, RoutedEventArgs e)
        {
            FileViewModel selectedFile = lstFileList.SelectedItem as FileViewModel;
            if (selectedFile != null)
            {
                // Copy the selectedFile to avoid modifying the original list
                FileViewModel fileToRemove = new FileViewModel
                {
                    FileName = selectedFile.FileName,
                    // Copy other properties as needed
                };

                string code = ((int)MessageCodes.MC_DELETE_FILE_REQUEST).ToString();
                communicator.SendData($"{code}{ProjectId.ToString().Length:D5}{ProjectId}" +
                    $"{fileToRemove.FileName.Length:D5}{fileToRemove.FileName}");
            }
        }

        private void SetCodeLanguageStyle(string codeLanguage)
        {
            string filePath = null;
            switch (codeLanguage)
            {
                case "cpp":
                    filePath = $"{communicator.DirectoryPath}\\C++-Mode.xshd";
                    break;
                case "python":
                    filePath = $"{communicator.DirectoryPath}\\Python-Mode.xshd";
                    codeLaneguage = "py";
                    break;
                case "cs":
                    filePath = $"{communicator.DirectoryPath}\\C#-Mode.xshd";
                    break;
                case "java":
                    filePath = $"{communicator.DirectoryPath}\\Java-Mode.xshd";
                    break;
                case "JavaScript":
                    filePath = $"{communicator.DirectoryPath}\\JavaScript-Mode.xshd";
                    break;
                default:
                    txtFileContent.SyntaxHighlighting = null;
                    return;
            }

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    using (var reader = new XmlTextReader(stream))
                    {
                        txtFileContent.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while loading the syntax highlighting file: {ex.Message}");
                txtFileContent.SyntaxHighlighting = null;
            }
        }

        private void DisconnectFromServer()
        {
            try
            {
                // Send a disconnect message to the server
                string disconnectCode = ((int)MessageCodes.MC_DISCONNECT).ToString();
                communicator.SendData(disconnectCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error disconnecting from the server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    // Receive update from the server
                    string update = communicator.ReceiveData();

                    string code = update.Substring(0, 3); // Assuming the message code is always 3 characters
                    
                    switch (code)
                    {
                        case "201":
                            HandleInitialFileContent(update);
                            break;

                        case "202": // MC_INSERT_RESP
                            HandleInsertResponse(update);
                            break;

                        case "203": // MC_DELETE_RESP
                            HandleDeleteResponse(update);
                            break;

                        case "204": // MC_REPLACE_RESP
                            HandleReplaceResponse(update);
                            break;

                        case "211": // MC_POST_MSG_RESP
                            HandlePostMessageResponse(update);
                            break;

                        case "302": // MC_APPROVE_RESP
                            break;

                        case "207": // MC_ADD_FILE_RESP
                            HandleAddFile(update);
                            break;

                        case "214": // MC_DELETE_FILE_RESP
                            HandleDeleteFile(update);
                            break;

                        case "200":
                            HandleError(update);
                            break;

                        case "300": // MC_DISCONNECT
                            HandleDisconnect(update);
                            break;

                        case "224":
                            HandleRemoveUser(update);
                            break;

                        case "401" or "403":
                            HandleLogin(update);
                            break;
                        case "233":
                            HandleRenameFile(update);
                            break;
                        case "231":
                            HandleBackToMainPage();
                            break;

                        default:
                            throw new InvalidOperationException($"Unknown message code: {code}");
                    }
                   
                }
                //receiveServerUpdatesThread.Abort();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error receiving server updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleRenameFile(string update)
        {
            string msg = update.Substring(3);

            // Extract the lengths and names
            int newFileNameLen = int.Parse(msg.Substring(0, 5));
            string newFileName = msg.Substring(5, newFileNameLen);

            string remainingMsg = msg.Substring(5 + newFileNameLen);
            int oldFileNameLen = int.Parse(remainingMsg.Substring(0, 5));
            string oldFileName = remainingMsg.Substring(5, oldFileNameLen);

            Dispatcher.Invoke(() =>
            {
                if (lstFileList.ItemsSource == null)
                {
                    lstFileList.ItemsSource = new List<FileViewModel>();
                }

                if (lstFileList.ItemsSource is List<FileViewModel> fileList)
                {
                    // Find and remove the old file
                    var oldFile = fileList.FirstOrDefault(f => f.FileName == oldFileName);
                    if (oldFile != null)
                    {
                        fileList.Remove(oldFile);
                    }

                    // Add the new file
                    FileViewModel newFile = new FileViewModel { FileName = newFileName };
                    fileList.Add(newFile);

                    // Sort the file list
                    SortFiles(fileList);

                    // Refresh the ListBox
                    lstFileList.ItemsSource = null;
                    lstFileList.ItemsSource = fileList;
                }
            });
        }

        private void HandleAddFile(string update)
        {
            string msg = update.Substring(3);

            // Parse msg to extract filename and status
            string filename = msg.Substring(0);

            FileViewModel newFile = new FileViewModel
            {
                FileName = filename,
            };
            Dispatcher.Invoke(() =>
            {
                if (lstFileList.ItemsSource == null)
                {
                    lstFileList.ItemsSource = new List<FileViewModel>();
                }

                if (lstFileList.ItemsSource is List<FileViewModel> fileList)
                {
                    int insertIndex = 0;
                    while (insertIndex < fileList.Count && string.Compare(fileList[insertIndex].FileName, newFile.FileName, StringComparison.Ordinal) < 0)
                    {
                        insertIndex++;
                    }

                    fileList.Insert(insertIndex, newFile);
                    SortFiles(fileList);

                    lstFileList.ItemsSource = null;
                    lstFileList.ItemsSource = fileList;
                }
            });
        }
        
        private void HandleDeleteFile(string update)
        {
            string deletedFileName = update.Substring(3);

            Dispatcher.Invoke(() =>
            {
                if (lstFileList.ItemsSource is List<FileViewModel> fileList)
                {
                    // Find the index of the file to delete
                    int deleteIndex = fileList.FindIndex(file => file.FileName == deletedFileName);

                    // If the file is found, remove it from the list
                    if (deleteIndex != -1)
                    {
                        fileList.RemoveAt(deleteIndex);

                        SortFiles(fileList);

                        // Set the ListBox's ItemsSource again to trigger the update
                        lstFileList.ItemsSource = null;
                        lstFileList.ItemsSource = fileList;
                    }
                }
            });
        }

        private void HandleInsertResponse(string update)
        {
            // Parse update from server, e.g., "202{lenOfInput}{input}{index}{newLineCount}"
            int lenIndex = 3;
            int lenOfInput = int.Parse(update.Substring(lenIndex, 5));
            int inputIndex = lenIndex + 5;
            string input = update.Substring(inputIndex, lenOfInput);
            int indexIndex = inputIndex + lenOfInput;
            int index = int.Parse(update.Substring(indexIndex, 5));

            Dispatcher.Invoke(() =>
            {
                int currentIndex = txtFileContent.CaretOffset; // Moved inside Dispatcher.Invoke
                if (index >= 0 && index <= txtFileContent.Document.TextLength)
                {
                    txtFileContent.Document.Insert(index, input);

                    // Adjust caret position after insert
                    if (currentIndex >= index)
                    {
                        txtFileContent.CaretOffset = currentIndex + lenOfInput;
                    }
                    else
                    {
                        txtFileContent.CaretOffset = currentIndex;
                    }
                }
            });
        }

        private void HandleDeleteResponse(string update)
        {
            // Parse update from server, e.g., "{code}{length}{index}{newLineCount}"
            int lengthIndex = 3;
            int length = int.Parse(update.Substring(lengthIndex, 5));
            int indexIndex = lengthIndex + 5;
            int index = int.Parse(update.Substring(indexIndex, 5));

            Dispatcher.Invoke(() =>
            {
                int currentIndex = txtFileContent.CaretOffset; // Moved inside Dispatcher.Invoke
                if (index >= 0 && index < txtFileContent.Document.TextLength)
                {
                    txtFileContent.Document.Remove(index, length);

                    // Adjust caret position after delete
                    if (currentIndex > index)
                    {
                        txtFileContent.CaretOffset = currentIndex - length;
                    }
                    else
                    {
                        txtFileContent.CaretOffset = currentIndex;
                    }
                }
            });
        }

        private void HandleReplaceResponse(string update)
        {
            // Parse update from server, e.g., "{code}{lengthToRemove}{replacementTextLength}{replacementText}{index}{newLineCount}"
            int lengthToRemoveIndex = 3;
            int lengthToRemove = int.Parse(update.Substring(lengthToRemoveIndex, 5));
            int replacementTextLengthIndex = lengthToRemoveIndex + 5;
            int replacementTextLength = int.Parse(update.Substring(replacementTextLengthIndex, 5));
            int replacementTextIndex = replacementTextLengthIndex + 5;
            string replacementText = update.Substring(replacementTextIndex, replacementTextLength);
            int indexIndex = replacementTextIndex + replacementTextLength;
            int index = int.Parse(update.Substring(indexIndex, 5));

            Dispatcher.Invoke(() =>
            {
                int currentIndex = txtFileContent.CaretOffset; // Moved inside Dispatcher.Invoke
                if (index >= 0 && index < txtFileContent.Document.TextLength)
                {
                    txtFileContent.Document.Replace(index, lengthToRemove, replacementText);

                    // Adjust caret position after replace
                    if (currentIndex >= index)
                    {
                        txtFileContent.CaretOffset = currentIndex + replacementText.Length - lengthToRemove;
                    }
                    else
                    {
                        txtFileContent.CaretOffset = currentIndex;
                    }
                }
            });
        }

        private void HandlePostMessageResponse(string update)
        {
            try
            {
                int dataLength = int.Parse(update.Substring(3, 5));

                // Extract data from the response
                string data = update.Substring(8, dataLength);

                // Extract user name for each message
                int userNameLen = int.Parse(update.Substring(8 + dataLength, 5));
                string userName = update.Substring(13 + dataLength, userNameLen);

                AppendChatMessage($"{userName}: {data}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling PostMessage response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleError(string update)
        {
            try
            {
                string lengthString = update.Substring(0, 5);
                int length = int.Parse(lengthString);
                string data = update.Substring(5);

                // Ensure the length matches the actual data length
                if (length == data.Length)
                {
                    // Update the TextBox with the initial content
                    Dispatcher.Invoke(() =>
                    {
                        txtFileContent.Clear();
                        txtFileContent.Text = data;
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling error response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void HandleInitialFileContent(string update)
        {
            try
            {
                string lengthString = update.Substring(3, 5);
                int length = int.Parse(lengthString);
                string data = update.Substring(8);

                // Ensure the length matches the actual data length
                if (length == data.Length)
                {
                    // Update the TextBox with the initial content
                    Dispatcher.Invoke(() =>
                    {
                        txtFileContent.Clear();
                        txtFileContent.Text = data;
                    });
                }
                else
                {
                    // Handle the case where the length does not match the data length
                    MessageBox.Show("Error: Length mismatch in initial content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: Length mismatch in initial content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ReceiveInitialContent(string fileName)
        {
            try
            {
                string code = ((int)MessageCodes.MC_INITIAL_REQUEST).ToString();
                communicator.SendData($"{code}{fileName}");
                string initialContent = communicator.ReceiveData();
                string codeString = initialContent.Substring(0, 3);
                if (codeString == ((int)MessageCodes.MC_INITIAL_RESP).ToString() &&
                    initialContent.Length > 3)
                {
                    initialContent = initialContent.Substring(3);

                    // Extract length and data
                    string lengthString = initialContent.Substring(0, 5);
                    int length = int.Parse(lengthString);
                    string data = initialContent.Substring(5);

                    // Ensure the length matches the actual data length
                    if (length == data.Length)
                    {
                        // Update the TextBox with the initial content
                        Dispatcher.Invoke(() =>
                        {
                            txtFileContent.Text = data;
                        });
                    }
                    else
                    {
                        // Handle the case where the length does not match the data length
                        MessageBox.Show("Error: Length mismatch in initial content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error receiving initial content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ProjectDirectory_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    isListeningToServer = false; // Stop listening to the server
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();

                    string fullMessage = $"{chatMessageCode}{communicator.UserId:D5}";

                    communicator.SendData(fullMessage);

                    // Close the window on the UI thread
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TxtChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Handle sending chat messages
                string chatMessage = txtChatInput.Text;
                if (IsValidMessage(chatMessage))
                {
                    // Clear the error message
                    lblErr.Content = string.Empty;

                    // Continue with sending the message or other actions
                    SendChatMessage(chatMessage);

                    // Clear the input TextBox after sending the message
                    txtChatInput.Clear();
                }
                else
                {
                    // Display an error message
                    lblErr.Content = "Invalid characters in the message.\n Please use only letters, numbers";
                }
            }
        }

        private bool IsValidMessage(string message)
        {
            // Your validation logic here
            // For example, allow only letters, numbers, and specific special characters
            return System.Text.RegularExpressions.Regex.IsMatch(message, @"^[A-Za-z0-9,."":\[\]{}\+=_!@#$%^&*()<>?/~` ]+$");
        }

        private void SendChatMessage(string message)
        {
            try
            {
                string chatMessageCode = ((int)MessageCodes.MC_POST_MSG_REQUEST).ToString();

                // Get the length of the message
                int messageLength = message.Length;

                // Get the user ID (you may need to replace this with the actual user ID logic)
                int userId = communicator.UserId; // Assuming you have a property UserId in your class

                // Construct the message to be sent to the server
                string fullMessage = $"{chatMessageCode}{ProjectId.ToString().Length:D5}{ProjectId}" +
                    $"{messageLength:D5}{message}";

                communicator.SendData(fullMessage);

                // Update the UI with the sent message
                AppendChatMessage($"You: {message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending chat message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendChatMessage(string message)
        {
            // Append the chat message to the txtChat TextBox
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(txtChat.Text))
                {
                    txtChat.AppendText(message);
                }
                else
                {
                    txtChat.AppendText(Environment.NewLine + message);
                }
                txtChat.ScrollToEnd(); // Scroll to the end to show the latest message
            });
        }

        private void TxtNewFileName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (txtNewFileName.Text.Length > 0 && IsValidMessage(txtNewFileName.Text))
                {
                    if (FileExists(txtNewFileName.Text))
                    {
                        lblErr.Content = "File already exists";
                        return;
                    }

                    string newName = txtNewFileName.Text + "." + codeLaneguage;
                    string code = ((int)MessageCodes.MC_CREATE_FILE_REQUEST).ToString();
                    communicator.SendData($"{code}{ProjectId.ToString().Length:D5}{ProjectId}" +
                        $"{newName.Length:D5}{newName}");
                    return;
                }
            }
        }

        private bool FileExists(string fileName)
        {
            foreach (var item in lstFileList.Items)
            {
                if (item is FileViewModel file && file.FileName == fileName)
                {
                    return true;
                }
            }
            return false;
        }

        private void ReturnToMainPage_Click(object sender, RoutedEventArgs e)
        {
            if (insideFile)
            {
                string MessageCode = ((int)MessageCodes.MC_EXIT_FILE_REQUEST).ToString();
                string Message = $"{MessageCode}";
                communicator.SendData(Message);
            }
            string chatMessageCode = ((int)MessageCodes.MC_EXIT_PROJECT_REQUEST).ToString();

            string fullMessage = $"{chatMessageCode}";
            communicator.SendData(fullMessage);
            return;
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

        private void ApplyFileNameChange_Click(object sender, RoutedEventArgs e)
        {
            string newFileName = txtFileName.Text.Trim();
            if (!string.IsNullOrEmpty(newFileName))
            {
                // Logic to rename the file
                RenameFile(newFileName);
            }
        }

        private void CloseFile_Click(object sender, RoutedEventArgs e)
        {
            insideFile = false;
            string chatMessageCode = ((int)MessageCodes.MC_EXIT_FILE_REQUEST).ToString();
            string fullMessage = $"{chatMessageCode}";
            communicator.SendData(fullMessage);
            currFileName = "";
            txtFileName.Text = "";
            closeFileBtn.Visibility = Visibility.Collapsed;
            // Clear the content of the TextEditor
            txtFileContent.Clear();
        }

        private void RenameFile(string newFileName)
        {
            string chatMessageCode = ((int)MessageCodes.MC_RENAME_FILE_REQUEST).ToString();
            string fullMessage = $"{chatMessageCode}{newFileName.Length:D5}{newFileName}{ProjectId.ToString().Length:D5}{ProjectId}";
            communicator.SendData(fullMessage);
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

        public event PropertyChangedEventHandler PropertyChangeda;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public class FileViewModel
        {
            public string FileName { get; set; }
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

        public enum Status
        {
            Offline,
            Online,
            search
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
}
