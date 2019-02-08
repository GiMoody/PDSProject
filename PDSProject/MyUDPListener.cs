﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PDSProject
{
    /// <summary>
    /// Classe che gestisce l'UDPListener, ovvero il server.
    /// Sta in ascolto per ricevere profilo dei vari utenti connessi alla rete locale.
    /// </summary>
    class MyUDPListener
    {
        SharedInfo _referenceData;

        public MyUDPListener(){
            _referenceData = SharedInfo.Instance;
        }

        /// <summary>
        /// Si mette in ascolto di un pachetto UDP
        /// </summary>
        public void Listener(){
            UdpClient receiver = new UdpClient(_referenceData.UDPReceivedPort);
            while (true)
            {
                IPEndPoint receivedIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

                Byte[] receivedBytes = receiver.Receive(ref receivedIpEndPoint);
                MemoryStream stream = new MemoryStream(receivedBytes);
                stream.Position = 0;
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Host));
                Host received = (Host)ser.ReadObject(stream);

                // Controlla che il mittente non siamo noi
                if (receivedIpEndPoint.Address.ToString().Equals(_referenceData.LocalIPAddress)) continue;

                // Se l'utente è già presente all'interno della struttura dati e ha dati diversi, aggiorno
                if (_referenceData.Users.ContainsKey(receivedIpEndPoint.Address.ToString())){
                    if (!_referenceData.Users[receivedIpEndPoint.Address.ToString()].Equals(received)){
                        _referenceData.Users[receivedIpEndPoint.Address.ToString()] = received;

                        //TODO: da riorganizzare bene che è un po' confusionario

                        if (_referenceData.UserImageChange.ContainsKey(received.ProfileImageHash)) {
                            //Da aggiornare immagine profilo
                            _referenceData.Users[receivedIpEndPoint.Address.ToString()].ProfileImagePath = _referenceData.UserImageChange[received.ProfileImageHash];
                        }
                        string ip = receivedIpEndPoint.Address.ToString();
                        MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.UpdateProfileHost(ip);
                        }));

                    }
                }
                else{
                    // Se invece non esiste, viene inserito
                    _referenceData.Users[receivedIpEndPoint.Address.ToString()] = received;
                    if (_referenceData.UserImageChange.ContainsKey(received.ProfileImageHash))
                    {
                        //Da aggiornare immagine profilo
                        _referenceData.Users[receivedIpEndPoint.Address.ToString()].ProfileImagePath = _referenceData.UserImageChange[received.ProfileImageHash];
                    }
                    string ip = receivedIpEndPoint.Address.ToString();
                    MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.UpdateProfileHost(ip);
                    }));
                }
            }
        }

        // Non usato, riceve il dato in maniera asincrona 
        private static void DataReceived(IAsyncResult ar)
        {
            UdpClient c = (UdpClient)ar.AsyncState;
            IPEndPoint receivedIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
            Byte[] receivedBytes = c.EndReceive(ar, ref receivedIpEndPoint);

            // Convert data to ASCII and print in console
            string receivedText = ASCIIEncoding.ASCII.GetString(receivedBytes);
            Console.Write(receivedIpEndPoint + ": " + receivedText + Environment.NewLine);

            // Restart listening for udp data packages
            c.BeginReceive(DataReceived, ar.AsyncState);
        }
    }
}
