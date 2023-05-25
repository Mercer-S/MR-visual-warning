using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MR_Server
{
    class Program
    {
        static void Main(string[] args)
        {
            string ipAddressServer = "172.31.19.217";
            string udpPort = "9998";
            string tcpPort = "9999";
            
            Console.WriteLine("Initialising...");
            Console.WriteLine("Default IP address: " + ipAddressServer );
            Console.WriteLine("UDP Port: " + udpPort + "; TCP Port: " + tcpPort);
            
            while (true) //change IP
            {
                Console.WriteLine("Please enter your server IP address (press Enter to use the default IP): ");
                string userIp = Console.ReadLine().Trim();
                if(userIp == "")
                {
                    Console.WriteLine("Using default Ip: " + ipAddressServer);
                    break;
                }

                if (IpCheck(userIp))
                {
                    ipAddressServer = userIp;
                    break;
                }
                Console.WriteLine("Invalid ip address.");
            }
            
            TcpServer tcpServer = new TcpServer(ipAddressServer, tcpPort);
            UdpServer udpServer = new UdpServer(ipAddressServer, udpPort);
            
            while (true)
            {
                Console.Write("Which server do you want to activate (TCP/UDP/both): ");
                string serverOpt = Console.ReadLine().Trim().ToLower();
                if (serverOpt == "tcp")
                {
                    tcpServer.TcpServerStart();
                    break;
                }
                else if (serverOpt == "udp")
                {
                    udpServer.UdpServerStart();
                    break;
                }
                else if(serverOpt == "both" || serverOpt == "") 
                {
                    tcpServer.TcpServerStart();
                    udpServer.UdpServerStart();
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid input.");
                }
            }

            Thread.Sleep(1000);
            ReadConsoleMessage(tcpServer);
            
        }

        private static void ReadConsoleMessage(TcpServer tcpServer)
        {
            Console.WriteLine("Enter \"exit server\" to exit.");
            String str = "";
            while (true) //send server message
            {
                try
                {
                    str = Console.ReadLine();
                }
                finally { }

                if (str == "exit server")
                    break;
                else if (str == "") { }
                else
                {
                    tcpServer.ServerSend(str);
                    str = "";
                }
                
            }
        }
        private static bool IpCheck(string ip)
        {
            Regex regex = new Regex("^[0-9]{1,3}.[0-9]{1,3}.[0-9]{1,3}.[0-9]{1,3}$");
            if (!regex.IsMatch(ip))
                return false;
            
            string[] spIp = ip.Split(new char[] { '.' });
            if (spIp.Length != 4)
                return false;
            foreach(string num in spIp)
            {
                if (Convert.ToInt16(num) > 255)
                {
                    return false;
                }
            }
            return true;
        }
    }
    
    /// <summary>
    /// The TcpServer is for communication
    /// </summary>
    public class TcpServer
    {
        //define server IP and Port. The port should be accessible. 
        private readonly string _ipAddressServer; 
        private readonly string _portServer;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ipAddressServer"></param>
        /// <param name="portServer"></param>
        public TcpServer(string ipAddressServer, string portServer)
        { 
            _ipAddressServer = ipAddressServer; 
            _portServer = portServer;
        }

        /// <summary>
        /// Start the TCP server 
        /// </summary>
        /// <returns></returns>
        public void TcpServerStart()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                serverSocket.Bind(new IPEndPoint(IPAddress.Parse(_ipAddressServer), int.Parse(_portServer)));
            }
            catch
            {
                Console.WriteLine("TCP socket: Wrong IP address and/or Port.");
                return;
            }
            serverSocket.Listen(10); //listen 10 connecting requirement at the same time. 
            PrintConsole("TCP Socket generated.");
            
            // put the receiving connections function into thread pool
            ThreadPool.QueueUserWorkItem(new WaitCallback(AcceptClientConnect), serverSocket);
        }

        private List<Socket> _clientProxSocketList = new List<Socket>();
        
        /// <summary>
        /// Accept client connection requirement.
        /// </summary>
        /// <param name="socket">A tcp socket created for communication</param>
        private void AcceptClientConnect(object socket)
        {
            Socket serverSocket = socket as Socket; //back to Socket type  

            PrintConsole("TCP socket start receiving.");

            while (true) //keep receiving connections
            {
                Socket clientSocket = serverSocket.Accept(); //may block current thread, so we use asynchronous.
                PrintConsole(string.Format("Client {0} connected", clientSocket.RemoteEndPoint));
                ServerSend(string.Format("Client {0} connected", clientSocket.RemoteEndPoint));
                _clientProxSocketList.Add(clientSocket);
                
                // put the receiving data function into thread pool
                ThreadPool.QueueUserWorkItem(new WaitCallback(ReceiveData), clientSocket);
            }
        }

        /// <summary>
        /// receive client messages
        /// </summary>
        /// <param name="socket"></param>
        private void ReceiveData(object socket)
        {
            Socket clientSocket = socket as Socket;
            while (true)
            {
                int len = 0;
                byte[] receiveBuffer = new byte[63 * 1024 + 6];
                try
                {
                    len = clientSocket.Receive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None);
                    receiveBuffer = receiveBuffer.Take(len).ToArray();
                    //Array.ConstrainedCopy(data, 0, data, 0, len);
                }
                catch (Exception ex)
                {
                    //Exceptional exit
                    PrintConsole($"receive: Client {clientSocket.RemoteEndPoint} disconnected");
                    ServerSend($"Client {clientSocket.RemoteEndPoint} disconnected");
                    _clientProxSocketList.Remove(clientSocket);
                    StopConnect(clientSocket);
                    return;
                }

                //client exit normally
                if (len <= 0)
                {
                    PrintConsole($"receive: Client {clientSocket.RemoteEndPoint} quit");
                    ServerSend($"Client {clientSocket.RemoteEndPoint} quit");
                    _clientProxSocketList.Remove(clientSocket);
                    StopConnect(clientSocket);
                    return;
                }
                
                DataPreprocess(receiveBuffer, clientSocket);
            }
        }

        byte[] _continueReceivingBuffer = Array.Empty<byte>();
        int _continueReceivingLen = 0;
        
        private void DataPreprocess(byte[] data, Socket proxSocket)
        {
            int head = Convert.ToUInt16(data[0]) * 100 + Convert.ToUInt16(data[1]) * 10 + Convert.ToUInt16(data[2]);
            int len = Convert.ToUInt16(data[3]) * 256*256 + Convert.ToUInt16(data[4]) * 256 + Convert.ToUInt16(data[5]);
            if (head == 101 || head == 231 || head == 321 || head == 232)
            {
                _continueReceivingBuffer = Array.Empty<byte>();
                _continueReceivingLen = 0;
                if (len <= data.Length - 6) // finished package
                {
                    ProcessData(data, proxSocket);
                    return;
                }
                else // unfinished package
                {
                    _continueReceivingBuffer = data;
                    _continueReceivingLen = len;
                    PrintConsole($"Unfinished package received, type: {head}, length: {len}");
                    return;
                }
            }
            else if (_continueReceivingLen > 0)
            {
                byte[] tempBuffer = new byte[data.Length + _continueReceivingBuffer.Length]; 
                Array.ConstrainedCopy(_continueReceivingBuffer, 0, tempBuffer, 0, _continueReceivingBuffer.Length);
                Array.ConstrainedCopy(data, 0, tempBuffer, _continueReceivingBuffer.Length, data.Length);
                _continueReceivingBuffer = tempBuffer;
                
                PrintConsole("(" + _continueReceivingBuffer.Length.ToString() + "/" + _continueReceivingLen + ") bytes received");
                
                if (_continueReceivingBuffer.Length == _continueReceivingLen + 6)
                {
                    PrintConsole("Data receiving completed");
                    ProcessData(_continueReceivingBuffer, proxSocket);
                }
                if (_continueReceivingBuffer.Length > _continueReceivingLen + 6)
                {
                    PrintConsole("Data receiving completed");
                    byte[] wholePackage = new byte[_continueReceivingLen + 6]; 
                    Array.ConstrainedCopy(_continueReceivingBuffer, 0, wholePackage, 0, wholePackage.Length);
                    ProcessData(wholePackage, proxSocket);

                    byte[] anotherPackage = new byte[_continueReceivingBuffer.Length - (_continueReceivingLen + 6)]; 
                    Array.ConstrainedCopy(_continueReceivingBuffer, (_continueReceivingLen + 6), anotherPackage, 0, anotherPackage.Length);
                    DataPreprocess(anotherPackage, proxSocket);
                }
            }
        }

        /// <summary>
        /// process the received data
        /// </summary>
        /// <param name="data">the received data</param>
        /// <param name="proxSocket">client socket</param>
        private void ProcessData(byte[] data, Socket proxSocket)
        {
            int head = Convert.ToUInt16(data[0]) * 100 + Convert.ToUInt16(data[1]) * 10 + Convert.ToUInt16(data[2]);
            int len = Convert.ToUInt16(data[3]) * 256*256 + Convert.ToUInt16(data[4]) * 256 + Convert.ToUInt16(data[5]);

            //Server text message to All, type = 101
            if (head == 101)
            {
                
            }
            
            //HoloLens text message to AI, type = 231
            else if (head == 231)
            {
                string msg = Encoding.Default.GetString(data, 6, data.Length - 6); 
                string[] sMsg = msg.Split('$'); 
                PrintConsole($"receive: Client {proxSocket.RemoteEndPoint}: {msg} ({len} bytes)");
                QuerySend(sMsg[0]);
            }
            
            //python answer message to HoloLens, type = 321 
            else if (head == 321)
            {
                string msg = Encoding.Default.GetString(data, 6, data.Length - 6);
                string[] sMsg = msg.Split('$');
                PrintConsole($"receive: Client {proxSocket.RemoteEndPoint}: {msg} ({len} bytes)");
                AnswerSend(sMsg[0]);
            }
            
            //HoloLens image message to AI, type = 232
            else if (head == 232)
            {
                PrintConsole($"receive: Photo from {proxSocket.RemoteEndPoint} ({len} bytes)");

                byte[] tempBuffer = new byte[data.Length - 6]; 
                Array.ConstrainedCopy(data, 6, tempBuffer, 0, tempBuffer.Length);
                ForwardPhoto(tempBuffer);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="proxSocket"></param>
        private void StopConnect(Socket proxSocket)
        {
            try
            {
                if (proxSocket.Connected)
                {
                    proxSocket.Shutdown(SocketShutdown.Both);
                    proxSocket.Close(100); //close the proxSocket after 100s if the socket cannot be closed normally.
                }
            }
            catch (Exception ex)
            {
        
            }
        }
        
        /// <summary>
        /// broadcast non-text message.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="type">{sender,receiver,datatype},sender/receiver: 0.All, 1.Server, 2.HL, 3.AI. type:1.text, 2.image </param>
        private void Forward(byte[] data, byte[] type)
        {
            byte[] len = {(byte)(data.Length/65536), (byte)((data.Length % 65536)/256), (byte)(data.Length % 256)};
            byte[] sendBuffer = new byte[type.Length + len.Length + data.Length];
            Array.ConstrainedCopy(type, 0, sendBuffer, 0, type.Length);
            Array.ConstrainedCopy(len, 0, sendBuffer, type.Length, len.Length);
            Array.ConstrainedCopy(data, 0, sendBuffer, type.Length + len.Length, data.Length);
            PrintConsole($"{data.Length} bytes data forwarded, whole package length: {sendBuffer.Length} bytes");
            foreach (var clientSocket in _clientProxSocketList)
            {
                if (clientSocket.Connected)
                {
                    clientSocket.Send(sendBuffer, 0, sendBuffer.Length, SocketFlags.None);
                }
            }
        }

        /// <summary>
        /// Forward photo from HoloLens to AI,232
        /// </summary>
        /// <param name="data"></param>
        private void ForwardPhoto(byte[] data)
        {
            Forward(data,new byte[] {2,3,2});
            PrintConsole("Photo forwarded");
        }
        
        /// <summary>
        /// send text message.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="type">{sender,receiver,datatype},sender/receiver: 0.All, 1.Server, 2.HL, 3.AI. type:1.text, 2.image </param>
        private void Send(string msg, byte[] type)
        {
            byte[] msgByte = Encoding.Default.GetBytes(msg+ "$");
            byte[] len = {(byte)(msgByte.Length/65536), (byte)((msgByte.Length % 65536)/256), (byte)(msgByte.Length % 256)};
            byte[] sendBuffer = new byte[type.Length + len.Length + msgByte.Length];
            Array.ConstrainedCopy(type, 0, sendBuffer, 0, type.Length);
            Array.ConstrainedCopy(len, 0, sendBuffer, type.Length, len.Length);
            Array.ConstrainedCopy(msgByte, 0, sendBuffer, type.Length + len.Length, msgByte.Length);
            foreach (var proxSocket in _clientProxSocketList)
            {
                if (proxSocket.Connected)
                {
                    proxSocket.Send(sendBuffer, 0, sendBuffer.Length, SocketFlags.None);
                }
            }
        }

        /// <summary>
        /// send message, type:101, format: "Server: \msg$"
        /// </summary>
        /// <param name="msg">text message to be sent</param>
        public void ServerSend(string msg)
        {
            Send(msg, new byte[] {1,0,1});//sender=1.Server, receiver=0.All, type=1.Text 
            PrintConsole("*system message \"" + string.Format(msg) + "\" sent");
        }

        /// <summary>
        /// send message for query, type:231, format: "msg$"
        /// </summary>
        /// <param name="msg"></param>
        private void QuerySend(string msg)
        {
            PrintConsole("send: " + string.Format("Query:" + msg));
            Send(msg, new byte[] {2,3,1});//sender=1.Server, receiver=3.AI, type=1.Text
        }
        
        /// <summary>
        /// send message for answering query, type:321, format: "msg$"
        /// </summary>
        /// <param name="msg"></param>
        private void AnswerSend(string msg)
        {
            PrintConsole("send: " + string.Format("Answer:" + msg));
            Send(msg, new byte[] {3,2,1});//sender=1.Server, receiver=3.AI, type=1.Text
        }
        
        /// <summary>
        /// print text on console 
        /// </summary>
        /// <param name="txt"></param>
        private void PrintConsole(string txt)
        {
            string hour = DateTime.Now.Hour.ToString().PadLeft(2, '0');
            string min = DateTime.Now.Minute.ToString().PadLeft(2, '0');
            string sec = DateTime.Now.Second.ToString().PadLeft(2, '0');
            //string milisec = DateTime.Now.Millisecond.ToString().PadLeft(3, '0');
            string CurrentTimeText = string.Format("{0:D2}:{1:D2}:{2:D2}", hour, min, sec);
            Console.WriteLine("(" + CurrentTimeText + ")" + string.Format("{0}", txt));
        }
    }

    public class UdpServer
    {
        private EndPoint _endPoint;

        /// <summary>
        /// define server IP and Port. The port should be accessible.
        /// </summary>
        /// <param name="ipAddressServer"></param>
        /// <param name="portServer"></param>
        public UdpServer(string ipAddressServer, string portServer)
        {
            _endPoint = new IPEndPoint(IPAddress.Parse(ipAddressServer), int.Parse(portServer));
        }

        /// <summary>
        /// Start the UDP server 
        /// </summary>
        /// <returns></returns>
        public void UdpServerStart()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            
            try
            {
                serverSocket.Bind(_endPoint);
            }
            catch
            {
                Console.WriteLine("UDP socket: Wrong IP address and/or Port.");
                return;
            }

            ThreadPool.QueueUserWorkItem(new WaitCallback(ReceiveData), serverSocket);
        }

        /// <summary>
        /// receive data from Object tracking modules
        /// </summary>
        /// <param name="socket"></param>
        void ReceiveData(object socket)
        {
            Socket proxSocket = socket as Socket;
            PrintConsole("UDP Socket generated.");
            while (true)
            {
                byte[] buffer = new byte[64];
                int len = proxSocket.ReceiveFrom(buffer, SocketFlags.None, ref _endPoint);
                string data = Encoding.Default.GetString(buffer, 0, len);
                PrintConsole(data);
                ForwardData(data, proxSocket);
            }
        }
        
        /// <summary>
        /// forward data to HoloLens
        /// </summary>
        /// <param name="data"></param>
        /// <param name="socket"></param>
        void ForwardData(string data, Socket socket)
        {
            byte[] btData = Encoding.Default.GetBytes(data + "$");
            socket.SendTo(btData, 0, btData.Length, SocketFlags.None, _endPoint);
        }
        
        /// <summary>
        /// print texts to the console
        /// </summary>
        /// <param name="txt"></param>
        private void PrintConsole(string txt)
        {
            string hour = DateTime.Now.Hour.ToString().PadLeft(2, '0');
            string min = DateTime.Now.Minute.ToString().PadLeft(2, '0');
            string sec = DateTime.Now.Second.ToString().PadLeft(2, '0');
            //string milisec = DateTime.Now.Millisecond.ToString().PadLeft(3, '0');
            string CurrentTimeText = string.Format("{0:D2}:{1:D2}:{2:D2}", hour, min, sec);
            Console.WriteLine("(" + CurrentTimeText + ")UDP: " + string.Format("{0}", txt));
        }
    }
}