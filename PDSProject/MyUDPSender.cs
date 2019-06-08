using System;

using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;

namespace PDSProject
{
    /// <summary>
    /// Class that manage a UDP Client
    /// </summary>
    class MyUDPSender {
        SharedInfo _referenceData;

        public MyUDPSender(){
            _referenceData = SharedInfo.Instance;
        }

        public void Sender(string multicastAddr){
            UdpClient sender = new UdpClient();

            // It serialize the file content and send it
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Host));
            try
            {
                MemoryStream ms = new MemoryStream();
                ser.WriteObject(ms, _referenceData.GetInfoLocalUser().ConvertToHost());
                ms.Position = 0;
                byte[] buffer = ms.ToArray();
                sender.Send(buffer, buffer.Length, multicastAddr, SharedInfo.UDPReceivedPort);
                sender.Close();
            }
            catch (Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPSender Exception - {e.GetType()} {e.Message}");
            }
            finally {
                sender.Close();
            }
        }
    }
}