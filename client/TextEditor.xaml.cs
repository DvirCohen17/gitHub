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

namespace client_side
{
    public partial class TextEditor : Window
    {
        private string filePath;
        private Communicator communicator;
        private Thread receiveServerUpdatesThread;

        bool disconnect = true; // if window closed by the user disconnect

        private bool isListeningToServer = true;
        private bool isCapsLockPressed = false;
        private bool isBackspaceHandled = false;

        public TextEditor(Communicator communicator, string fileName)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            filePath = fileName;
            lblFileName.Text = fileName;
            try
            {
                ReceiveInitialContent(fileName);  // Receive initial content from the server
                ReceiveInitialChat(fileName);     // Receive initial content from the server
                ReceiveInitialUsers(fileName);    // Receive initial content from the server

                using (var stream = new FileStream("C:\\Users\\test0\\OneDrive\\Documents\\cyber\\cloud\\client\\client_side\\CPP-Mode.xshd", FileMode.Open))
                {
                    using (var reader = new XmlTextReader(stream))
                    {
                        txtFileContent.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    }
                }

                receiveServerUpdatesThread = new Thread(() => ReceiveServerUpdates())
                {
                    IsBackground = true
                };
                receiveServerUpdatesThread.Start();

                //txtFileContent.CaretIndex = communicator.UserFileIndex; 
                Closing += TextEditor_CloseFile; // Hook up the closing event handler

            }
            catch (Exception)
            {
                MessageBox.Show("An error occurred while connecting to the server.");
                DisconnectFromServer();
                Application.Current.Shutdown();
                Environment.Exit(0);
            }
        }

        private void TextBoxInput_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
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
                    else if (e.Key == Key.S)
                    {
                        SaveFileContent();
                        return;
                    }
                    else if (e.Key == Key.W)
                    {
                        // Ctrl+W is pressed
                        // Close the window and send a leaveFile message to the server
                        e.Handled = true;
                        // TODO function that leave the file
                        disconnect = false; // if window closed by the user disconnect
                        isListeningToServer = false;

                        string chatMessageCode = ((int)MessageCodes.MC_LEAVE_FILE_REQUEST).ToString();

                        string fullMessage = $"{chatMessageCode}";
                        communicator.SendData(fullMessage);

                        Files mainWindow = new Files(communicator);
                        mainWindow.Show();
                        Close();
                        return;
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

        private async void TextEditor_CloseFile(object sender, EventArgs e)
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
                            HandleInitial(update);
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

                        case "212": // MC_JOIN_FILE_RESP
                            HandleJoinFileResponse(update);
                            break;

                        case "213": // MC_LEAVE_FILE_RESP
                            HandleLeaveFileResponse(update);
                            break;

                        case "302": // MC_APPROVE_RESP
                            break;

                        case "200":
                            HandleError(update);
                            break;

                        case "300": // MC_DISCONNECT
                            HandleDiscconect(update);
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

        private void HandleJoinFileResponse(string update)
        {
            try
            {
                // Assuming the message format is "{code}{userNameLength}{userName}{fileNameLength}{FileName}"
                int lengthIndex = 3;
                int length = int.Parse(update.Substring(lengthIndex, 5));

                string name = update.Substring(8, length);

                // Process the join file response as needed (e.g., update UI)
                Dispatcher.Invoke(() => lstUserList.Items.Add(name));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Join File response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleLeaveFileResponse(string update)
        {
            try
            {
                // Assuming the message format is "{code}{userNameLength}{userName}{fileNameLength}{FileName}"
                int lengthIndex = 3;
                int length = int.Parse(update.Substring(lengthIndex, 5));

                string name = update.Substring(8, length);

                // Process the leave file response as needed (e.g., update UI)
                Dispatcher.Invoke(() => lstUserList.Items.Remove(name));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling Leave File response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void HandleDiscconect(string update)
        {
            try
            {

                // Assuming the message format is "300{name}"
                int lengthIndex = 3;

                string name = update.Substring(lengthIndex);

                // Process the leave file response as needed (e.g., update UI)
                Dispatcher.Invoke(() => lstUserList.Items.Remove(name));


            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling disconnect response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleInitial(string update)
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

        private void ReceiveInitialUsers(string fileName)
        {
            try
            {
                string code = ((int)MessageCodes.MC_GET_USERS_ON_FILE_REQUEST).ToString();
                communicator.SendData($"{code}{fileName}");
                string initialContent = communicator.ReceiveData();
                string codeString = initialContent.Substring(0, 3);

                if (codeString == ((int)MessageCodes.MC_GET_USERS_ON_FILE_RESP).ToString() &&
                    initialContent.Length > 3)
                {
                    List<string> users = new List<string>();

                    int currentIndex = 3;

                    // Extract each user from the response
                    while (currentIndex < initialContent.Length)
                    {
                        int length = int.Parse(initialContent.Substring(currentIndex, 5));
                        currentIndex += 5;

                        string name = initialContent.Substring(currentIndex, length);
                        currentIndex += length;

                        users.Add(name);
                    }

                    // Update the user list in the UI
                    UpdateUserList(users);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling GetUsers response: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReceiveInitialChat(string fileName)
        {
            try
            {
                int newLength = fileName.Length - 4;
                string stringWithoutLast4Chars = fileName.Substring(0, newLength);
                string code = ((int)MessageCodes.MC_GET_MESSAGES_REQUEST).ToString();
                communicator.SendData($"{code}{stringWithoutLast4Chars}");

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

        private void SaveFileContent()
        {
            try
            {
                // Use the Text property to get the content of the TextBox
                string fileContent = txtFileContent.Text;
                File.WriteAllText(filePath, fileContent);
                //MessageBox.Show("File saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void txtFileContent_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (isBackspaceHandled)
            {
                // Reset the flag after handling backspace
                isBackspaceHandled = false;

                // Ensure the caret index is correctly set after backspace
                txtFileContent.CaretOffset = Math.Max(0, txtFileContent.CaretOffset);
            }
        }

        //           ******** Chat ********* 

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

                int newLength = filePath.Length - 4;
                string stringWithoutLast4Chars = filePath.Substring(0, newLength);

                // Construct the message to be sent to the server
                string fullMessage = $"{chatMessageCode}{stringWithoutLast4Chars.Length:D5}" +
                    $"{stringWithoutLast4Chars}{messageLength:D5}{message}";

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

        private void UpdateUserList(IEnumerable<string> userList)
        {
            // Clear the current user list
            Dispatcher.Invoke(() => lstUserList.Items.Clear());

            // Add the updated user list
            Dispatcher.Invoke(() =>
            {
                foreach (var user in userList)
                {
                    lstUserList.Items.Add(user);
                }
            });
        }

        //           ******** Buttons ********* 
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileContent();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedText();
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            PasteClipboardContent(true);
        }

        private void CutButton_Click(object sender, RoutedEventArgs e)
        {
            cutSelectedText();
        }

    }
}