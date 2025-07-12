using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Linq;

namespace Client
{
    public partial class ClientForm : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private Thread listenThread;
        private string username;
        private string historyFilePath = @"C:\ENES\DERS\Gazi_Ders\2. Sınıf\2. DÖNEM\BİLGİSAYAR AĞLARININ PROGRAMLANMASI\PROJE\GelenDosyalar\chat_history.txt";

        public ClientForm()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                string ip = txtIP.Text;
                int port = int.Parse(txtPort.Text);
                username = txtUsername.Text;

                if (!IPAddress.TryParse(ip, out _))
                {
                    MessageBox.Show("Geçersiz IP adresi.");
                    return;
                }

                client = new TcpClient();
                client.Connect(ip, port);
                stream = client.GetStream();

                byte[] buffer = Encoding.UTF8.GetBytes(username);
                stream.Write(buffer, 0, buffer.Length);

                if (File.Exists(historyFilePath))
                {
                    string[] pastMessages = File.ReadAllLines(historyFilePath);
                    lstMessages.Items.Add("[🕘 Geçmiş mesajlar yüklendi]");
                    foreach (string line in pastMessages)
                        lstMessages.Items.Add(line);
                }

                lstMessages.Items.Add($"Bağlandın: {username}");

                listenThread = new Thread(ListenForMessages);
                listenThread.IsBackground = true;
                listenThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Bağlantı hatası: " + ex.Message);
            }
        }

        private void ListenForMessages()
        {
            byte[] buffer = new byte[1024 * 100];
            while (true)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    Invoke(new Action(() =>
                    {
                        if (message.StartsWith("USERS:"))
                        {
                            string[] users = message.Substring(6).Split(',');
                            lstOnlineUsers.Items.Clear();
                            foreach (string user in users)
                                lstOnlineUsers.Items.Add(user);
                        }
                        else if (message.StartsWith("FILEIMG:"))
                        {
                            string[] parts = message.Split(':');
                            if (parts.Length >= 3)
                            {
                                string fileName = parts[1];
                                string base64Data = string.Join(":", parts.Skip(2));
                                byte[] fileBytes = Convert.FromBase64String(base64Data);

                                using (var ms = new MemoryStream(fileBytes))
                                {
                                    pictureBox1.Image = System.Drawing.Image.FromStream(ms);
                                }

                                string path = Path.Combine(Path.GetDirectoryName(historyFilePath), fileName);
                                File.WriteAllBytes(path, fileBytes);
                                lstMessages.Items.Add($"[📷 Resim alındı]: {fileName}");
                            }
                        }
                        else
                        {
                            lstMessages.Items.Add(message);
                        }
                    }));
                }
                catch
                {
                    break;
                }
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            string message = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                stream.Write(buffer, 0, buffer.Length);

                
                if (!message.StartsWith("/pm "))
                {
                    lstMessages.Items.Add($"[SEN] {message}");
                }

                txtMessage.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Mesaj gönderilemedi: " + ex.Message);
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            if (client != null && client.Connected)
            {
                stream.Close();
                client.Close();
                listenThread?.Abort();
                lstMessages.Items.Add("Bağlantı kesildi.");
            }
        }

        private void btnSendFile_Click(object sender, EventArgs e)
        {
            if (stream == null || !client.Connected)
            {
                MessageBox.Show("Sunucuya bağlı değil.");
                return;
            }

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string filePath = ofd.FileName;
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    string fileName = Path.GetFileName(filePath);
                    string message = $"FILE:{fileName}:{Convert.ToBase64String(fileBytes)}";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    try
                    {
                        stream.Write(data, 0, data.Length);
                        lstMessages.Items.Add($"[📁 Dosya gönderildi]: {fileName}");

                        if (fileName.EndsWith(".jpg") || fileName.EndsWith(".png"))
                        {
                            using (var ms = new MemoryStream(fileBytes))
                            {
                                pictureBox1.Image = System.Drawing.Image.FromStream(ms);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Dosya gönderilemedi: " + ex.Message);
                    }
                }
            }
        }

        private void linkGelenDosyalar_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string klasorYolu = Path.GetDirectoryName(historyFilePath);
            if (Directory.Exists(klasorYolu))
                System.Diagnostics.Process.Start("explorer.exe", klasorYolu);
            else
                MessageBox.Show("Klasör bulunamadı!");
        }
    }
}
