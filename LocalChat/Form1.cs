using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace LocalChat
{
    public partial class Form1 : Form
    {
        private Peer peer;
        private bool connected = false;
        private bool messageBoxKeyPressed = false;
        private bool usernameBoxKeyPressed = false;

        public Form1()
        {
            InitializeComponent();
            peer = new Peer();
            listBox1.DataSource = new BindingSource(peer.ConnectedClients.Keys, null);
            //listBox1.DisplayMember = "Key"; //Username
            //listBox1.ValueMember = "Value"; //IPAdress

            button2.Enabled = false;

            //richTextBox1.Text = "You are using LocalChat v0.2 by MCL & M4a1x";
            richTextBox1.Text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] You are using LocalChat v0.2 by M4a1x & MCL"; //A little bit of credit
            label2.Text = "Connected users: 0";

            peer.MessageRecieved += new MessageEventHandler(peer_MessageRecieved);
            peer.ConnectedClientsChanged += new ConnectedClientsChangedEventHandler(peer_ConnectedClientsChanged);
        }

        void peer_ConnectedClientsChanged()
        {
            if (this.InvokeRequired)
                this.Invoke(new ConnectedClientsChangedEventHandler(peer_ConnectedClientsChanged), new object[] { });

            listBox1.DataSource = null;
            listBox1.DataSource = new BindingSource(peer.ConnectedClients, null);
            listBox1.DisplayMember = "Key"; //Username
            listBox1.ValueMember = "Value"; //IPAdress

            label2.Text = "Connected users: " + (listBox1.Items.Count - 1);
        }

        private void peer_MessageRecieved(string sender, string recipient, string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MessageEventHandler(peer_MessageRecieved), new object[] { sender, recipient, message });
                return;
            }

            if (sender == null)
                richTextBox1.AppendText("\n" + "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
            else
            {
                if (recipient == null)
                    richTextBox1.AppendText("\n" + "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + sender + ": " + message);
                else
                    richTextBox1.AppendText("\n" + "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + sender + " -> " + recipient + ": " + message);
            }

            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!connected)
            {
                Regex regex = new Regex(@"\S"); //Match not whitespace
                Match match = regex.Match(textBox1.Text); //User should type at least 1 non-whitespace character

                if (usernameBoxKeyPressed && match.Success)
                {
                    if (peer.requestConnection(textBox1.Text))
                    {
                        button1.Text = "Disconnect";
                        button2.Enabled = true;
                        connected = true;

                        label1.Text = "Connected as: " + textBox1.Text;
                        textBox1.Visible = false;
                        label1.Visible = true;
                        textBox2.Enabled = true;
                    }
                    else
                        MessageBox.Show("Error starting the local server!\n\nPlease make sure there's no other application running on port 1337 and 7331!", "LocalChat", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                    MessageBox.Show("Please enter a valid Username!", "LocalChat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                peer.disconnect();
                button1.Text = "Connect";
                button2.Enabled = false;
                connected = false;

                textBox1.Visible = true;
                label1.Visible = false;
                textBox2.Enabled = false;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            sendMessage(textBox2.Text);
        }

        private void textBox2_KeyPress(object sender, KeyPressEventArgs e)
        {
            messageBoxKeyPressed = true;
            if (e.KeyChar == (char)Keys.Enter)
            {
                sendMessage(textBox2.Text);
                e.Handled = true;
            }
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            usernameBoxKeyPressed = true;
        }

        private void sendMessage(string message)
        {
            if (connected)
            {
                if (messageBoxKeyPressed && message != "")
                {
                    if (listBox1.SelectedIndex == 0)
                        peer.sendMessage(textBox2.Text);
                    else
                    {
                        var listsel = (KeyValuePair<string, string>)listBox1.SelectedItem;
                        peer.sendMessage(textBox2.Text, listsel.Value);
                    }
                    textBox2.Text = "";
                }
            }
        }

        private void textBox2_Enter(object sender, EventArgs e)
        {
            if (textBox2.Text == "Enter your message here..." && !messageBoxKeyPressed)
                textBox2.Text = "";
        }

        private void textBox2_Leave(object sender, EventArgs e)
        {
            if (textBox2.Text == "")
            {
                textBox2.Text = "Enter your message here...";
                messageBoxKeyPressed = false;
            }
        }

        private void textBox1_Enter(object sender, EventArgs e)
        {
            if (textBox1.Text == "Enter an username..." && !usernameBoxKeyPressed)
                textBox1.Text = "";
        }

        private void textBox1_Leave(object sender, EventArgs e)
        {
            if (textBox1.Text == "")
            {
                textBox1.Text = "Enter an username...";
                usernameBoxKeyPressed = false;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Coming soon", "LocalChat", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
