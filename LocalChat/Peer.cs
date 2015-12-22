using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace LocalChat
{
    public delegate void MessageEventHandler(string sender, string recipient, string message);
    public delegate void ConnectedClientsChangedEventHandler();

    class Peer
    {
        private bool udpClosed = false;
        private string username = "Unkown";
        private static int tcpPort = 1337;
        private static int udpPort = 7331;
        private UdpClient udpClient;
        private TcpListener tcpListener;


        private Thread udpListenThread;
        private Thread tcpListenThread;

        private List<Thread> threadList;
        private Dictionary<IPAddress, TcpClient> workerlist;
        private Dictionary<IPAddress, string> usernamelist;

        public event MessageEventHandler MessageRecieved;
        public event ConnectedClientsChangedEventHandler ConnectedClientsChanged;

        public Dictionary<string, string> ConnectedClients
        {
            get
            {
                Dictionary<string, string> list = new Dictionary<string, string>();
                list.Add("All", "255.255.255.255"); //Key = Username, Value = IP
                foreach (KeyValuePair<IPAddress, string> peer in usernamelist)
                {
                    list.Add(peer.Value, peer.Key.ToString());
                }
                return list;
            }
        }

        public Peer()
        {
            threadList = new List<Thread>();
            workerlist = new Dictionary<IPAddress, TcpClient>();
            usernamelist = new Dictionary<IPAddress, string>();

            Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

            Trace.WriteLine("LocalChat v0.2 startup complete");
        }

        private bool startListen()
        {
            try
            {
                if (tcpListener == null)
                    tcpListener = new TcpListener(IPAddress.Any, tcpPort);

                if (udpClient == null || udpClosed)
                {
                    udpClient = new UdpClient(udpPort);
                    //udpClient = new UdpClient(udpPort, AddressFamily.InterNetwork);
                    udpClosed = false;
                }

                if (udpListenThread == null || !udpListenThread.IsAlive)
                {
                    udpListenThread = new Thread(new ThreadStart(listenForUdpClients));
                    udpListenThread.IsBackground = true;
                    udpListenThread.Start();
                }
                if (tcpListenThread == null || !tcpListenThread.IsAlive)
                {
                    tcpListenThread = new Thread(new ThreadStart(listenForTcpClients));
                    tcpListenThread.IsBackground = true;
                    tcpListenThread.Start();
                }
                Trace.WriteLine("Server started on TCP-Port: " + tcpPort + " and UDP-Port: " + udpPort);
                return true;
            }
            catch
            {
                Trace.WriteLine("Couldn't start server on Tcp-Port: " + tcpPort + " and Udp-Port: " + udpPort);
                return false;
            }
        }

        private void listenForUdpClients()
        {
            while (true)
            {
                IPEndPoint ipendpoint = new IPEndPoint(IPAddress.Any, udpPort); //Get the peer's IP address
                byte[] message = udpClient.Receive(ref ipendpoint); //Receive peer data - blocking

                UTF8Encoding encoder = new UTF8Encoding();
                string username = encoder.GetString(message, 1, message.Length - 1); //Get the peer's user name

                IPAddress address = ipendpoint.Address; //Get the peer's IP

                if (message[0] == 170) //Request connection broadcast
                {
                    if (checkaddress(address)) //Filter out own broadcast
                    {
                        Trace.WriteLine("Got connection request from " + address + " (" + username + ")");

                        if (!workerlist.ContainsKey(address)) //Check if IP is already connected
                        {
                            usernamelist.Add(address, username); //Store the peer's username

                            answerConnection(true, ipendpoint);

                            Trace.WriteLine("Answered " + address + " (" + username + ") connection allowed");
                        }
                        else
                        {
                            answerConnection(false, ipendpoint);
                            Trace.WriteLine("Answered " + address + " (" + username + ") connection declined: peer has already an open connection from this IP");
                        }
                    }
                }
                else if (message[0] == 255) //Answer connection unicast
                {
                    Trace.WriteLine("Got answer from " + address + " (" + username + ") allowing us to connect; connecting...");

                    try //Try to connect
                    {

                        TcpClient newpeer = new TcpClient(address.ToString(), tcpPort); //Connect to the client

                        workerlist.Add(address, newpeer); //Add client to the connection lists
                        usernamelist.Add(address, username);

                        ConnectedClientsChanged();

                        Thread clientThread = new Thread(new ParameterizedThreadStart(handleClientComm));
                        threadList.Add(clientThread);
                        clientThread.IsBackground = true;
                        clientThread.Start(address);

                        Trace.WriteLine("Connected to " + address + " (" + username + ")");
                    }
                    catch
                    {
                        Trace.WriteLine("Failed to connect to " + address + " (" + username + ")!");
                    }
                }
                else     //message[0] == 0  //got reply negate
                {

                    Trace.WriteLine("Got answer from " + address + " (" + username + ") disallowing us to connect!");
                }
            }
        }

        private void listenForTcpClients()
        {
            tcpListener.Start();

            while (true)
            {

                TcpClient client = tcpListener.AcceptTcpClient(); //Blocks until a client has connected to the server

                IPAddress clientaddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address; //Get the peer's IP address

                if (usernamelist.ContainsKey(clientaddress)) //Has this peer announced it's connection before?
                {
                    string username = usernamelist[clientaddress]; //Search for it's username

                    workerlist.Add(clientaddress, client); //Add this peer to the connection list

                    ConnectedClientsChanged();

                    MessageRecieved(null, null, username + " connected");

                    //Create a thread to handle communication with connected peer
                    Thread clientThread = new Thread(new ParameterizedThreadStart(handleClientComm));
                    clientThread.IsBackground = true;
                    threadList.Add(clientThread);
                    clientThread.Start(clientaddress);

                    Trace.WriteLine("Accepted connection from " + clientaddress + " (" + username + ")");
                }
                else
                {
                    client.Close();
                    Trace.WriteLine("Unknown client " + clientaddress + " tried to connect, because there was no announce; ignored");
                }
            }
        }

        private void handleClientComm(object peer)
        {
            IPAddress address = (IPAddress)peer;

            TcpClient tcpClient = workerlist[address];

            NetworkStream clientStream = tcpClient.GetStream();

            byte[] message = new byte[4096];
            int bytesRead;

            while (true)
            {
                bytesRead = 0;

                try
                {
                    bytesRead = clientStream.Read(message, 0, 4096); //Blocks until a client sends a message
                }
                catch
                {
                    break; //A socket error has occured
                }

                if (bytesRead == 0)
                    break; //The client has disconnected from the server

                //Message has successfully been received
                UTF8Encoding encoder = new UTF8Encoding();
                string msg = encoder.GetString(message, 1, bytesRead - 1);
                if (message[0] == 255)
                    MessageRecieved(usernamelist[address], null, msg);
                else
                    MessageRecieved(usernamelist[address], this.username, msg);
            }

            clientStream.Close(); //Closing the stream
            tcpClient.Close(); //Disconnect from that peer

            MessageRecieved(null, null, usernamelist[address] + " disconnected");
            Trace.WriteLine("Connection to " + usernamelist[address] + " closed");

            workerlist.Remove(address); //Remove that peer from the connection lists
            usernamelist.Remove(address);

            ConnectedClientsChanged();
        }

        public bool sendMessage(string message, string addressstring)
        {
            IPAddress address;
            try
            {
                address = IPAddress.Parse(addressstring);
            }
            catch
            {
                return false;
            }


            if (workerlist.ContainsKey(address))
            {
                UTF8Encoding encoder = new UTF8Encoding();
                byte[] buffer = encoder.GetBytes(message);
                byte[] msg = new byte[buffer.Length + 1];
                buffer.CopyTo(msg, 1);
                msg[0] = 0;

                NetworkStream clientStream = workerlist[address].GetStream();
                clientStream.Write(msg, 0, msg.Length);
                clientStream.Flush();

                MessageRecieved(this.username, usernamelist[address], message);
                return true;
            }
            else
                return false;
        }

        public void sendMessage(string message) //Send a message to all connected peers
        {
            UTF8Encoding encoder = new UTF8Encoding();
            byte[] buffer = encoder.GetBytes(message);
            byte[] msg = new byte[buffer.Length + 1];
            buffer.CopyTo(msg, 1);
            msg[0] = 255;

            foreach (KeyValuePair<IPAddress, TcpClient> peer in workerlist)
            {
                NetworkStream clientStream = peer.Value.GetStream();
                clientStream.Write(msg, 0, msg.Length);
                clientStream.Flush();
            }

            MessageRecieved(this.username, null, message);
        }

        public bool requestConnection(string username)
        {
            if (startListen())
            {
                this.username = username;

                UTF8Encoding encoder = new UTF8Encoding();
                byte[] buffer = encoder.GetBytes(username);
                byte[] message = new byte[buffer.Length + 1];
                buffer.CopyTo(message, 1);
                message[0] = 170;

                udpClient.Send(message, message.Length, IPAddress.Broadcast.ToString(), udpPort);
                Trace.WriteLine("Request with username \"" + username + "\" sent to " + IPAddress.Broadcast.ToString());
            }
            else
                return false;
            MessageRecieved(null, null, "Connected to the network");
            return true;
        }

        public void disconnect()
        {
            foreach (Thread thread in threadList)
            {
                if (thread.IsAlive)
                    thread.Abort();
            }

            foreach (KeyValuePair<IPAddress, TcpClient> peer in workerlist)
            {
                peer.Value.GetStream().Close();
                peer.Value.Close();
            }

            if (udpListenThread != null && udpListenThread.IsAlive)
                udpListenThread.Abort();
            if (udpListenThread != null && tcpListenThread.IsAlive)
                tcpListenThread.Abort();

            tcpListener.Stop();

            if (udpClient != null)
            {
                udpClient.Client.Close();
                udpClient.Close();
                udpClosed = true;
            }

            workerlist.Clear();
            usernamelist.Clear();

            MessageRecieved(null, null, "Disconnected from network");
            ConnectedClientsChanged();
            Trace.WriteLine("Killed all Threads, Lists, TcpClients, UdpClients and stopped the TcpListener");
        }

        private void answerConnection(bool success, IPEndPoint address)
        {
            UTF8Encoding encoder = new UTF8Encoding();
            byte[] buffer = encoder.GetBytes(username);
            byte[] message = new byte[buffer.Length + 1];
            buffer.CopyTo(message, 1);

            if (success)
                message[0] = 255;
            else
                message[0] = 0;

            udpClient.Send(message, message.Length, address);
        }

        private List<IPAddress> localIPAddress()
        {
            List<IPAddress> localIPs = new List<IPAddress>();
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)     //!= AddressFamily.InterNetworkV6
                {
                    localIPs.Add(ip);
                }
            }
            return localIPs;
        }

        private bool checkaddress(IPAddress address)
        {
            List<IPAddress> localaddressList = localIPAddress();

            foreach (IPAddress localaddress in localaddressList)
            {
                if (localaddress.ToString() == address.ToString())
                    return false;
            }
            return true;
        }
    }
}
