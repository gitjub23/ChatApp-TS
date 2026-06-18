using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        private string? loggedInUsername = null;
        private TcpClient? client;
        private NetworkStream? stream;
        private Thread? receiveThread;
        private bool isAuthenticated = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MessageBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendButton_Click(null!, null!);
                e.Handled = true;
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAuthenticated)
            {
                AppendMessage("✅ Already connected.");
                return;
            }

            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password.Trim()
            : VisiblePasswordBox.Text.Trim();


            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                AppendMessage("⚠️ Username and password required.");
                return;
            }

            try
            {
                client = new TcpClient("127.0.0.1", 5000);
                stream = client.GetStream();

                string authMessage = $"{username}:{password}";
                await SendMessageAsync(authMessage);

                string response = await ReadMessageAsync();

                if (response == "AUTH_SUCCESS")
                {
                    isAuthenticated = true;
                    loggedInUsername = username;
                    AppendMessage("✅ Login successful.");
                    _ = Task.Run(ReceiveMessages);
                }
                else if (response == "AUTH_FAIL")
                {
                    AppendMessage("❌ Login failed. Incorrect credentials.");
                    Cleanup();
                }
                else if (response == "AUTH_ALREADY_CONNECTED")
                {
                    AppendMessage("⚠️ Login failed. This user is already connected from another window.");
                    Cleanup();
                }
                else
                {
                    AppendMessage($"User already connected");
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"❌ Error during login: {ex.Message}");
                Cleanup();
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAuthenticated)
            {
                AppendMessage("✅ Already connected.");
                return;
            }

            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password.Trim()
            : VisiblePasswordBox.Text.Trim();


            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                AppendMessage("⚠️ Username and password required.");
                return;
            }

            try
            {
                client = new TcpClient("127.0.0.1", 5000);
                stream = client.GetStream();

                string registerMessage = $"REGISTER {username}:{password}";
                await SendMessageAsync(registerMessage);

                string response = await ReadMessageAsync();

                if (response == "REGISTER_SUCCESS")
                {
                    isAuthenticated = true;
                    loggedInUsername = username;
                    AppendMessage("✅ Registration successful.");
                    _ = Task.Run(ReceiveMessages);
                }
                else if (response == "REGISTER_FAIL")
                {
                    AppendMessage("❌ Registration failed. Username may be taken.");
                    Cleanup();
                }
                else
                {
                    AppendMessage($"❓ Unexpected response: {response}");
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"❌ Registration error: {ex.Message}");
                Cleanup();
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (stream == null || !isAuthenticated) return;

            string message = MessageBox.Text.Trim();
            if (string.IsNullOrEmpty(message)) return;

            string finalMessage = message;

            if (UserList.SelectedItem != null && UserList.SelectedItem.ToString() != "Send to All")
            {
                string selectedUser = UserList.SelectedItem!.ToString()!;
                finalMessage = $"@ {selectedUser} {message}";
            }

            await SendMessageAsync(finalMessage);
            AppendMessage($"You: {message}");
            MessageBox.Clear();
        }

        private async void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserList.SelectedItem == null || UserList.SelectedItem.ToString() == "Send to All")
            {
                AppendMessage("⚠️ Select a user to send the file to.");
                return;
            }

            OpenFileDialog dlg = new();
            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;
                string fileName = Path.GetFileName(filePath);
                string selectedUser = UserList.SelectedItem!.ToString()!;

                try
                {
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    string base64 = Convert.ToBase64String(fileBytes);

                    string message = $"FILE:{selectedUser}:{fileName}:{base64}";
                    await SendMessageAsync(message);

                    AppendMessage($"📤 Sent file '{fileName}' to {selectedUser}.");
                }
                catch (Exception ex)
                {
                    AppendMessage($"❌ File send failed: {ex.Message}");
                }
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (stream == null || !stream.CanWrite) return;

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] lengthPrefix = BitConverter.GetBytes(messageBytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthPrefix);

            await stream.WriteAsync(lengthPrefix, 0, 4);
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        private async Task<string> ReadMessageAsync()
        {
            byte[] lengthBuffer = new byte[4];
            int read = await stream!.ReadAsync(lengthBuffer, 0, 4);
            if (read < 4) throw new IOException("Connection closed unexpectedly.");

            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuffer);

            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            byte[] messageBuffer = new byte[messageLength];

            int totalRead = 0;
            while (totalRead < messageLength)
            {
                int bytesRead = await stream.ReadAsync(messageBuffer, totalRead, messageLength - totalRead);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            return Encoding.UTF8.GetString(messageBuffer);
        }

        private async void ReceiveMessages()
        {
            try
            {
                while (client != null && client.Connected && stream != null)
                {
                    string fullMessage = await ReadMessageAsync();

                    if (fullMessage.StartsWith("FILE:"))
                    {
                        string[] parts = fullMessage.Split(':', 4);
                        if (parts.Length < 4) continue;

                        string senderName = parts[1];
                        string filename = parts[2];
                        string base64Data = parts[3];

                        byte[] fileData = Convert.FromBase64String(base64Data);
                        string baseDownloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ChatDownloads");
string userFolder = Path.Combine(baseDownloadDir, loggedInUsername ?? "UnknownUser");
                        Directory.CreateDirectory(userFolder); // Ensure the user's folder exists

                        string fullPath = Path.Combine(userFolder, filename);


                        await Task.Run(() => File.WriteAllBytes(fullPath, fileData));
                        Dispatcher.Invoke(() => AppendMessage($"📥 File received from {senderName}. Saved to: {fullPath}"));
                    }
                    else if (fullMessage.StartsWith("USERS:"))
                    {
                        string[] users = fullMessage[6..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        Dispatcher.Invoke(() =>
                        {
                            UserList.Items.Clear();
                            UserList.Items.Add("Send to All");
                            foreach (string user in users)
                            {
                                UserList.Items.Add(user);
                            }
                            UserList.SelectedIndex = 0;
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => AppendMessage(fullMessage));
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendMessage($"❌ Connection lost: {ex.Message}"));
            }
        }



        private void AppendMessage(string message)
        {
            ChatBox.AppendText($"{message}\n");
            ChatBox.ScrollToEnd();
        }

        private void Cleanup()
        {
            stream?.Close();
            client?.Close();
            stream = null;
            client = null;
            isAuthenticated = false;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Cleanup();
            receiveThread?.Join(500);
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (TogglePasswordVisibility.IsChecked == true)
            {
                VisiblePasswordBox.Text = PasswordBox.Password;
                VisiblePasswordBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;

                PasswordToggleIcon.Text = "🙈"; // Hide password
            }
            else
            {
                PasswordBox.Password = VisiblePasswordBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                VisiblePasswordBox.Visibility = Visibility.Collapsed;

                PasswordToggleIcon.Text = "👁"; // Show password
            }
        }

    }
}
