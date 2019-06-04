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
    /// Classe che gestisce il TCPClient.
    /// Per ora client e server sono stati separati, ma se non risulta funzionale è possibile unirli in un'unica classe.
    /// Internamente ha un riferimento a SharedInfo al quale può accedere all'IP dell'utente locale, IP broadcast dell'attuale sottorete e reltive porte.
    /// </summary>
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
            lock (_referenceData.FileToRecive) {
                if (_referenceData.FileToRecive.ContainsKey(ip) && _referenceData.FileToRecive[ip].ContainsKey(nameFile)) {
                    if (type == PacketType.YFILE)
                        _referenceData.FileToRecive[ip][nameFile] = FileRecvStatus.YSEND;
                    else
                        _referenceData.FileToRecive[ip][nameFile] = FileRecvStatus.NSEND;
                }
            }

            int attempts = 0;
            // In caso di eccezione, prova a rinviare il pacchetto n volte
            do {
                TcpClient client = null;
                NetworkStream stream = null;

                try {
                    attempts++;
                    client = new TcpClient();
                    await client.ConnectAsync(ip, _referenceData.TCPPort).ConfigureAwait(false);

                    // La dimensione massima del nome del file è di 256 bytes
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

                    // In caso l'host si sia disconnesso mentre si inviava la richiesta si passa al successivo, appena ritornerà
                    // attivo si invierà la richiesta di invio di nuovo
                    if (_referenceData.Users[ip].Status.Equals("offline"))
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
                if (attempts == 3 && _referenceData.Users[ip].Status.Equals("offline"))
                    break;
            } while (true);
        }
        
        /// <summary>
        /// Metodo che invia una richiesta di invio file all'host destinazione.
        /// Il metodo riceve solo la lista dei nomi dei file da inviare mentre la lista degli utenti a cui sarà inviata la richiesta
        /// è salvata all'interno dell'oggetto singleton _referenceData (selectedHosts)
        /// </summary>
        /// <param name="filenames">Lista path file</param>
        public async Task SendRequest (List<string> filenames) {
            List<string> CurrentSelectedHost = _referenceData.GetCurrentSelectedHost();
            // Nel caso non ci siano utenti selezionati non si invia niente
            if (CurrentSelectedHost.Count <= 0) return;
            
            // Per far partire contemporanemante le richieste di invio per ogni utente si utilizza una lista di Task
            List<Task> listTask = new List<Task>();
            foreach (string ip in CurrentSelectedHost) {
                IPAddress serverAddr = IPAddress.Parse(ip);
                listTask.Add(SendSetFiles(filenames, ip));
            }

            //Quando sono stati generati tutti i task, questi vengono eseguiti. Si attente fino a quando tutti i task sono completati
            try{
                await Task.WhenAll(listTask);
            }
            catch (Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendRequest - {e.Message}");
            }
        }

        /// <summary>
        /// Metodo che invia effettivamente il pacchetto TCP di richiesta invio file
        /// </summary>
        /// <param name="filenames">Lista dei file da inviare</param>
        /// <param name="ip">IP host destinazione</param>
        public async Task SendSetFiles (List<string> filenames, string ip) {
            // Per ogni file creo un nuovo TCPClient
            foreach (string file in filenames) {
                int attempts = 0;

                // Prima di tutto controllo se il file da annunciare sia stato già annunciato o no
                if (!_referenceData.CheckSendStatusFile(ip, file, FileSendStatus.READY)) continue;
                
                // In caso di eccezione, prova a rinviare il pacchetto n volte
                do {
                    TcpClient client = null;
                    NetworkStream stream = null;

                    try {
                        attempts++;
                        client = new TcpClient();
                        await client.ConnectAsync(ip, _referenceData.TCPPort).ConfigureAwait(false);

                        // La dimensione massima del nome del file è di 256 bytes
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

                        // In caso l'host si sia disconnesso mentre si inviava la richiesta si passa al successivo, appena ritornerà
                        // attivo si invierà la richiesta di invio di nuovo
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
        /// Invio a tutti gli utenti connessi l'immagine di proflio dell'host locale.
        /// </summary>
        public async Task SendProfilePicture () {
            // Salvo su una variabile temporanea la lista degli utenti connessi
            List<Host> currentHosts = _referenceData.GetOnlineUsers();
            
            string currentImagePath = _referenceData.GetInfoLocalUser().ProfileImagePath;
            if (!Utility.PathToFileName(currentImagePath).Equals(_referenceData.defaultImage)) {
                IPAddress serverAddr;
                foreach (Host host in currentHosts) {

                    serverAddr = IPAddress.Parse(host.Ip);
                    int attempts = 0;

                    // In caso di eccezione, prova a rinviare il pacchetto n volte
                    do {
                        TcpClient client = null;
                        NetworkStream stream = null;

                        try {
                            attempts++;
                            client = new TcpClient();
                            await client.ConnectAsync(serverAddr.ToString(), _referenceData.TCPPort).ConfigureAwait(false);

                            // La dimensione massima del nome del file è di 256 bytes, mentre la dimensione del file è di 8 bytes
                            byte[] bytes = new byte[1 + 256 + 8 + 32];

                            // Primo byte: tipo pacchetto
                            bytes[0] = (byte)PacketType.CIMAGE;
                            
                            // Successivi 256 bytes : nome file
                            UTF8Encoding encorder = new UTF8Encoding();
                            encorder.GetBytes(Utility.PathToFileName(currentImagePath)).CopyTo(bytes, 1);

                            byte[] hash;
                            using (SHA256 sha = SHA256.Create()) {
                                FileStream file = File.OpenRead(currentImagePath);
                                hash = sha.ComputeHash(file);
                                file.Close();
                            }

                            // Apertura file immagine
                            using (var file = new FileStream(currentImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                                // Successivi 8 bytes : dimensione file
                                BitConverter.GetBytes(file.Length).CopyTo(bytes, 257);
                                hash.CopyTo(bytes, 265);

                                //Accesso network stream del client...
                                stream = client.GetStream();

                                // Primi 265 byte di header
                                await stream.WriteAsync(bytes, 0, 297).ConfigureAwait(false);

                                // Successivi 64K di payload (immagine di profilo)
                                bytes = new byte[bufferSize * 64];
                                await file.CopyToAsync(stream, bufferSize).ConfigureAwait(false);
                            }
                            break;
                        }
                        catch (SocketException e) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendProfilePicture - {e.Message}");

                            // In caso l'host si sia disconnesso mentre si inviava la richiesta si passa al successivo, appena ritornerà
                            // attivo si invierà la richiesta di invio di nuovo
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
        /// Invio file all'utente che ha inviato risposta positiva alla ricezione del file
        /// </summary>
        /// <param name="filename">Path del file confermato</param>
        /// <param name="ip">Ip dell'utente che ha confermato</param>
        /// <returns></returns>
        public async Task SendFile (string filename, string ip) {
            // Il file verrà inviato solo se esiste all'interno della struttura dati FileToFinish e se è stato confermato o se è da rinviare
            if (!_referenceData.CheckPacketSendFileStatus(ip, filename)) return;
            if (_referenceData.GetUserStatus(ip).Equals("offline")) {
                _referenceData.UpdateSendStatusFileForUser(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT);

                await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT, "-", 0);
                }));
                return;
            }
            IPAddress serverAddr = IPAddress.Parse(ip);
            int attempts = 0;

            // In caso di eccezione, prova a rinviare il pacchetto n volte
            do {
                TcpClient client = null;
                NetworkStream stream = null;

                if (!_referenceData.CheckPacketSendFileStatus(ip, filename)) return;

                try {
                    attempts++;
                    client = new TcpClient();
                    await client.ConnectAsync(serverAddr.ToString(), _referenceData.TCPPort).ConfigureAwait(false);

                    // La dimensione massima del nome del file è di 256 bytes, mentre la dimensione del file è di 8 bytes
                    byte[] bytes = new byte[1 + 256 + 8];

                    // Primo byte: tipo pacchetto
                    bytes[0] = (byte)PacketType.FSEND;

                    // Successivi 256 bytes : nome file
                    UTF8Encoding encorder = new UTF8Encoding();
                    encorder.GetBytes(Utility.PathToFileName(filename)).CopyTo(bytes, 1);

                    // Apertura file immagine
                    filename = Utility.PathTmp() + "\\" + filename;
                    using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        // Successivi 8 bytes : dimensione file
                        BitConverter.GetBytes(file.Length).CopyTo(bytes, 257);

                        // Accesso network stream del client...
                        stream = client.GetStream();

                        // Primi 265 byte di header
                        await stream.WriteAsync(bytes, 0, 265).ConfigureAwait(false);
                        _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.INPROGRESS);

                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.INPROGRESS, "-", 0);
                        }));

                        // Successivi 64K di payload (immagine di profilo)
                        bytes = new byte[bufferSize * 64];
                        await file.CopyToAsync(stream, bufferSize).ConfigureAwait(false);
                    }
                    _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.END);

                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.END, "-", 0);
                    }));
                    break;
                }
                catch (SocketException e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendFile - {e.Message}");

                    // In caso l'host si sia disconnesso mentre si inviava la richiesta si passa al successivo, appena ritornerà
                    // attivo si invierà la richiesta di invio di nuovo
                    string CurrentUserStatus = _referenceData.GetUserStatus(serverAddr.ToString());
                    if (!CurrentUserStatus.Equals("") && CurrentUserStatus.Equals("offline")) {
                        _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.RESENT);

                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT, "-", 0);
                        }));

                        File.Delete(filename);
                        break;
                    }
                    else if (attempts == 3) {
                        _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.RESENT);

                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT, "-", 0);
                        }));

                        File.Delete(filename);
                        break;
                    }
                    else
                        await Task.Delay(10).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - SocketException on SendFile - {e.Message}");
                    if (attempts == 3) {
                        _referenceData.UpdateSendStatusFileForUser(serverAddr.ToString(), Utility.PathToFileName(filename), FileSendStatus.RESENT);
                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(filename), FileSendStatus.RESENT, "-", 0);
                        }));
                        File.Delete(filename);
                        break;
                    }
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

