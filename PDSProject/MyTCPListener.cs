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
                
                lock (_referenceData.cvListener) {
                    while (_referenceData.GetLocalIPAddress().Equals(""))
                        Monitor.Wait(_referenceData.cvListener);
                }
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Change Listener local IP  {_referenceData.GetLocalIPAddress()}");

                IPAddress localAddr = IPAddress.Parse(_referenceData.GetLocalIPAddress());

                try {
                    //Creo server definendo un oggetto TCPListener con porta ed indirizzo
                    server = new TcpListener(localAddr, _referenceData.TCPPort);
                    server.Start();

                    // Loop ascolto
                    while (!tokenListener.IsCancellationRequested) {
                        Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener Waiting for connection...");
                        await server.AcceptTcpClientAsync().WithWaitCancellation(tokenListener).ContinueWith(async ( antecedent ) => {
                            try {
                                TcpClient client = antecedent.Result;
                                await ServeClient((TcpClient)antecedent.Result);
                            }
                            catch (Exception e) {
                                Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener Exception {e.Message}");
                            }
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    }
                }
                catch (SocketException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener SocketException - {e.Message}");
                }
                catch (OperationCanceledException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener OperationCanceledException - {e.Message}");
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - End execution because of destruction of the cancellation token");
                }
                catch (Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener Exception - {e.GetType()} {e.Message}");
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
            Console.WriteLine($"{DateTime.Now.ToString()} - Current TCPListener stopped");
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
                Console.WriteLine($"{DateTime.Now.ToString()}\t - TCPListener Client connected! {((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()}");

                byte[] bytes = new byte[1+256+8];
                stream = client.GetStream();

                int i = 0;
                if ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0) {
                    // Il primo pacchetto cambia a seconda del tipo di pacchetto ricevuto
                    byte[] readPacket = new byte[1];
                    Array.Copy(bytes, 0, readPacket, 0, 1);

                    // In caso il primo byte non rispetti il tipo, viene lanciata un'eccezione
                    if ((int)readPacket[0] > 5)
                        throw new Exception($"Packet not valid");

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
                                    MainWindow.main.AddOrUpdateListFile(ipClient, filename, FileSendStatus.CONFERMED, "-", 0);
                                    MainWindow.main.SendFile(filename, ipClient);
                                }));
                            }
                            else
                                throw new Exception("No file with name " + filename + " was announced from this client");
                            break;
                        case PacketType.NFILE:
                            if (_referenceData.UpdateSendStatusFileForUser(ipClient, filename, FileSendStatus.REJECTED)) {
                                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.AddOrUpdateListFile(ipClient, filename, FileSendStatus.REJECTED, "-", 0);
                                    MainWindow.main.SendFile(filename, ipClient);
                                }));
                            }
                            else
                                throw new Exception("No file with name " + filename + " was announced from this client");
                            break;
                        case PacketType.FSEND:
                            {
                                if(_referenceData.CheckPacketRecvFileStatus(ipClient, filename))
                                    throw new Exception("File with name " + filename + " never confirmed or need to be resend to the user");
                                string fileNameOriginal = filename;

                                readPacket = new byte[8];
                                Array.Copy(bytes, 257, readPacket, 0, 8);
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
                            Array.Copy(bytes, 257, readPacket, 0, 8);
                            try {
                                dimFile = BitConverter.ToInt64(readPacket, 0);
                            }
                            catch (Exception e) {
                                throw new Exception($"Packet not valid - Reason {e.Message}");
                            }
                            await ServeReceiveProfileImage(client, stream, ipClient, filename, dimFile);
                            break;
                    }   
                }
            }
            catch (SocketException e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Serve TCPClient SocketException - {e.Message}");
            }
            catch (Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Serve TCPClient Exception - {e.GetType()} {e.Message}");
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
                _referenceData.UpdateFileName(filename, ip, FileRecvStatus.YSEND);
            }
            file = File.Create(filename);
            Console.WriteLine($"{DateTime.Now.ToString()}\t - ServeReceive created file on path {filename}");

            semaphoreForFile.Release();

            // Inizio ricezione file
            // ---------------------------------------------------------------
           
            byte[] bytes = new byte[bufferSize * 64];
            bool first = true;
            bool noFlag = false;
            
            // dimFile = dimensione totale del file
            // dataReceived = totale dei byte che deve ancora ricevere
            long dataReceived = dim; 
            int i = 0;
            uint estimatedTimePacketCount = 0;
            double numerator = 0.0;
            double estimateTime = 0.0;
            DateTime started = DateTime.Now;
            TimeSpan estimatedTime = TimeSpan.FromSeconds(0);

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
                        dataReceivedJet = Math.Ceiling((double)(dim - dataReceived) / ((double)dim)*100);
                    }
                    dataReceived -= i;

                    
                    if(estimatedTimePacketCount < 5) { 
                        numerator += (double)(dim - dataReceived);
                        estimatedTimePacketCount++;
                    }
                    else {
                        TimeSpan elapsedTime = DateTime.Now - started;

                        estimateTime = elapsedTime.TotalSeconds * dataReceived / numerator;
                        estimatedTime= TimeSpan.FromSeconds(estimateTime);

                        numerator = 0.0;
                        estimatedTimePacketCount = 0;
                    }
                    string estimatedTimeJet = string.Format("{00:00}", estimatedTime.Minutes) + ":" +
                                              string.Format("{00:00}", estimatedTime.Seconds) + ":" +
                                              string.Format("{00:00}", estimatedTime.Milliseconds);
                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, FileRecvStatus.INPROGRESS, estimatedTimeJet, dataReceivedJet);
                    }));
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
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - ExtractFileToSavePath Exception - {e.GetType()} {e.Message}");
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
                                        MainWindow.main.StopNotify();
                                    }));
                                    semaphoreForFile.Release();
                                }
                            }
                        }
                        catch (Exception e) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on unzip file received - {e.GetType()} {e.Message}");
                            File.Delete(fileNameToProcess);
                            FileRecvStatus status = FileRecvStatus.RESENT;
                            if(_referenceData.GetUserStatus(ipUser).Equals("online")) {
                                status = FileRecvStatus.NSEND;
                            }

                            _referenceData.UpdateStatusRecvFileForUser(ipUser, Utility.PathToFileName(fileNameToProcess), status);
                            MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, status, "-", 0);
                            }));
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
                Console.WriteLine($"{DateTime.Now.ToString()}\t - ServeReceiveFile Exception - {e.GetType()} {e.Message}");
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
            int i = 0;
            string hash_s = "";
            byte[] bytes = new byte[32];

            if ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0) {
                try {
                    hash_s = BitConverter.ToString(bytes).Replace("-", string.Empty);
                }
                catch (Exception) {
                    throw new Exception("Pacchetto inviato non corretto");
                }
            }
            else throw new Exception("Pacchetto inviato non corretto"); ;

            // Aspetto operazione sincrona per controllare se esiste un file con lo stesso nome
            await semaphoreProfileImage.WaitAsync();

            if (_referenceData.GetRemoteUserHashImage(ip).Equals(hash_s)) {
                semaphoreProfileImage.Release();
                return;
            }
            bytes = new byte[bufferSize * 64];

            // Aspetto operazione sincrona per controllare se esiste un file con lo stesso nome
            //wait semaphoreProfileImage.WaitAsync();
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
                Console.WriteLine($"{DateTime.Now.ToString()}\t - ProfileImage File Created on path {filename}");

                long dataReceived = dim;
                //int i = 0;
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
                Console.WriteLine($"{DateTime.Now.ToString()}\t - ServeReceiveProfileImage Exception - {e.GetType()} {e.Message}");
                semaphoreProfileImage.Release();

                if (fileImage != null) {
                    File.Delete(filename);
                }
            }
        }
    }
}