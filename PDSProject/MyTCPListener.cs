using System;

using System.IO;
using System.IO.Compression;

using System.Net;
using System.Net.Sockets;

using System.Threading;
using System.Threading.Tasks;

using System.Windows.Controls;
using System.Windows.Threading;

using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Linq;


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
        const long bufferSize = 1024;
        CancellationTokenSource source;

        TcpListener server = null;

        SemaphoreSlim semaphoreForFile = new SemaphoreSlim(1,1);
        SemaphoreSlim semaphoreProfileImage = new SemaphoreSlim(1,1);
        SemaphoreSlim obj = new SemaphoreSlim(0);


        public MyTCPListener () {
            _referenceData = SharedInfo.Instance;
        }

        // Alternativa listener precendente in modo tale che supporti i cambi di rete
        public async Task Listener (CancellationToken tokenEndListener) {
            while (!tokenEndListener.IsCancellationRequested) {
                source = new CancellationTokenSource();
                CancellationToken tokenListener = source.Token;

                Console.WriteLine("Wait for change");
                lock (_referenceData.cvListener) {
                    while (_referenceData.GetLocalIPAddress().Equals(""))
                        Monitor.Wait(_referenceData.cvListener);
                }
                Console.WriteLine("Change Listener local IP:" + _referenceData.GetLocalIPAddress());

                IPAddress localAddr = IPAddress.Parse(_referenceData.GetLocalIPAddress());

                try {
                    //Creo server definendo un oggetto TCPListener con porta ed indirizzo
                    server = new TcpListener(localAddr, _referenceData.TCPPort);
                    server.Start();

                    // Loop ascolto
                    while (!tokenListener.IsCancellationRequested) {
                        Console.WriteLine("Waiting for connection...");
                        await server.AcceptTcpClientAsync().WithWaitCancellation(tokenListener).ContinueWith(async ( antecedent ) => {
                            try {
                                TcpClient client = antecedent.Result;
                                await ServeClient((TcpClient)antecedent.Result);
                            }
                            catch (Exception e) {
                                Console.WriteLine($"Exception {e}");
                            }
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    }
                }
                catch (SocketException e) {
                    Console.WriteLine($"SocketException on Listener - {e}");
                }
                catch (OperationCanceledException e) {
                    Console.WriteLine($"OperationCanceledException on Listener: {e.Message}");
                    Console.WriteLine("End execution because of destruction of the cancellation token");
                }
                catch (Exception e) {
                    Console.WriteLine($"Exception on Listener - {e}");
                }
                finally {
                    if (server != null)
                        server.Stop();
                }

            }
        }

        /// <summary>
        /// Metodo chiamato per stoppare il server
        /// </summary>
        public void StopServer () {
            Console.WriteLine("On stop server");
             source.Cancel();
        }

        /// <summary>
        /// Metodo che gestisce i vari client connessi
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public async Task ServeClient( object result) {
            NetworkStream stream = null;
            TcpClient client = null;
            Dictionary<string, FileRecvStatus> updateRecvDictionary = new Dictionary<string, FileRecvStatus>();
            Dictionary<string, FileSendStatus> updateSendDictionary = new Dictionary<string, FileSendStatus>();

            try {
                client = (TcpClient)result;
                Console.WriteLine($"Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");

                byte[] bytes = new byte[1+256+8];
                stream = client.GetStream();

                int i = 0;
                if ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0) {
                    // Il primo pacchetto cambia a seconda del tipo di pacchetto ricevuto
                    byte[] readPacket = new byte[1];
                    Array.Copy(bytes, 0, readPacket, 0, 1);

                    // In caso il primo byte non rispetti il tipo, viene lanciata un'eccezione
                    if ((int)readPacket[0] > 5)
                        throw new Exception("Packet not valid");

                    // La seconda parte dell'header corrisponde in ogni caso al nome di un file codificato in UTF8
                    PacketType type = (PacketType)readPacket[0];
                    readPacket = new byte[256];
                    Array.Copy(bytes, 1, readPacket, 0, 256);
                    string filename = "";
                    UTF8Encoding encoder;

                    // In caso di eccezione, questa viene rilanciata
                    try {
                        encoder = new UTF8Encoding();
                        filename = encoder.GetString(readPacket);
                        filename = filename.Replace("\0", string.Empty);
                    }
                    catch (Exception e) {
                        throw new Exception($"Packet not valid - Reason {e.Message}");
                    }
                    long dimFile = 0;

                    string ipClient = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                    switch (type) {
                        case PacketType.RFILE:
                            if (_referenceData.GetInfoLocalUser().AcceptAllFile) {
                                _referenceData.AddOrUpdateRecvStatus(ipClient, filename, FileRecvStatus.YSEND);

                                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.AddOrUpdateListFile(ipClient, filename, FileRecvStatus.YSEND, "-", 0.0f);
                                    MainWindow.main.NotifySistem();
                                    MainWindow.main.SendResponse(filename, ipClient, PacketType.YFILE);
                                }));
                         
                            } else {
                                _referenceData.AddOrUpdateRecvStatus(ipClient, filename, FileRecvStatus.TOCONF);

                                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.AddOrUpdateListFile(ipClient, filename, FileRecvStatus.TOCONF, "-", 0.0f);
                                    MainWindow.main.NotifySistem();
                                }));
                                
                            }
                            break;
                        case PacketType.YFILE:
                            if (_referenceData.UpdateSendStatusFileForUser(ipClient, filename, FileSendStatus.CONFERMED)) {
                                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.SendFile(filename, ipClient);
                                }));
                            }
                            else
                                throw new Exception("No file with name " + filename + " was announced from this client");
                            break;
                        case PacketType.NFILE:
                            if (!_referenceData.UpdateSendStatusFileForUser(ipClient, filename, FileSendStatus.REJECTED)) 
                                throw new Exception("No file with name " + filename + " was announced from this client");
                            break;
                        case PacketType.FSEND:
                            {
                                filename = filename.Substring(0, filename.Length - 1);
                                if(_referenceData.CheckPacketRecvFileStatus(ipClient, filename))
                                    throw new Exception("File with name " + filename + " never confirmed or need to be resend to the user");
                                string fileNameOriginal = filename;

                                readPacket = new byte[8];
                                Array.Copy(bytes, 256, readPacket, 0, 8);
                                try {
                                    dimFile = BitConverter.ToInt64(readPacket, 0);
                                }
                                catch (Exception e) {
                                    throw new Exception($"Packet not valid - Reason {e.Message}");
                                }
                                await ServeReceiveFile(client, stream, ipClient, filename, dimFile);
                            }
                            break;
                        case PacketType.CIMAGE:
                            readPacket = new byte[8];
                            Array.Copy(bytes, 256, readPacket, 0, 8);
                            try {
                                dimFile = BitConverter.ToInt64(readPacket, 0);
                            }
                            catch (Exception e) {
                                throw new Exception($"Packet not valid - Reason {e.Message}");
                            }
                            filename = filename.Substring(0, filename.Length - 1);
                            await ServeReceiveProfileImage(client, stream, ipClient, filename, dimFile);
                            break;
                    }   
                }
            }
            catch (SocketException e) {
                Console.WriteLine($"SocketException on ServeClientNewProtocolTest - {e}");
            }
            catch (Exception e) {
                Console.WriteLine($"Exception on ServeClientNewProtocolTest - {e}");
            }
            finally {
                stream.Close();
                client.Close();
            }
        }
        
        /// <summary>
        /// Metodo che gestisce il salvataggio di un file inviato da un host
        /// </summary>
        /// <param name="client">TCPClient, il mittente</param>
        /// <param name="stream">il NetworkStream per accedere ai dati</param>
        /// <param name="ip">Ip mittente</param>
        /// <param name="fileOriginal">Nome del file</param>
        /// <param name="dim">Dimesione del file</param>
        async Task ServeReceiveFile ( TcpClient client, NetworkStream stream, string ip, string fileOriginal, long dim ) {
            string filename = "";
            FileStream file;

            // Aspetto operazione sincrona per controllare se esiste un file con lo stesso nome
            await semaphoreForFile.WaitAsync();

            filename = Utility.PathTmp() + "\\" + fileOriginal;
            if (File.Exists(filename)) {
                string[] splits = filename.Split('.');
                string[] files = Directory.GetFiles(Utility.PathTmp(), Utility.PathToFileName(splits[splits.Length - 2]) + "*" + splits[splits.Length - 1]);
                splits[splits.Length - 2] += files.Count() > 0 ? ("_" + files.Count()) : "";
                filename = string.Join(".", splits);
            }
            file = File.Create(filename);
            Console.WriteLine($"File Created on path {filename}");

            semaphoreForFile.Release();

            // Inizio ricezione file
            // ---------------------------------------------------------------
            DateTime started = DateTime.Now;
            byte[] bytes = new byte[bufferSize * 64];
            bool first = true;
            bool noFlag = false;
            
            // dimFile = dimensione totale del file
            // dataReceived = totale dei byte che deve ancora ricevere
            long dataReceived = dim; 
            int i = 0;
            try {
                while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
                    double dataReceivedJet = 0.0f;

                    if (first) {
                        noFlag = _referenceData.CheckAndUpdateRecvFileStatus(ip, fileOriginal);
                        if(!noFlag)
                            await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, FileRecvStatus.INPROGRESS,null, null);
                            }));
                        first  = false;
                    }
                    else
                        noFlag = _referenceData.CheckRecvFileStatus(ip, fileOriginal, FileRecvStatus.NSEND);
                    
                    if (noFlag)
                        throw new Exception("File is rejected by remote host");

                    if (dataReceived > 0 && dataReceived < i) {
                        file.Write(bytes, 0, Convert.ToInt32(dataReceived));
                        dataReceivedJet = 100f;
                    }
                    else {
                        file.Write(bytes, 0, i);
                        dataReceivedJet = Math.Ceiling((float)(dim - dataReceived) / (float)dim * 100);
                    }
                    TimeSpan elapsedTime = DateTime.Now - started;
                    double numerator = ((double)dim - (double)(dim - dataReceived));
                    double denominator = dim/ elapsedTime.TotalSeconds;
                    double estimateTime = numerator / denominator;
                    TimeSpan estimatedTime = TimeSpan.FromSeconds(estimateTime);

                    string estimatedTimeJet = String.Format("{00:00}", estimatedTime.TotalMinutes) + ":" +
                                              String.Format("{00:00}", estimatedTime.TotalSeconds) + ":" +
                                              String.Format("{00:00}", estimatedTime.Milliseconds);
                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, null, estimatedTimeJet, dataReceivedJet);
                    }));
                    Console.WriteLine(dataReceivedJet + "%");

                    dataReceived -= i;
                }
                file.Close();
                
                // Fine ricezione file, inizio unzip!
                // ---------------------------------------------------------------
                string fileNameToProcess = filename;
                string ipUser = ip;

                // Creo un nuovo Task per eseguire l'unzip del file in modo asincrono
                await Task.Run(() => {
                    FileRecvStatus? currentStatusFile = _referenceData.GetStatusRecvFileForUser(ipUser, Utility.PathToFileName(fileNameToProcess));
                    if (currentStatusFile == null) return;

                    if (currentStatusFile.Value == FileRecvStatus.NSEND) {
                        try {
                            File.Delete(fileNameToProcess);
                        }
                        catch (Exception e) {
                            Console.WriteLine($"Exception on ExtractFileToSavePath : {e}");
                            obj.Release();
                        }
                    }
                    else {
                        try {
                            string savePathLocalUser = _referenceData.GetInfoLocalUser().SavePath;

                            // Caso Cartella
                            if (fileNameToProcess.Contains("Dir")) {
                                string nameFile = Utility.PathToFileName(fileNameToProcess);
                                nameFile = fileNameToProcess.Replace("_" + ipUser.Replace(".","_"), String.Empty);

                                // Ottengo il nome della cartella
                                string[] parts = nameFile.Split('_');
                                nameFile = nameFile.Replace(parts[0] + "_", string.Empty);

                                string nameDir = nameFile.Replace("_" + parts[parts.Length - 1], string.Empty);

                                string destPath = Path.Combine(savePathLocalUser, nameDir);
                                semaphoreForFile.Wait();
                                if (!Directory.Exists(destPath)) {
                                    Directory.CreateDirectory(destPath);
                                }
                                else {
                                    int numb = Directory.GetDirectories(savePathLocalUser, nameDir + "*").Count();
                                    Directory.CreateDirectory(destPath + "_(" + numb + ")");
                                    destPath += "_(" + numb + ")";
                                }

                                // Estraggo effettivamente i file
                                ZipFile.ExtractToDirectory(fileNameToProcess, destPath);
                                _referenceData.UpdateStatusRecvFileForUser(ipUser, Utility.PathToFileName(fileNameToProcess), FileRecvStatus.RECIVED);
                                semaphoreForFile.Release();

                            }
                            else if (fileNameToProcess.Contains("Files")) {
                                // Caso Files
                                using (ZipArchive archive = ZipFile.OpenRead(fileNameToProcess)) {
                                    // Per ogni file dell'archivio controllo se esiste lo stesso file o no
                                    semaphoreForFile.Wait();
                                    foreach (ZipArchiveEntry entry in archive.Entries) {
                                        string nameFileToExtract = "";
                                        if (File.Exists(Path.Combine(savePathLocalUser, entry.Name))) {
                                            string[] parts = entry.Name.Split('.');
                                            string extension = parts[parts.Length - 1];
                                            parts = parts.Take(parts.Count() - 1).ToArray();

                                            string name = String.Join(".", parts);
                                            int numb = Directory.GetFiles(savePathLocalUser, name + "*" + extension).Length;
                                            nameFileToExtract = name + "_(" + numb + ")." + extension;
                                        }
                                        else {
                                            nameFileToExtract = entry.Name;
                                        }
                                        entry.ExtractToFile(Path.Combine(savePathLocalUser, nameFileToExtract));
                                    }
                                    _referenceData.UpdateStatusRecvFileForUser(ipUser, Utility.PathToFileName(fileNameToProcess), FileRecvStatus.RECIVED);
                                    MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                        MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, FileRecvStatus.RECIVED, "-", 100);
                                    }));
                                    semaphoreForFile.Release();
                                }
                            }
                        }
                        catch (Exception e) {
                            Console.WriteLine($"Exception on unzip file received. Exception {e}");
                            File.Delete(fileNameToProcess);
                            semaphoreForFile.Release();
                        }
                        finally {
                            obj.Release();
                        }
                    }
                });
                await obj.WaitAsync();
            }
            catch (Exception e) {
                Console.WriteLine($"Exception on ServeReceiveFile - {e}");
                file.Close();
                File.Delete(filename);
            }
        }

        /// <summary>
        /// Metodo che gestisce il salvataggio dell'immagine di profilo di un host
        /// </summary>
        /// <param name="client">TCPClient, il mittente</param>
        /// <param name="stream">il NetworkStream per accedere ai dati</param>
        /// <param name="ip">Ip mittente</param>
        /// <param name="fileOriginal">Nome dell'immagine di profilo</param>
        /// <param name="dim">Dimesione del file</param>
        async Task ServeReceiveProfileImage ( TcpClient client, NetworkStream stream, string ip, string fileOriginal, long dim ) {
            string filename = Utility.PathHost() + "\\" + fileOriginal;
            byte[] bytes = new byte[bufferSize * 64];

            // Aspetto operazione sincrona per controllare se esiste un file con lo stesso nome
            await semaphoreProfileImage.WaitAsync();
            if (File.Exists(filename)) {
                string[] parts = fileOriginal.Split('.');
                string extension = parts[parts.Length - 1];
                parts = parts.Take(parts.Count() - 1).ToArray();
                string name = String.Join(".", parts);
                int numb = Directory.GetFiles(Utility.PathHost(), name + "*" + extension).Length;
                filename = Utility.PathHost() + "\\" + name + "_(" + numb + ")." + extension;
            }
            FileStream fileImage = null;
            try {
                fileImage = File.Create(filename);
                Console.WriteLine($"File Created on path {filename}");

                long dataReceived = dim;
                int i = 0;
                while (((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
                    if (dataReceived > 0 && dataReceived < i)
                        await fileImage.WriteAsync(bytes, 0, Convert.ToInt32(dataReceived));
                    else
                        await fileImage.WriteAsync(bytes, 0, i);

                    dataReceived -= i;
                }
                fileImage.Close();

                using (SHA256 sha = SHA256.Create()) {
                    FileStream fs = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(fs);
                    string hashimage = BitConverter.ToString(hash).Replace("-", string.Empty);
                    fs.Close();

                    if (!_referenceData.ProfileImageUpdate(hashimage, filename, ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()))
                        File.Delete(filename);
                }

                semaphoreProfileImage.Release();
            }
            catch (Exception e) {
                Console.WriteLine($"Exception on ServeReceiveProfileImage - {e}");
                semaphoreProfileImage.Release();

                if (fileImage != null) {
                    File.Delete(filename);
                }
            }
        }

        //public async Task ServeClientA ( Object result)
        //{
        //    // Gestione base problemi rete
        //    NetworkStream stream = null;
        //    TcpClient client = null;

        //    try
        //    {
        //        client = (TcpClient)result;

        //        Console.WriteLine($"Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");

        //        Byte[] bytes = new Byte[bufferSize];
        //        string data = null;

        //        stream = client.GetStream();

        //        int i = 0;
        //        if ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0)
        //        {

        //            // Il primo pacchetto contiene solo il nome del file e la dimensione del file
        //            // TODO: vedere quale carattere di terminazione scegliere, lo spazio potrebbe essere all'interno del nome del file
        //            UTF8Encoding encoder = new UTF8Encoding(); // Da cambiare, mettere almeno UTF-16
        //            data = encoder.GetString(bytes);

        //            // PER ROSSELLA: QUI SCRIVO STAI RICEVENDO FILE/COSE/BANANE!!!
        //            Console.WriteLine($"Received {data}");

        //            data = data.Replace("\0", string.Empty); // problemi con l'encoder e il valore \0
        //            string[] info = data.Split(' ');
        //            long dimfile = 0;
        //            string file_name = "";
        //            if (info[0].Equals("CHNETWORK"))
        //                source.Cancel();
        //                //Console.WriteLine("On CHNET");
        //            else
        //            {
        //                if (info[0].Equals("CHIMAGE")) {
        //                    file_name += Utility.PathHost();
        //                    file_name += /*"puserImage" +*/ "\\" + info[1];
        //                    dimfile = Convert.ToInt64(info[2]);
        //                }
        //                else
        //                {
        //                    dimfile = Convert.ToInt64(info[1]);
        //                    //file_name = _referenceData.LocalUser.SavePath + "\\" + info[0];
        //                    file_name = Utility.PathTmp() + "\\" + info[0];
        //                }

        //                // Crea il file e lo riempie
        //                bool isChImage = false;
        //                // Crea il file e lo riempie
        //                if (File.Exists(file_name)) {
        //                    if (info[0].Equals("CHIMAGE")) {
        //                        isChImage = true;
        //                    } else {
        //                        // TODO: cambiare nome file zip per evitare conflitti multiutente
        //                        string[] splits = file_name.Split('.');
        //                        //string[] files = Directory.GetFiles(_referenceData.LocalUser.SavePath, Utility.PathToFileName(splits[splits.Length - 2]) +"*" + splits[splits.Length - 1]);
        //                        string[] files = Directory.GetFiles(Utility.PathTmp(), Utility.PathToFileName(splits[splits.Length - 2]) + "*" + splits[splits.Length - 1]);
        //                        splits[splits.Length - 2] += files.Count() > 0 ? ("_"+files.Count()) : "" ;
        //                        file_name = string.Join(".", splits);
        //                    }
        //                }

        //                if (!isChImage) {
        //                    //TIMER PER CALCOLARE TEMPO RIMANENTE ALLA FINE DEL DOWNLOAD
        //                    //string secondsElapsed = "";
        //                    //Stopwatch stopwatch = new Stopwatch();
        //                    //stopwatch.Start();

        //                    DateTime started = DateTime.Now;

        //                    var file = File.Create(file_name);
        //                    Console.WriteLine($"File Created on path {file_name}");

        //                    bytes = new byte[bufferSize * 64];
        //                    long dataReceived = dimfile; // dimFile = dimensione totale del file , dataReceived = totale dei byte che deve ancora ricevere
        //                    while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
        //                        double dataReceivedJet = 0.0f;

        //                        if (dataReceived > 0 && dataReceived < i) { //bufferSize)
        //                            file.Write(bytes, 0, Convert.ToInt32(dataReceived));
        //                            dataReceivedJet = 100f;

        //                        }
        //                        else {
        //                            file.Write(bytes, 0, i);
        //                            dataReceivedJet = Math.Ceiling((float)(dimfile - dataReceived) / (float)dimfile * 100);
        //                        }
        //                        TimeSpan elapsedTime = DateTime.Now - started;

        //                        //OVERFLOW DA CONTROLLARE
        //                        //TimeSpan estimatedTime = 
        //                        //    TimeSpan.FromSeconds(
        //                        //        (dimfile - (dimfile - dataReceived)) / 
        //                        //        ((double)(dimfile - dataReceived)  / elapsedTime.TotalSeconds));

        //                        //PROGRESS BAR (BOH) -------------------------------
        //                        //secondsElapsed = stopwatch.Elapsed.TotalSeconds.ToString();
        //                        //string secondElapsedJet = secondsElapsed;
        //                        //string estimatedTimeJet = estimatedTime.ToString();
        //                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
        //                            MainWindow.main.progressFile.SetValue(ProgressBar.ValueProperty, dataReceivedJet);
        //                            //MainWindow.main.textTime.Text = estimatedTimeJet;
        //                        }));
        //                        Console.WriteLine(dataReceivedJet + "%");

        //                        dataReceived -= i;
        //                    }
        //                    Console.WriteLine($"File Received {data}");
        //                    //stopwatch.Stop();
        //                    ////secondsElapsed += stopwatch.Elapsed.TotalSeconds;
        //                    file.Close();

        //                    if(!isChImage)
        //                    ZipFile.ExtractToDirectory(file_name, _referenceData.LocalUser.SavePath);

        //                }
        //                else
        //                {
        //                    Console.WriteLine($"File CHIMAGE already saved " + data );
        //                    while (stream.Read(bytes, 0, bytes.Length) != 0);
        //                }



        //                // Avvisa che un'immagine è stata cambiata
        //                if (info[0].Equals("CHIMAGE"))
        //                {
        //                    //Salvo info e poi udp reciver aggiornerà le info

        //                    using (SHA256 sha = SHA256.Create())
        //                    {
        //                        FileStream fs = File.OpenRead(file_name);
        //                        byte[] hash = sha.ComputeHash(fs);
        //                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

        //                        _referenceData.UserImageChange[hashImage] = file_name;
        //                        fs.Close();
        //                    }
        //                }
        //            }

        //        }
        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine($"SocketException: {e}");
        //    }
        //    finally
        //    {
        //        stream.Close();
        //        client.Close();
        //    }
        //}

        /// <summary>
        /// Funzione che avvia il server, viene eseguito in un thread secondario che rimane in esecuzione fino alla fine del programma.
        /// Per ogni TCPClient viene creato un nuovo thread che lo gestisce (pensare caso troppi thread in esecuzione? evito eccessivo context switching)
        /// TODO: cercare quanti thread fisici la CPU riesce a gestire in contemporanea
        /// TODO: gestire correttamente gli accessi concorrenti alle risorse e la terminazione corretta del thread (adesso non gestita)
        /// </summary>
        //public void Listener(){
        //    // Caratteristiche base per tcp lister: porta ed indirizzo
        //    IPAddress localAddr = IPAddress.Parse(_referenceData.LocalIPAddress);
        //    TcpListener server = null;
        //    try
        //    {
        //        //Creo server definendo un oggetto TCPListener con porta ed indirizzo
        //        server = new TcpListener(localAddr, _referenceData.TCPPort);
        //        server.Start();

        //        //Creo buffer su cui scrivere dati (in questo caso byte generici)
        //        Byte[] bytes = new Byte[256];

        //        // Loop ascolto
        //        while (true)
        //        {
        //            Console.WriteLine("Waiting for connection...");
        //            TcpClient client = server.AcceptTcpClient();
        //            Thread t = new Thread(new ParameterizedThreadStart(ServeClient));
        //            t.Start(client);
        //        }
        //    }
        //    catch (SocketException e){
        //        Console.WriteLine($"SocketException: {e}");
        //    }
        //    finally{
        //        server.Stop();
        //    }
        //}


        //// Alternativa listener precendente in modo tale che supporti i cambi di rete
        //public void ListenerB ()// CancellationToken tokenEndListener )
        //{
        //    while (true)//!tokenEndListener.IsCancellationRequested)
        //    {
        //        //source = new CancellationTokenSource();
        //        //CancellationToken tokenListener = source.Token;

        //        Console.WriteLine("Wait for change");
        //        lock (_referenceData.cvListener)
        //        {
        //            while (_referenceData.LocalIPAddress.Equals(""))
        //                Monitor.Wait(_referenceData.cvListener);
        //        }
        //        Console.WriteLine("Change Listener local IP:" + _referenceData.LocalIPAddress);

        //        IPAddress localAddr = IPAddress.Parse(_referenceData.LocalIPAddress);
        //        //TcpListener server = null;

        //        try
        //        {
        //            //Creo server definendo un oggetto TCPListener con porta ed indirizzo
        //            server = new TcpListener(localAddr, _referenceData.TCPPort);
        //            server.Start();

        //            // Loop ascolto
        //            while (true)
        //            {
        //                Console.WriteLine("Waiting for connection...");
        //                TcpClient client = server.AcceptTcpClient();
        //                Thread t = new Thread(new ParameterizedThreadStart(ServeClient));
        //                t.Start(client);
        //                //await ServeClientA(client);
        //            }
        //        }
        //        catch (SocketException e)
        //        {
        //            Console.WriteLine($"SocketException: {e}");
        //        }
        //        catch(Exception e)
        //        {
        //            Console.WriteLine($"Exception: {e}");
        //        }
        //        finally
        //        {
        //            if (server != null)
        //                server.Stop();
        //        }

        //    }
        //}


        /// <summary>
        /// Metodo chiamato da un thread secondario che gestisce il client connesso.
        /// Riceve un file e lo salva nel file system, nel caso esista già per ora lo sovrascrive e non fa nessun controllo in caso non esista o di problemi di rete.
        /// TODO: vedere caso di file con lo stesso nome e come gestirli, gestire le varie casistiche di congestione di rete etc...
        /// </summary>
        /// <param name="result">Oggetto TCPClient connesso al server TCPListener corrente</param>
        //public void ServeClient(Object result){
        //    // Gestione base problemi rete
        //    NetworkStream stream = null;
        //    TcpClient client = null;
        //    try
        //    {
        //        client = (TcpClient)result;

        //        Console.WriteLine($"Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");


        //        Byte[] bytes = new Byte[bufferSize];
        //        string data = null;

        //        stream = client.GetStream();

        //        int i = 0;
        //        if ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
        //        {

        //            // Il primo pacchetto contiene solo il nome del file e la dimensione del file
        //            // TODO: vedere quale carattere di terminazione scegliere, lo spazio potrebbe essere all'interno del nome del file
        //            UTF8Encoding encoder = new UTF8Encoding(); // Da cambiare, mettere almeno UTF-16
        //            data = encoder.GetString(bytes);

        //            Console.WriteLine($"Received {data}");

        //            data = data.Replace("\0", string.Empty); // problemi con l'encoder e il valore \0
        //            string[] info = data.Split(' ');
        //            long dimfile = 0; 
        //            string file_name = "";
        //            if (info[0].Equals("CHIMAGE")){
        //                file_name += Utility.PathHost();
        //                file_name += /*"puserImage" +*/ "\\" + info[1];
        //                dimfile = Convert.ToInt64(info[2]);
        //            }
        //            else{
        //                dimfile = Convert.ToInt64(info[1]);
        //                file_name = info[0];
        //            }

        //            bool isChImage = false;
        //            // Crea il file e lo riempie
        //            if (File.Exists(file_name)) {
        //                if (info[0].Equals("CHIMAGE")) {
        //                    isChImage = true;
        //                } else {
        //                    string[] splits = file_name.Split('.');
        //                    splits[splits.Length - 2] += "_Copia";
        //                    file_name = string.Join(".", splits);
        //                }
        //            }

        //            if (!isChImage) {
        //                var file = File.Create(file_name);
        //                bytes = new byte[bufferSize * 64];
        //                long dataReceived = dimfile;
        //                while (((i = stream.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
        //                    if (dataReceived > 0 && dataReceived < i) //bufferSize)
        //                        file.Write(bytes, 0, Convert.ToInt32(dataReceived));
        //                    else
        //                        file.Write(bytes, 0, i);
        //                    dataReceived -= i;
        //                }

        //                file.Close();
        //            }
        //            else{
        //             while (stream.Read(bytes, 0, bytes.Length) != 0);
        //            }

        //            // Avvisa che un'immagine è stata cambiata
        //            if (info[0].Equals("CHIMAGE")){
        //                //Salvo info e poi udp reciver aggiornerà le info

        //                using (SHA256 sha = SHA256.Create())
        //                {
        //                    FileStream fs = File.OpenRead(file_name);
        //                    byte[] hash = sha.ComputeHash(fs);
        //                    string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

        //                    _referenceData.UserImageChange[hashImage] = file_name;
        //                    fs.Close();
        //                }
        //            }
        //        }
        //        /*
        //        // CODICE VECCHIO, tengo per sicurezza // 
        //        while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
        //        {
        //            UTF8Encoding encoder = new UTF8Encoding();
        //            data = encoder.GetString(bytes);
        //            Console.WriteLine($"Received {data}");
        //            data = data.Replace("\0", string.Empty);
        //            MainWindow.main.textInfoMessage.Dispatcher.BeginInvoke(
        //            DispatcherPriority.Normal, new Action(() => {
        //                MainWindow.main.textInfoMessage.Text = $"Received {data}";
        //            }));
        //        }
        //        Thread.Sleep(2000);
        //        */

        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine($"SocketException: {e}");
        //    }
        //    finally {
        //        stream.Close();
        //        client.Close();
        //    }
        //    }
    }
}