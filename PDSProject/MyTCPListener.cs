using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;

namespace PDSProject
{
    static class TestCancellation
    {
        public static async Task<T> WithWaitCancellation<T> (
            this Task<T> task, CancellationToken cancellationToken )
        {
            // The tasck completion source. 
            var tcs = new TaskCompletionSource<bool>();

            // Register with the cancellation token.
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                // If the task waited on is the cancellation token...
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(cancellationToken);
            }

            // Wait for one or the other to complete.
            return await task;
        }
    }


    /// <summary>
    /// Classe che gestisce il TCPListener, ovvero il server.
    /// Per ora client e server sono stati separati, ma se non risulta funzionale è possibile unirli in un'unica classe.
    /// Internamente ha un riferimento a SharedInfo al quale può accedere all'IP dell'utente locale, IP broadcast dell'attuale sottorete e reltive porte.
    /// </summary>
    public class MyTCPListener
    {
        SharedInfo _referenceData;
        long bufferSize = 1024;
        CancellationTokenSource source;
        TcpListener server = null;

        public MyTCPListener () {
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
            try
            {
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


        // Alternativa listener precendente in modo tale che supporti i cambi di rete
        public void ListenerB ()// CancellationToken tokenEndListener )
        {
            while (true)//!tokenEndListener.IsCancellationRequested)
            {
                //source = new CancellationTokenSource();
                //CancellationToken tokenListener = source.Token;

                Console.WriteLine("Wait for change");
                lock (_referenceData.cvListener)
                {
                    while (_referenceData.LocalIPAddress.Equals(""))
                        Monitor.Wait(_referenceData.cvListener);
                }
                Console.WriteLine("Change Listener local IP:" + _referenceData.LocalIPAddress);

                IPAddress localAddr = IPAddress.Parse(_referenceData.LocalIPAddress);
                //TcpListener server = null;

                try
                {
                    //Creo server definendo un oggetto TCPListener con porta ed indirizzo
                    server = new TcpListener(localAddr, _referenceData.TCPPort);
                    server.Start();

                    // Loop ascolto
                    while (true)
                    {
                        Console.WriteLine("Waiting for connection...");
                        TcpClient client = server.AcceptTcpClient();
                        Thread t = new Thread(new ParameterizedThreadStart(ServeClient));
                        t.Start(client);
                        //await ServeClientA(client);
                    }
                }
                catch (SocketException e)
                {
                    Console.WriteLine($"SocketException: {e}");
                }
                catch(Exception e)
                {
                    Console.WriteLine($"Exception: {e}");
                }
                finally
                {
                    if (server != null)
                        server.Stop();
                }

            }
        }

        // Alternativa listener precendente in modo tale che supporti i cambi di rete
        public async Task ListenerA (CancellationToken tokenEndListener) {
            while (!tokenEndListener.IsCancellationRequested) {
                source = new CancellationTokenSource();
                CancellationToken tokenListener = source.Token;

                Console.WriteLine("Wait for change");
                lock (_referenceData.cvListener)
                {
                    while (_referenceData.LocalIPAddress.Equals(""))
                        Monitor.Wait(_referenceData.cvListener);
                }
                Console.WriteLine("Change Listener local IP:" + _referenceData.LocalIPAddress);

                IPAddress localAddr = IPAddress.Parse(_referenceData.LocalIPAddress);
                //TcpListener server = null;

                try
                {
                    //Creo server definendo un oggetto TCPListener con porta ed indirizzo
                    server = new TcpListener(localAddr, _referenceData.TCPPort);
                    server.Start();
                    
                    // Loop ascolto
                    while (!tokenListener.IsCancellationRequested)
                    {
                        Console.WriteLine("Waiting for connection...");
                        TcpClient client = await server.AcceptTcpClientAsync().WithWaitCancellation(tokenListener);
                        //Thread t = new Thread(new ParameterizedThreadStart(ServeClientA));
                        //t.Start(client);
                        await ServeClientA(client);
                    }
                }
                catch (SocketException e)
                {
                    Console.WriteLine($"SocketException: {e}");
                }
                finally
                {
                    if(server!= null)
                    server.Stop();
                }

            }
        }

        public void StopServer ()
        {
            Console.WriteLine("On stop server");
            if (_referenceData.useTask)
                source.Cancel();
            else
            {
                server.Stop();
                server = null;
            }
        }


        public async Task ServeClientA ( Object result)
        {
            // Gestione base problemi rete
            NetworkStream stream = null;
            TcpClient client = null;
            
            try
            {
                client = (TcpClient)result;

                Console.WriteLine($"Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");

                Byte[] bytes = new Byte[bufferSize];
                string data = null;

                stream = client.GetStream();

                int i = 0;
                if ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0)
                {

                    // Il primo pacchetto contiene solo il nome del file e la dimensione del file
                    // TODO: vedere quale carattere di terminazione scegliere, lo spazio potrebbe essere all'interno del nome del file
                    UTF8Encoding encoder = new UTF8Encoding(); // Da cambiare, mettere almeno UTF-16
                    data = encoder.GetString(bytes);

                    // PER ROSSELLA: QUI SCRIVO STAI RICEVENDO FILE/COSE/BANANE!!!
                    Console.WriteLine($"Received {data}");

                    data = data.Replace("\0", string.Empty); // problemi con l'encoder e il valore \0
                    string[] info = data.Split(' ');
                    long dimfile = 0;
                    string file_name = "";
                    if (info[0].Equals("CHNETWORK"))
                        source.Cancel();
                        //Console.WriteLine("On CHNET");
                    else
                    {
                        if (info[0].Equals("CHIMAGE")) {
                            file_name += Utility.PathHost();
                            file_name += /*"puserImage" +*/ "\\" + info[1];
                            dimfile = Convert.ToInt64(info[2]);
                        }
                        else
                        {
                            dimfile = Convert.ToInt64(info[1]);
                            file_name = info[0];
                        }

                        // Crea il file e lo riempie
                        bool isChImage = false;
                        // Crea il file e lo riempie
                        if (File.Exists(file_name)) {
                            if (info[0].Equals("CHIMAGE")) {
                                isChImage = true;
                            } else {
                                string[] splits = file_name.Split('.');
                                splits[splits.Length - 2] += "_Copia";
                                file_name = string.Join(".", splits);
                            }
                        }

                        if (!isChImage) {
                            //TIMER PER CALCOLARE TEMPO RIMANENTE ALLA FINE DEL DOWNLOAD
                            string secondsElapsed = "";
                            Stopwatch stopwatch = new Stopwatch();
                            stopwatch.Start();

                            var file = File.Create(file_name);
                            bytes = new byte[bufferSize * 64];
                            long dataReceived = dimfile; // dimFile = dimensione totale del file , dataReceived = totale dei byte che deve ancora ricevere
                            while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
                                if (dataReceived > 0 && dataReceived < i) //bufferSize)
                                    file.Write(bytes, 0, Convert.ToInt32(dataReceived));
                                else
                                    file.Write(bytes, 0, i);
                                dataReceived -= i;
                                //PROGRESS BAR (BOH) -------------------------------
                                secondsElapsed = stopwatch.Elapsed.TotalSeconds.ToString();
                                string secondElapsedJet = secondsElapsed;
                                double dataReceivedJet = dataReceived/dimfile*100;

                                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.progressFile.SetValue(ProgressBar.ValueProperty, dataReceivedJet);
                                    MainWindow.main.textTime.Text = secondElapsedJet;
                                }));
                                
                            }
                            Console.WriteLine($"File Received {data}");
                            stopwatch.Stop();
                            //secondsElapsed += stopwatch.Elapsed.TotalSeconds;
                            file.Close();
                        }
                        else{
                            Console.WriteLine($"File CHIMAGE already saved " + data );
                            while (stream.Read(bytes, 0, bytes.Length) != 0);
                        }

                        // Avvisa che un'immagine è stata cambiata
                        if (info[0].Equals("CHIMAGE"))
                        {
                            //Salvo info e poi udp reciver aggiornerà le info

                            using (SHA256 sha = SHA256.Create())
                            {
                                FileStream fs = File.OpenRead(file_name);
                                byte[] hash = sha.ComputeHash(fs);
                                string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

                                _referenceData.UserImageChange[hashImage] = file_name;
                                fs.Close();
                            }
                        }
                    }

                }
            }
            catch (SocketException e)
            {
                Console.WriteLine($"SocketException: {e}");
            }
            finally
            {
                stream.Close();
                client.Close();
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
                    file_name += Utility.PathHost();
                    file_name += /*"puserImage" +*/ "\\" + info[1];
                    dimfile = Convert.ToInt64(info[2]);
                }
                else{
                    dimfile = Convert.ToInt64(info[1]);
                    file_name = info[0];
                }

                bool isChImage = false;
                // Crea il file e lo riempie
                if (File.Exists(file_name)) {
                    if (info[0].Equals("CHIMAGE")) {
                        isChImage = true;
                    } else {
                        string[] splits = file_name.Split('.');
                        splits[splits.Length - 2] += "_Copia";
                        file_name = string.Join(".", splits);
                    }
                }

                if (!isChImage) {
                    var file = File.Create(file_name);
                    bytes = new byte[bufferSize * 64];
                    long dataReceived = dimfile;
                    while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
                        if (dataReceived > 0 && dataReceived < i) //bufferSize)
                            file.Write(bytes, 0, Convert.ToInt32(dataReceived));
                        else
                            file.Write(bytes, 0, i);
                        dataReceived -= i;
                    }

                    file.Close();
                }
                else{
                 while (stream.Read(bytes, 0, bytes.Length) != 0);
                }

                // Avvisa che un'immagine è stata cambiata
                if (info[0].Equals("CHIMAGE")){
                    //Salvo info e poi udp reciver aggiornerà le info

                    using (SHA256 sha = SHA256.Create())
                    {
                        FileStream fs = File.OpenRead(file_name);
                        byte[] hash = sha.ComputeHash(fs);
                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

                        _referenceData.UserImageChange[hashImage] = file_name;
                        fs.Close();
                    }
                }
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
            
        }
        catch (SocketException e)
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