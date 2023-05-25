using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;

namespace MR_Photo_Receiver
{
    class Program
    {
        public static void Main(string[] args)
        {
            
            Console.WriteLine("Please enter the server ip.");
            string ip = Console.ReadLine();
            Console.WriteLine("Please enter your photo folder path.(e.g: E:/photos)");
            string folderPath = Console.ReadLine();
            Client client = new Client(ip, folderPath);
            
            Console.WriteLine("Enter \"exit\" to exit.");
            while (true)
            {
                String str = Console.ReadLine();
                if (str == "exit")
                {
                    client.DisConnect();
                    break;
                }
            }
        }
    }
    public class Client
    {
        string FolderPath = "E:/photos";
        int port = 9999;
        Socket socket;
        private IPEndPoint remoteEP;
        int photoNumber = 0;
        
        private byte[] _imageBuffer = new byte[] {0};
        private int _imageLen = 0;

        public Client(string ip, string path)
        {
            remoteEP = new IPEndPoint(IPAddress.Parse(ip), port);
            FolderPath = path;
            CreateClient();
        }
        public void CreateClient()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Connect(remoteEP);
            }
            catch
            {
                Console.WriteLine("TCP socket: Wrong IP address and/or Port.");
                return;
            }
            PrintConsole("TCP Socket connected.");
            ThreadPool.QueueUserWorkItem(new WaitCallback(ReceiveData), socket);
        }

        void ReceiveData(Object socket)
        {
            Socket serverSocket = socket as Socket;
            while (true)
            {
                int len = 0;
                byte[] data = new byte[64 * 1024 + 6];
                try
                {
                    len = serverSocket.Receive(data, 0, data.Length, SocketFlags.None);
                    data = data.Take(len).ToArray();
                    //Array.ConstrainedCopy(data, 0, data, 0, len);
                }
                catch (Exception ex)
                {
                    PrintConsole("error2");
                    return;
                }
                
                ProcessData(data, serverSocket);
            }
        }

        void ProcessData(byte[] data, Socket socket)
        {
            int head = Convert.ToUInt16(data[0]) * 100 + Convert.ToUInt16(data[1]) * 10 + Convert.ToUInt16(data[2]);
            int len = Convert.ToUInt16(data[3]) * 256*256 + Convert.ToUInt16(data[4]) * 256 + Convert.ToUInt16(data[5]);
            if (head == 101)
                ;
            else if (head == 231)
                ;
            else if (head == 321)
                ;
            else if (head == 232)
            {
                PrintConsole(string.Format("receive: Photo from {0} ({1} bytes)", socket.RemoteEndPoint.ToString(), len));
                if(len <= data.Length - 6)
                {
                    SavePhoto(data);
                }
                else
                {
                    _imageLen = len;
                    byte[] newBuffer = new byte[data.Length - 6]; 
                    Array.ConstrainedCopy(data, 6, newBuffer, 0, (data.Length - 6));
                    _imageBuffer = newBuffer;
                    PrintConsole("(" + _imageBuffer.Length.ToString() + "/" + _imageLen + ") bytes received, " +
                                 "whole package length: " + data.Length + " bytes");
                }
            }
            else
            {
                byte[] newBuffer = new byte[data.Length + _imageBuffer.Length]; 
                Array.ConstrainedCopy(this._imageBuffer, 0, newBuffer, 0, _imageBuffer.Length);
                Array.ConstrainedCopy(data, 0, newBuffer, _imageBuffer.Length, data.Length);
                _imageBuffer = newBuffer;
                PrintConsole("(" + _imageBuffer.Length.ToString() + "/" + _imageLen + ") bytes received, " +
                             "whole package length: " + data.Length+ " bytes");
                if (this._imageBuffer.Length >= _imageLen)
                {
                    PrintConsole("photo receiving completed");
                    SavePhoto(_imageBuffer);
                }
            }
        }

        public void DisConnect()
        {
            if (socket != null)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException e)
                {
                }
                finally
                {
                    //m_socket.Close();
                }
            }
        }

        private void SavePhoto(byte[] PhotoData)
        {
            string filePath = FolderPath + "/HoloLens-" + photoNumber + ".jpg";
            File.WriteAllBytes(filePath, PhotoData);
            PrintConsole(filePath + " export finished");
            photoNumber++;
        }
        
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
}