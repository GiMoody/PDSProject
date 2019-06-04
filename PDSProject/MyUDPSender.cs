using System;

using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;

namespace PDSProject
{
    /// <summary>
    /// Classe che gestice il client UDP
    /// Internamente ha solo una classe che invia un messaggio UDP contenente le informazioni del profilo utente.
    /// </summary>
    class MyUDPSender {
        SharedInfo _referenceData;

        public MyUDPSender(){
            _referenceData = SharedInfo.Instance;
        }

        public void Sender(string multicastAddr){
            UdpClient sender = new UdpClient();

            // Serializza il contenuto e lo invia
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Host));
            try
            {
                MemoryStream ms = new MemoryStream();
                ser.WriteObject(ms, _referenceData.GetInfoLocalUser().ConvertToHost());
                ms.Position = 0;
                byte[] buffer = ms.ToArray();
                sender.Send(buffer, buffer.Length, multicastAddr, _referenceData.UDPReceivedPort);
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