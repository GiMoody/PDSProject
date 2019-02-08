using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PDSProject
{
    /// <summary>
    /// Classe che gestisce il TCPListener, ovvero il server.
    /// Per ora client e server sono stati separati, ma se non risulta funzionale è possibile unirli in un'unica classe.
    /// Internamente ha un riferimento a SharedInfo al quale può accedere all'IP dell'utente locale, IP broadcast dell'attuale sottorete e reltive porte.
    /// </summary>
    public class MyTCPListener
    {
        SharedInfo _referenceData;
        long bufferSize = 1024;

        public MyTCPListener() {
            _referenceData = SharedInfo.Instance;
        }

        /// <summary>
        /// Funzione che avvia il server, viene eseguito in un thread secondario che rimane in esecuzione fino alla fine del programma.
        /// Per ogni TCPClient viene creato un nuovo thread che lo gestisce (pensare caso troppi thread in esecuzione? evito eccessivo context switching)
        /// TODO: cercare quanti thread fisici la CPU riesce a gestire in contemporanea
        /// TODO: gestire correttamente gli accessi concorrenti alle risorse e la terminazione corretta del thread (adesso non gestita)
        /// </summary>
        public void Listener(){
            // Caratteristiche base per tcp lister: porta ed indirizzo
            IPAddress localAddr = IPAddress.Parse(_referenceData.LocalIPAddress);
            TcpListener server = null;
            
            try{
                //Creo server definendo un oggetto TCPListener con porta ed indirizzo
                server = new TcpListener(localAddr, _referenceData.TCPPort);
                server.Start();

                //Creo buffer su cui scrivere dati (in questo caso byte generici)
                Byte[] bytes = new Byte[256];

                // Loop ascolto
                while (true)
                {
                    Console.WriteLine("Waiting for connection...");
                    TcpClient client = server.AcceptTcpClient();
                    Thread t = new Thread(new ParameterizedThreadStart(ServeClient));
                    t.Start(client);
                }
            }
            catch (SocketException e){
                Console.WriteLine($"SocketException: {e}");
            }
            finally{
                server.Stop();
            }
        }

        /// <summary>
        /// Metodo chiamato da un thread secondario che gestisce il client connesso.
        /// Riceve un file e lo salva nel file system, nel caso esista già per ora lo sovrascrive e non fa nessun controllo in caso non esista o di problemi di rete.
        /// TODO: vedere caso di file con lo stesso nome e come gestirli, gestire le varie casistiche di congestione di rete etc...
        /// </summary>
        /// <param name="result">Oggetto TCPClient connesso al server TCPListener corrente</param>
        public void ServeClient(Object result){
            // Gestione base problemi rete
            NetworkStream stream = null;
            TcpClient client = null;
            try
            {
                client = (TcpClient)result;

                Console.WriteLine($"Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");

                //Da rivedere. Di base permette di richiamare la finestra principale e di accedere al Dispatcher ma non so quanto corretto
                MainWindow.main.textCheckConnection.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { MainWindow.main.textCheckConnection.Text = "CONNENCTED"; }));
                MainWindow.main.textCheckConnection.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal, new Action(() =>
                    {
                        MainWindow.main.textCheckConnection.Text = $"Client connected!";
                    })
                );

                Byte[] bytes = new Byte[bufferSize];
                string data = null;

                stream = client.GetStream();

                int i = 0;
                if ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {

                    // Il primo pacchetto contiene solo il nome del file e la dimensione del file
                    // TODO: vedere quale carattere di terminazione scegliere, lo spazio potrebbe essere all'interno del nome del file
                    UTF8Encoding encoder = new UTF8Encoding(); // Da cambiare, mettere almeno UTF-16
                    data = encoder.GetString(bytes);

                    Console.WriteLine($"Received {data}");

                    data = data.Replace("\0", string.Empty); // problemi con l'encoder e il valore \0
                    string[] info = data.Split(' ');
                    long dimfile = 0; 
                    string file_name = "";
                    if (info[0].Equals("CHIMAGE")){
                        file_name += "puserImage" + info[1];
                        dimfile = Convert.ToInt64(info[2]);
                    }
                    else{
                        dimfile = Convert.ToInt64(info[1]);
                        file_name = info[0];
                    }

                    // Crea il file e lo riempie
                    var file = File.Create(file_name);
                    long dataReceived = 0;
                    while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived <= dimfile)
                    {
                        file.Write(bytes, 0, i);
                        dataReceived += i;
                    }
                    file.Close();

                    // Avvisa che un'immagine è stata cambiata
                    if (info[0].Equals("CHIMAGE")){
                        //Salvo info e poi udp reciver aggiornerà le info

                        using (SHA256 sha = SHA256.Create())
                        {
                            FileStream fs = File.OpenRead(file_name);
                            byte[] hash = sha.ComputeHash(fs);
                            string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);
                            string[] infoImage = file_name.Split(new string[] { "\\" }, StringSplitOptions.None);
                            _referenceData.UserImageChange[hashImage] = infoImage[infoImage.Length-1];
                            fs.Close();
                        }
                    }
                    file.Close();
                }
                /*
                // CODICE VECCHIO, tengo per sicurezza // 
                while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    UTF8Encoding encoder = new UTF8Encoding();
                    data = encoder.GetString(bytes);
                    Console.WriteLine($"Received {data}");

                    data = data.Replace("\0", string.Empty);

                    MainWindow.main.textInfoMessage.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.textInfoMessage.Text = $"Received {data}";
                    }));

                }
                Thread.Sleep(2000);
                */
                //Da rivedere
                MainWindow.main.textCheckConnection.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { MainWindow.main.textCheckConnection.Text = "NOT CONNENCTED"; }));
                MainWindow.main.textCheckConnection.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal, new Action(() =>
                    {
                        MainWindow.main.textCheckConnection.Text = $"Client disconnected!";
                    })
                );
            }
            catch (Exception e)
            {
                Console.WriteLine($"SocketException: {e}");
            }
            finally {
                stream.Close();
                client.Close();
            }
        }
    }
}
