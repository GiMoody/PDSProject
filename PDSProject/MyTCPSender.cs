using System;

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;
using System.Windows.Threading;
using System.Security.Cryptography;

namespace PDSProject
{
    /// <summary>
    /// Class that manage a TCPClient
    /// /// </summary>
    class MyTCPSender {
        SharedInfo _referenceData;
        const Int32 bufferSize = 1024;

        public MyTCPSender () {
            _referenceData = SharedInfo.Instance;
        }

        /// <summary>
        /// Questo metodo conferma o rifiuta la ricezione di un file da parte di un host remoto.
        /// Seguito dalla conferma ci sarà il nome del file su cui è stata fatta la scelta
        /// </summary>
        /// <param name="ip">Ip dell'host su cui si è eseguita la scelta</param>
        /// <param name="type">La scela effettuata</param>
        public async Task SendResponse (string ip, string nameFile, PacketType type) {
            if (type != PacketType.YFILE && type != PacketType.NFILE) return;

            if (!_referenceData.UpdateStatusRecvFileForUser(ip, nameFile, type == PacketType.YFILE ? FileRecvStatus.YSEND : FileRecvStatus.NSEND))
                throw new Exception("File don't exists in collection");

            int attempts = 0;
            // In case of exception resend the packet three times
            do {
                TcpClient client = null;
                NetworkStream stream = null;

                try {
                    attempts++;
                    client = new TcpClient();
                    await client.ConnectAsync(ip, SharedInfo.TCPPort).ConfigureAwait(false);

                    byte[] bytes = new byte[1 + 256];
                    bytes[0] = (byte)type;
                    UTF8Encoding encorder = new UTF8Encoding();
                    encorder.GetBytes(nameFile).CopyTo(bytes, 1);

                    stream = client.GetStream();
                    await stream.WriteAsync(bytes, 0, 257).ConfigureAwait(false);
                    break;
                }
                catch (SocketException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendResponse - {e.Message}");

                    // If the remote host was offline, try to resend it for three times
                    if (_referenceData.GetUserStatus(ip).Equals("offline"))
                        break;
                    else if (attempts == 3)
                        break;
                    else
                        await Task.Delay(10).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendResponse - {e.Message}");
                    if (attempts == 3)
                        break;
                    else
                        await Task.Delay(10).ConfigureAwait(false);
                }
                finally {
                    client.Close();
                    stream.Close();
                }
                if (attempts == 3 && _referenceData.GetUserStatus(ip).Equals("offline"))
                    break;
            } while (true);
        }
        
        /// <summary>
        /// Send a request of send file to a remote host.
        /// It sends only a list of names, not the full files
        /// </summary>
        /// <param name="filenames">List of paths</param>
        public async Task SendRequest (List<string> filenames) {
            List<string> CurrentSelectedHost = _referenceData.GetCurrentSelectedHost();
            // If no user is selected, no request will be sended
            if (CurrentSelectedHost.Count <= 0) return;
            
            // Create a Task list to send all the request at the same time
            List<Task> listTask = new List<Task>();
            foreach (string ip in CurrentSelectedHost) {
                IPAddress serverAddr = IPAddress.Parse(ip);
                listTask.Add(SendSetFiles(filenames, ip));
            }

            // When all tasks are generated, they will be executed. We wait untill all the task are completed
            try{
                await Task.WhenAll(listTask);
            }
            catch (Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendRequest - {e.Message}");
            }
        }

        /// <summary>
        /// It send the file name's list to a remote user
        /// </summary>
        /// <param name="filenames">List of the file's name</param>
        /// <param name="ip">Remote user's ip</param>
        public async Task SendSetFiles (List<string> filenames, string ip) {
            foreach (string file in filenames) {
                int attempts = 0;

                // Check if the file is in a Ready status before continue
                if (!_referenceData.CheckSendStatusFile(ip, file, FileSendStatus.READY)) continue;

                // In case of exception resend the packet three times
                do {
                    TcpClient client = null;
                    NetworkStream stream = null;

                    try {
                        attempts++;
                        client = new TcpClient();
                        await client.ConnectAsync(ip, SharedInfo.TCPPort).ConfigureAwait(false);

                        byte[] bytes = new byte[1 + 256];
                        bytes[0] = (byte)PacketType.RFILE;
                        UTF8Encoding encorder = new UTF8Encoding();
                        encorder.GetBytes(file).CopyTo(bytes, 1);

                        stream = client.GetStream();
                        await stream.WriteAsync(bytes, 0, 257).ConfigureAwait(false);
                        break;
                    }
                    catch (SocketException e) {
                        Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendSetFiles - {e.Message}");

                        // If the remote host was offline, try to resend it for three times
                        string UserStatus = _referenceData.GetUserStatus(ip);
                        if (!UserStatus.Equals("") && UserStatus.Equals("offline"))
                            break;
                        else if (attempts == 3)
                            break;
                        else
                            await Task.Delay(10).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendSetFiles - {e.Message}");
                        if (attempts == 3)
                            break;
                        else
                            await Task.Delay(10).ConfigureAwait(false);
                    }
                    finally {
                        client.Close();
                        stream.Close();
                    }
                    if (attempts == 3 && _referenceData.GetUserStatus(ip).Equals("offline"))
                        break;
                } while (true);
            }
        }

        /// <summary>
        /// Send to all the connected user the profile image of local user
        /// </summary>
        public async Task SendProfilePicture () {
            // Get the current list of online users
            List<Host> currentHosts = _referenceData.GetOnlineUsers();
            
            string currentImagePath = _referenceData.GetInfoLocalUser().ProfileImagePath;
            if (!Utility.PathToFileName(currentImagePath).Equals(_referenceData.defaultImage)) {
                IPAddress serverAddr;
                foreach (Host host in currentHosts) {

                    serverAddr = IPAddress.Parse(host.Ip);
                    int attempts = 0;

                    // In case of exception resend the packet three times
                    do {
                        TcpClient client = null;
                        NetworkStream stream = null;

                        try {
                            attempts++;
                            client = new TcpClient();
                            await client.ConnectAsync(serverAddr.ToString(), SharedInfo.TCPPort).ConfigureAwait(false);

                            // It sends also the Hash of the current image
                            byte[] bytes = new byte[1 + 256 + 8 + 32];

                            // 1^ byte: packet type
                            bytes[0] = (byte)PacketType.CIMAGE;
                            
                            // Following 256 bytes : file name
                            UTF8Encoding encorder = new UTF8Encoding();
                            encorder.GetBytes(Utility.PathToFileName(currentImagePath)).CopyTo(bytes, 1);

                            // Save the hash of the file
                            byte[] hash;
                            using (SHA256 sha = SHA256.Create()) {
                                FileStream file = File.OpenRead(currentImagePath);
                                hash = sha.ComputeHash(file);
                                file.Close();
                            }

                            // Open file image
                            using (var file = new FileStream(currentImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                                // Following 8 bytes : file legth
                                BitConverter.GetBytes(file.Length).CopyTo(bytes, 257);

                                // Following 32 bytes : hash of the image
                                hash.CopyTo(bytes, 265);

                                // Get Network stream
                                stream = client.GetStream();

                                // First 297 : header
                                await stream.WriteAsync(bytes, 0, 297).ConfigureAwait(false);

                                // Following 64K of payload (profile image)
                                bytes = new byte[bufferSize * 64];
                                await file.CopyToAsync(stream, bufferSize).ConfigureAwait(false);
                            }
                            break;
                        }
                        catch (SocketException e) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendProfilePicture - {e.Message}");

                            // If the remote host was offline, try to resend it for three times
                            string UserStatus = _referenceData.GetUserStatus(host.Ip);
                            if (!UserStatus.Equals("") && UserStatus.Equals("offline"))
                                break;
                            else if (attempts == 3)
                                break;
                            else
                                await Task.Delay(10).ConfigureAwait(false);
                        }
                        catch (Exception e) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendProfilePicture - {e.Message}");
                            if (attempts == 3)
                                break;
                            else
                                await Task.Delay(10).ConfigureAwait(false);
                        }
                        finally {
                            client.Close();
                            stream.Close();
                        }
                    } while (true);
                }
            }
        }

        /// <summary>
        /// Send file to the remote user that accept it
        /// </summary>
        /// <param name="filename">File name</param>
        /// <param name="ip">Ip remote user</param>
        public async Task SendFile (string filename, string ip) {
            // Check if the file is in a status to be rended or if it was confirmed 
            if (!_referenceData.CheckPacketSendFileStatus(ip, filename)) return;
            if (_referenceData.GetUserStatus(ip).Equals("offline")) {
                _referenceData.UpdateSendStatusFileForUser(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT);

                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT, "-", 0);
                }));
                return;
            }
            IPAddress serverAddr = IPAddress.Parse(ip);
            
            // In case of exception resend the packet three times
                TcpClient client = null;
                NetworkStream stream = null;

                if (!_referenceData.CheckPacketSendFileStatus(ip, filename)) return;

                try {
                    client = new TcpClient();
                    await client.ConnectAsync(serverAddr.ToString(), SharedInfo.TCPPort).ConfigureAwait(false);

                    byte[] bytes = new byte[1 + 256 + 8];

                    // 1^ byte: packet type
                    bytes[0] = (byte)PacketType.FSEND;

                    // Following 256 bytes : file name
                    UTF8Encoding encorder = new UTF8Encoding();
                    encorder.GetBytes(Utility.PathToFileName(filename)).CopyTo(bytes, 1);

                    // Open file image
                    filename = Utility.PathTmp() + "\\" + filename;
                    using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        // Following 8 bytes : file legth
                        BitConverter.GetBytes(file.Length).CopyTo(bytes, 257);

                        stream = client.GetStream();

                        // Primi 265 byte di header
                        await stream.WriteAsync(bytes, 0, 265).ConfigureAwait(false);
                        _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.INPROGRESS);

                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.INPROGRESS, "-", 0);
                        }));

                        // First 297 : header
                        bytes = new byte[bufferSize * 64];

                        int i;
                        long dataReceived = file.Length;
                        uint estimatedTimePacketCount = 0;
                        double numerator = 0.0;
                        double estimateTime = 0.0;
                        DateTime started = DateTime.Now;
                        TimeSpan estimatedTime = TimeSpan.FromSeconds(0);
                        
                        while(((i = file.Read(bytes, 0, bytes.Length)) != 0) && dataReceived >= 0) {
                            double dataReceivedJet = 0.0f;

                            if(_referenceData.CheckSendStatusFile(ip, Utility.PathToFileName(filename), FileSendStatus.REJECTED))
                                throw new RejectedFileException("File is rejected by remote host");
                            
                            if(_referenceData.GetUserStatus(ip).Equals("offline"))
                                throw new Exception("User offline");

                            if (dataReceived > 0 && dataReceived < i) {
                                await stream.WriteAsync(bytes, 0, Convert.ToInt32(dataReceived));
                                dataReceivedJet = 100f;
                            } else {
                                await stream.WriteAsync(bytes, 0, i);
                                dataReceivedJet = Math.Ceiling((double)(file.Length - dataReceived) / ((double)file.Length) * 100);
                            }
                            dataReceived -= i;


                            if(estimatedTimePacketCount < 5) {
                                numerator += (double)(file.Length - dataReceived);
                                estimatedTimePacketCount++;
                            } else {
                                TimeSpan elapsedTime = DateTime.Now - started;

                                estimateTime = elapsedTime.TotalSeconds * dataReceived / numerator;
                                estimatedTime = TimeSpan.FromSeconds(estimateTime);

                                numerator = 0.0;
                                estimatedTimePacketCount = 0;
                            }
                            string estimatedTimeJet = string.Format("{00:00}", estimatedTime.Minutes) + ":" +
                                                      string.Format("{00:00}", estimatedTime.Seconds) + ":" +
                                                      string.Format("{00:00}", estimatedTime.Milliseconds);
                            await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.INPROGRESS, estimatedTimeJet, dataReceivedJet);
                            }));
                        }
                    }
                    _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.END);

                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.END, "-", 100);
                    }));

                } catch(RejectedFileException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - RejectedFileException on SendFile - {e.Message}");

                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.REJECTED, "-", 0);
                    }));

                } catch (SocketException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendFile - {e.Message}");

                    _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.REJECTED);

                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.REJECTED, "-", 0);
                    }));
                }
                catch (Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendFile - {e.Message}");
                    // If the remote host was offline, try to resend it for three times
                    _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.REJECTED);

                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.REJECTED, "-", 0);
                    }));
                }
                finally {
                    client.Close();
                    stream.Close();
                }

        }
    }
}

