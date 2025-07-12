using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO;

namespace Server
{
    public partial class ServerForm : Form
    {
        private TcpListener server;
        private Thread listenThread;
        private bool isRunning = false;
        private List<(TcpClient client, string username)> serverClients = new List<(TcpClient, string)>();
        private string historyFilePath = @"C:\ENES\DERS\Gazi_Ders\2. Sınıf\2. DÖNEM\BİLGİSAYAR AĞLARININ PROGRAMLANMASI\PROJE\GelenDosyalar\chat_history.txt";
       public ServerForm()
        {
            InitializeComponent();
        }

        private void btnStartServer_Click(object sender, EventArgs e)
        {
            int port = int.Parse(txtPort.Text);
            IPAddress ipAddress = IPAddress.Parse(txtIP.Text);

            server = new TcpListener(ipAddress, port);
            server.Start();
            isRunning = true;

            if (File.Exists(historyFilePath))
            {
                string[] pastMessages = File.ReadAllLines(historyFilePath);
                lstServer.Items.Add("[🕘 Geçmiş mesajlar yüklendi]");
                foreach (string line in pastMessages)
                    lstServer.Items.Add(line);
            }

            listenThread = new Thread(ListenForClients);
            listenThread.IsBackground = true;
            listenThread.Start();

            lstClients.Items.Add($"Sunucu başlatıldı: {ipAddress}:{port}");
        }

        private void ListenForClients()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    NetworkStream stream = client.GetStream();

                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string username = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    serverClients.Add((client, username));
                    BroadcastUserList();

                    Invoke(new Action(() => lstClients.Items.Add(username)));

                    Thread clientThread = new Thread(() => HandleClient(client, username));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
                catch { }
            }
        }

        private void HandleClient(TcpClient client, string username)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024 * 100];

            while (true)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (message.StartsWith("FILE:"))
                    {
                        string[] parts = message.Split(':');
                        if (parts.Length >= 3)
                        {
                            string fileName = parts[1];
                            string base64Data = string.Join(":", parts.Skip(2));
                            byte[] fileData = Convert.FromBase64String(base64Data);

                            string folderPath = Path.GetDirectoryName(historyFilePath);
                            if (!Directory.Exists(folderPath))
                                Directory.CreateDirectory(folderPath);

                            string filePath = Path.Combine(folderPath, fileName);
                            File.WriteAllBytes(filePath, fileData);

                            string logMessage = $"[📁 Dosya alındı] {username} -> {fileName}";
                            AppendToHistory(logMessage);
                            Invoke((MethodInvoker)(() => lstServer.Items.Add(logMessage)));

                            BroadcastMessage($"{username} bir dosya gönderdi: {fileName}");

                            if (fileName.EndsWith(".jpg") || fileName.EndsWith(".png"))
                                BroadcastImageToClients(fileName, base64Data, username);
                        }
                    }
                    else if (message.StartsWith("/pm "))
                    {
                        string trimmed = message.Substring(4);
                        int firstSpace = trimmed.IndexOf(' ');
                        if (firstSpace > 0)
                        {
                            string targetUser = trimmed.Substring(0, firstSpace);
                            string pmContent = trimmed.Substring(firstSpace + 1);

                            var target = serverClients.FirstOrDefault(c => c.username == targetUser);
                            if (target.client != null)
                            {
                                string pmMessage = $"[ÖZEL] {username}: {pmContent}";
                                byte[] pmBuffer = Encoding.UTF8.GetBytes(pmMessage);
                                try
                                {
                                    NetworkStream targetStream = target.client.GetStream();
                                    targetStream.Write(pmBuffer, 0, pmBuffer.Length);
                                }
                                catch
                                {
                                    serverClients.RemoveAll(c => c.client == target.client);
                                }

                                string selfMessage = $"[→ {targetUser}] {pmContent}";
                                byte[] selfBuffer = Encoding.UTF8.GetBytes(selfMessage);
                                stream.Write(selfBuffer, 0, selfBuffer.Length);

                                AppendToHistory($"[PM] {username} -> {targetUser}: {pmContent}");
                                Invoke((MethodInvoker)(() => lstServer.Items.Add($"[PM] {username} -> {targetUser}: {pmContent}")));
                            }
                        }
                    }
                    else
                    {
                        string fullMessage = $"{username}: {message}";
                        AppendToHistory(fullMessage);
                        Invoke((MethodInvoker)(() => lstServer.Items.Add(fullMessage)));
                        BroadcastMessage(fullMessage);
                    }
                }
                catch
                {
                    break;
                }
            }

            client.Close();
            serverClients.RemoveAll(c => c.client == client);
            BroadcastUserList();

            Invoke((MethodInvoker)(() => lstClients.Items.Add($"{username} ayrıldı.")));
        }

        private void BroadcastMessage(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);

            foreach (var (client, _) in serverClients.ToList())
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch
                {
                    serverClients.RemoveAll(c => c.client == client);
                }
            }
        }

        private void BroadcastUserList()
        {
            string userListMessage = "USERS:" + string.Join(",", serverClients.Select(c => c.username));
            byte[] buffer = Encoding.UTF8.GetBytes(userListMessage);

            foreach (var (client, _) in serverClients.ToList())
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch
                {
                    serverClients.RemoveAll(c => c.client == client);
                }
            }
        }

        private void BroadcastImageToClients(string fileName, string base64Data, string sender)
        {
            string msg = $"FILEIMG:{fileName}:{base64Data}";
            byte[] buffer = Encoding.UTF8.GetBytes(msg);

            foreach (var (client, username) in serverClients.ToList())
            {
                if (username != sender)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(buffer, 0, buffer.Length);
                    }
                    catch
                    {
                        serverClients.RemoveAll(c => c.client == client);
                    }
                }
            }
        }

        private void btnStopServer_Click(object sender, EventArgs e)
        {
            isRunning = false;
            server.Stop();
            listenThread?.Abort();
            lstClients.Items.Add("Sunucu kapatıldı.");
        }

        private void AppendToHistory(string line)
        {
            try
            {
                File.AppendAllText(historyFilePath, line + Environment.NewLine);
            }
            catch
            {
                
            }
        }
    }
}
