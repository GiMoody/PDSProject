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
    /// <summary>
    /// Class used to manage the CancellationToken for the UDP/TCP listener
    /// </summary>
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
    /// Class that manage the TCP Listener (server).
    /// </summary>
    public class MyTCPListener {
        SharedInfo _referenceData;
        const long bufferSize = 1024;
        CancellationTokenSource source;

        TcpListener server = null;

        SemaphoreSlim semaphoreForFile = new SemaphoreSlim(1,1);
        SemaphoreSlim semaphoreProfileImage = new SemaphoreSlim(1,1);
        SemaphoreSlim obj = new SemaphoreSlim(0);

        /// <summary>
        /// MyTCPListener Constructor
        /// </summary>
        public MyTCPListener () {
            _referenceData = SharedInfo.Instance;
        }

        /// <summary>
        /// Main Listener function that wait for any client that wants to send file or profile images
        /// </summary>
        /// <param name="tokenEndListener">Cancellation token from the MainWindows</param>
        /// <returns>Return a task, it is used to allow the Task asynchronous programming model (TAP)</returns>
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
                    // Create a TCPLIstenr object
                    server = new TcpListener(localAddr, SharedInfo.TCPPort);
                    server.Start();

                    // Listener loop
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
        /// It stops the server
        /// </summary>
        public void StopServer () {
            Console.WriteLine($"{DateTime.Now.ToString()} - Current TCPListener stopped");
             source.Cancel();
        }

        /// <summary>
        /// Manage the connected client
        /// </summary>
        /// <param name="result">TCPClient object</param>
        /// <returns>Return a task, it is used to allow the Task asynchronous programming model (TAP)</returns>
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
                    // The first byte define the type of the packet
                    byte[] readPacket = new byte[1];
                    Array.Copy(bytes, 0, readPacket, 0, 1);

                    // If it is not correct, send an Exception
                    if ((int)readPacket[0] > 5)
                        throw new Exception($"Packet not valid");

                    // The following 256 bytes are the name of the file (UTF8 coding)
                    PacketType type = (PacketType)readPacket[0];
                    readPacket = new byte[256];
                    Array.Copy(bytes, 1, readPacket, 0, 256);
                    string filename = "";
                    UTF8Encoding encoder;

                    // Trown an exception in case the sequence of bytes cannot be converted as a stirng
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
        /// Manage the download of a file sended by a remote host
        /// </summary>
        /// <param name="client">TCPClient object</param>
        /// <param name="stream">NetworkStream of the client</param>
        /// <param name="ip">Remote Ip user</param>
        /// <param name="fileOriginal">File Name</param>
        /// <param name="dim">File lenght(bytes)</param>
        async Task ServeReceiveFile ( TcpClient client, NetworkStream stream, string ip, string fileOriginal, long dim ) {
            string filename = "";
            FileStream file;

            // Wait the asyncronous operation to chec if the same file exists in the filesystem
            await semaphoreForFile.WaitAsync();

            filename = Utility.PathTmp() + "\\" + fileOriginal;
            if (File.Exists(filename)) {
                string[] splits = filename.Split('.');
                string[] files = Directory.GetFiles(Utility.PathTmp(), Utility.PathToFileName(splits[splits.Length - 2]) + "*" + splits[splits.Length - 1]);
                splits[splits.Length - 2] += files.Count() > 0 ? ("_" + files.Count()) : "";
                filename = string.Join(".", splits);
                _referenceData.UpdateFileName(fileOriginal,Utility.PathToFileName(filename), ip, FileRecvStatus.YSEND);
            }
            file = File.Create(filename);
            Console.WriteLine($"{DateTime.Now.ToString()}\t - ServeReceive created file on path {filename}");

            semaphoreForFile.Release();

            // Start file dowload
            // ---------------------------------------------------------------
           
            byte[] bytes = new byte[bufferSize * 64];
            bool first = true;
            bool noFlag = false;
            
            // dimFile = total file dimension
            // dataReceived = total bytes that aren't received yet
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
                
                // End file download, start unzip!
                // ---------------------------------------------------------------
                string fileNameToProcess = filename;
                string ipUser = ip;

                // Create a new task to execute the unzip asynchronously
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

                            // Create directory
                            if (fileNameToProcess.Contains("Dir")) {
                                string nameFile = Utility.PathToFileName(fileNameToProcess);
                                nameFile = fileNameToProcess.Replace("_" + ipUser.Replace(".","_"), String.Empty);

                                // Get directory name
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

                                // Unzip files
                                ZipFile.ExtractToDirectory(fileNameToProcess, destPath);
                                _referenceData.UpdateStatusRecvFileForUser(ipUser, Utility.PathToFileName(fileNameToProcess), FileRecvStatus.RECIVED);
                                MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    MainWindow.main.AddOrUpdateListFile(ip, fileOriginal, FileRecvStatus.RECIVED, "-", 100);
                                    MainWindow.main.StopNotify();
                                }));
                                semaphoreForFile.Release();

                            }
                            else if (fileNameToProcess.Contains("Files")) {
                                // In case of a zip full of files
                                using (ZipArchive archive = ZipFile.OpenRead(fileNameToProcess)) {
                                    // Check of each file if it already exists a file with the same name
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
        /// Manage a profile image dowload of a remote user
        /// </summary>
        /// <param name="client">TCPClient</param>
        /// <param name="stream">NetworkStream of the client</param>
        /// <param name="ip">Remote Ip</param>
        /// <param name="fileOriginal">Original file name</param>
        /// <param name="dim">Lenght of the file (bytes)</param>
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

            // Wait end asyncronous operation to check if a file already exists or not with the same name
            await semaphoreProfileImage.WaitAsync();

            if (_referenceData.GetRemoteUserHashImage(ip).Equals(hash_s)) {
                semaphoreProfileImage.Release();
                return;
            }
            bytes = new byte[bufferSize * 64];

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