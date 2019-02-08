using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PDSProject
{
    /// <summary>
    /// Classe che gestisce il TCPClient.
    /// Per ora client e server sono stati separati, ma se non risulta funzionale è possibile unirli in un'unica classe.
    /// Internamente ha un riferimento a SharedInfo al quale può accedere all'IP dell'utente locale, IP broadcast dell'attuale sottorete e reltive porte.
    /// </summary>
    class MyTCPSender{

        SharedInfo _referenceData;
        const Int32 bufferSize = 1024;

        public MyTCPSender(){
            _referenceData = SharedInfo.Instance;
        }

        /// <summary>
        /// Invia file ad uno o più host.
        /// Per ora invia un singolo file ad un singolo host.
        /// TODO: da fare gestiore di più file e verso più host
        /// </summary>
        /// <param name="message">Nome del file (path assoluto)</param>
        /// TODO: modificare la roba del path, da fare funzioncina che separa ed ottine solo il nome del file
        public void Send(string filename){
            // TODO: da cambiare!!!
            IPAddress serverAddr;
            if (!_referenceData.hasChangedProfileImage) {
                if (_referenceData.selectedHost.Equals("")) return;
                serverAddr = IPAddress.Parse(_referenceData.selectedHost);
            }
            else{
               serverAddr = IPAddress.Parse(_referenceData.Users.First().Key);//"192.168.1.69");
            }
            TcpClient client = null;

            try
            {
                client = new TcpClient(serverAddr.ToString(), _referenceData.TCPPort);

                UTF8Encoding encoder = new UTF8Encoding();
                FileStream file = File.OpenRead(filename);//"Risultati.pdf");

                // Invio primo pacchetto con nome e dimensione
                // TODO: vedere altro carattere di separazione che non sia lo spazio, potrebbe essere usato dentro il file
                long dim = file.Length;
                string firstmsg = "";

                // Da cambiare
                if (_referenceData.hasChangedProfileImage) {
                    firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)
                    _referenceData.hasChangedProfileImage = false;
                }

                string[] infoImage = filename.Split(new string[] { "\\" }, StringSplitOptions.None);
                firstmsg += infoImage[infoImage.Length - 1] + " " + dim;

                /* 
                 * Questa roba merita purtroppo 2 parole:
                 * Il primo pacchetto della sequenza ha solo nome + dimensione e poi è rimempito di byte a caso.
                 * Questo perchè a volte il pacchetto iniziale era vuoto e a volte aveva l'inizio del file.
                 * Per evitare casini ho fatto la cosa più stupida. Se si possono trovare altre soluzioni sono ben accette
                 */
                byte[] bytes = new byte[bufferSize];
                encoder.GetBytes(firstmsg).CopyTo(bytes, 0);
                Random rand = new Random();
                for (int i = 0; i < (bufferSize - encoder.GetByteCount(firstmsg)); i++){
                    byte b = 1;
                    bytes.Append(b);
                }
                NetworkStream stream = client.GetStream();
                stream.Write(bytes, 0, bufferSize);

                // Invio effettivo del file
                long numbPackets = dim / bufferSize;
                for (int i = 0; i <= numbPackets; i++){
                    bytes = new byte[bufferSize];
                    file.Read(bytes, 0, bytes.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
                file.Close();
                stream.Close();

                // OLD CODE
                /*Byte[] bytes = encoder.GetBytes(message);

                NetworkStream stream = client.GetStream();

                stream.Write(bytes, 0, bytes.Length);
                Console.WriteLine($"Send {message} to 192.168.1.69");
                stream.Close();
                client.Close();
                */
            }
            catch (SocketException e){
                Console.WriteLine($"SocketException: {e}");
            }
            finally{
                client.Close();
            }
        }
    }
}
