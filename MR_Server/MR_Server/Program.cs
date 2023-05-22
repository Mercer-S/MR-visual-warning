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
            string ipAddressServer = "192.168.56.1";
            string udpPort = "9998";
            string tcpPort = "9999";
            
            Console.WriteLine("Initialising...");
            Console.WriteLine("Default IP address: " + ipAddressServer );
            Console.WriteLine("UDP Port: " + ipAddressServer + "; TCP Port: " + tcpPort);
            
            while (true) //change IP
            {
                string userIp = "";
                Console.WriteLine("Please enter your server IP address (press Enter to use default IP): ");
                userIp = Console.ReadLine().Trim();
                if(userIp == "")
                {
                    Console.WriteLine("Ip: " + ipAddressServer);
                    break;
                }

                if (IpCheck(userIp))
                {
                    ipAddressServer = userIp;
                    break;
                }
                Console.WriteLine("Invalid ip address.");
            }

            string serverOpt;
            while (true)
            {
                Console.Write("Which server do you want to activate (TCP/UDP/both): ");
                serverOpt = Console.ReadLine().Trim().ToLower();
                if (serverOpt == "tcp")
                {
                    TcpServer tcpServer = new TcpServer(ipAddressServer, tcpPort);
                    tcpServer.TcpServerStart();
                    ReadConsoleMessage(tcpServer);
                    break;
                }
                else if (serverOpt == "udp")
                {
                    UdpServer udpServer = new UdpServer(ipAddressServer, udpPort);
                    udpServer.UdpServerStart();
                    break;
                }
                else if(serverOpt == "both") 
                {
                    TcpServer tcpServer = new TcpServer(ipAddressServer, tcpPort);
                    tcpServer.TcpServerStart();
                    UdpServer udpServer = new UdpServer(ipAddressServer, udpPort);
                    udpServer.UdpServerStart();
                    ReadConsoleMessage(tcpServer);
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid input.");
                }
            }
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
        private string _ipAddressServer; 
        private string _portServer;
        
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
        
        private List<Socket> _clientProxSocketList = new List<Socket>();

        private byte[] _imageBuffer = new byte[] {0};
        private int _imageLen = 0;

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
                Socket proxSocket = serverSocket.Accept(); //may block current thread, so we use asynchronous.
                PrintConsole(string.Format("Client {0} connected", proxSocket.RemoteEndPoint));
                ServerSend(string.Format("Client {0} connected", proxSocket.RemoteEndPoint));
                _clientProxSocketList.Add(proxSocket);
                
                // put the receiving data function into thread pool
                ThreadPool.QueueUserWorkItem(new WaitCallback(ReceiveData), proxSocket);
            }
        }

        /// <summary>
        /// receive client messages
        /// </summary>
        /// <param name="socket"></param>
        private void ReceiveData(object socket)
        {
            Socket proxSocket = socket as Socket;
            while (true)
            {
                int len = 0;
                byte[] data = new byte[63 * 1024 + 6];
                try
                {
                    len = proxSocket.Receive(data, 0, data.Length, SocketFlags.None);
                    data = data.Take(len).ToArray();
                    //Array.ConstrainedCopy(data, 0, data, 0, len);
                }
                catch (Exception ex)
                {
                    //Exceptional exit
                    PrintConsole("receive: " +
                                 string.Format("Client {0} disconnected", proxSocket.RemoteEndPoint));
                    ServerSend("Client " + proxSocket.RemoteEndPoint + " disconnected");
                    _clientProxSocketList.Remove(proxSocket);
                    StopConnect(proxSocket);
                    return;
                }

                //client exit normally
                if (len <= 0)
                {
                    PrintConsole(
                        "receive: " + string.Format("Client {0} quit", proxSocket.RemoteEndPoint));
                    ServerSend("Client " + proxSocket.RemoteEndPoint + " quit");
                    _clientProxSocketList.Remove(proxSocket);
                    StopConnect(proxSocket);
                    return;
                }
                
                ProcessData(data, proxSocket);
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="proxSocket"></param>
        private void ProcessData(byte[] data, Socket proxSocket)
        {
            int head = Convert.ToUInt16(data[0]) * 100 + Convert.ToUInt16(data[1]) * 10 + Convert.ToUInt16(data[2]);
            int len = Convert.ToUInt16(data[3]) * 256*256 + Convert.ToUInt16(data[4]) * 256 + Convert.ToUInt16(data[5]);
            //HoloLens text message to AI, type = 231
            if (head == 231)
            {
                string msg = Encoding.Default.GetString(data, 6, len); 
                string[] sMsg = msg.Split('$'); 
                PrintConsole("receive: " + 
                                    string.Format("Client {0}: {1} ({2} bytes)", proxSocket.RemoteEndPoint.ToString(), msg, len));
                QuerySend(sMsg[0]);
            }
            
            //python answer message to HoloLens, type = 321 
            else if (head == 321)
            {
                string msg = Encoding.Default.GetString(data, 6, len);
                string[] sMsg = msg.Split('$');
                PrintConsole("receive: " +
                                    string.Format("Client {0}: {1} ({2} bytes)", proxSocket.RemoteEndPoint.ToString(), msg, len));
                AnswerSend(sMsg[0]);
            }
            
            //HoloLens image message to AI, type = 232
            else if (head == 232)
            {
                PrintConsole(string.Format("receive: Photo from {0} ({1} bytes)", proxSocket.RemoteEndPoint.ToString(), len));
                if(len <= data.Length - 6)
                {
                    ForwardPhoto(data);
                }
                else
                {
                    _imageLen = len;
                    byte[] newBuffer = new byte[data.Length - 6]; 
                    Array.ConstrainedCopy(data, 6, newBuffer, 0, (data.Length - 6));
                    _imageBuffer = newBuffer;
                    PrintConsole("(" + _imageBuffer.Length.ToString() + "/" + _imageLen + ") bytes received");
                }
            }
            
            else
            {
                byte[] newBuffer = new byte[data.Length + _imageBuffer.Length]; 
                Array.ConstrainedCopy(this._imageBuffer, 0, newBuffer, 0, _imageBuffer.Length);
                Array.ConstrainedCopy(data, 0, newBuffer, _imageBuffer.Length, data.Length);
                _imageBuffer = newBuffer;
                PrintConsole("(" + _imageBuffer.Length.ToString() + "/" + _imageLen + ") bytes received");
                if (this._imageBuffer.Length >= _imageLen)
                {
                    PrintConsole("photo receiving completed");
                    ForwardPhoto(data);
                }
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
            foreach (var proxSocket in _clientProxSocketList)
            {
                if (proxSocket.Connected)
                {
                    proxSocket.Send(data, 0, data.Length, SocketFlags.None);
                }
            }
        }

        /// <summary>
        /// Forward photo from HoloLens to AI
        /// </summary>
        /// <param name="data"></param>
        private void ForwardPhoto(byte[] data)
        {
            Forward(data,new byte[] {1,3,2});
            PrintConsole("Photo forwarded");
        }
        
        /// <summary>
        /// send text message.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="type">{sender,receiver,datatype},sender/receiver: 0.All, 1.Server, 2.HL, 3.AI. type:1.text, 2.image </param>
        private void Send(string msg, byte[] type)
        {
            foreach (var proxSocket in _clientProxSocketList)
            {
                if (proxSocket.Connected)
                {
                    byte[] MsgByte = Encoding.Default.GetBytes(msg+ "$");
                    byte[] len = {(byte)(MsgByte.Length/65536), (byte)((MsgByte.Length % 65536)/256), (byte)(MsgByte.Length % 256)};
                    byte[] sendBuffer = new byte[type.Length + len.Length + MsgByte.Length];
                    Array.ConstrainedCopy(type, 0, sendBuffer, 0, type.Length);
                    Array.ConstrainedCopy(len, 0, sendBuffer, type.Length, len.Length);
                    Array.ConstrainedCopy(MsgByte, 0, sendBuffer, type.Length + len.Length, MsgByte.Length);
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
            Console.WriteLine("(" + CurrentTimeText + ") UDP: " + string.Format("{0}", txt));
        }
    }
}