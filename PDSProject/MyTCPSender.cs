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
                    Console.WriteLine($"SocketException on SendResponse - {e}");

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
                    Console.WriteLine($"Exception on SendResponse - {e}");
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
                Console.WriteLine($"Exception on SendRequest - {e}");
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
                        Console.WriteLine($"SocketException on SendSetFiles - {e}");

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
                        Console.WriteLine($"Exception on SendSetFiles - {e}");
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
                                Console.WriteLine(BitConverter.ToString(hash));
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
                            Console.WriteLine($"SocketException on SendProfilePicture - {e}");

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
                            Console.WriteLine($"Exception on SendProfilePicture - {e}");
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

            IPAddress serverAddr = IPAddress.Parse(ip);
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
                catch (SocketException e)
                {
                    Console.WriteLine($"SocketException on SendFile - {e}");

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
                    Console.WriteLine($"SocketException on SendFile - {e}");
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

        //public async Task SendA (List<string> filenames, bool isProfile)
        //{
        //    // TODO: da cambiare!!!
        //    IPAddress serverAddr;
        //    if (!isProfile)//!_referenceData.hasChangedProfileImage)
        //    {
        //        //foreach (string path in filenames) {
        //        if (_referenceData.selectedHosts.Count <= 0) return;
        //        List<string> currentlySelectedHost;
        //        lock (_referenceData.selectedHosts) {
        //            currentlySelectedHost = _referenceData.selectedHosts.ToList();
        //        }
        //        List<Task> test = new List<Task>();

        //        foreach (string ip in currentlySelectedHost) {
        //            serverAddr = IPAddress.Parse(ip);
        //            test.Add(SendListFiles(serverAddr, filenames, isProfile));

        //        }
        //        await Task.WhenAll(test);
        //        //if (_referenceData.selectedHost.Equals("")) return;
        //        //    serverAddr = IPAddress.Parse(_referenceData.selectedHost);
        //        //    await SendListFiles(serverAddr, filenames, isProfile);
        //        //}

        //    }
        //    else
        //    {
        //        List<Host> copyDictionary;
        //        lock (_referenceData.Users) {
        //            copyDictionary = _referenceData.Users.Values.ToList();
        //        }
        //        foreach (Host host in copyDictionary) {
        //            //if (!_referenceData.FileToFinish.ContainsKey(filenames[0]))
        //            //    _referenceData.FileToFinish.GetOrAdd(filenames[0], "start");
        //            serverAddr = IPAddress.Parse(host.Ip);//_referenceData.Users.First().Key);//"192.168.1.69");
        //            //await
        //            await SendListFiles(serverAddr, filenames, isProfile).ConfigureAwait(continueOnCapturedContext: false);
        //        }
        //    }
        //    //TcpClient client = null;

        //    /*try
        //    {
        //        foreach (string path in filenames)
        //        {
        //            Console.WriteLine("Send " + path + " to user " + serverAddr.ToString());
        //            client = new TcpClient(serverAddr.ToString(), _referenceData.TCPPort);
        //            UTF8Encoding encoder = new UTF8Encoding();
        //            FileStream file = File.OpenRead(@path);//"Risultati.pdf");
        //            // Invio primo pacchetto con nome e dimensione
        //            // TODO: vedere altro carattere di separazione che non sia lo spazio, potrebbe essere usato dentro il file
        //            long dim = file.Length;
        //            string firstmsg = "";
        //            // Da cambiare
        //            if (_referenceData.hasChangedProfileImage)
        //            {
        //                firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)
        //                _referenceData.hasChangedProfileImage = false;
        //            }
        //            string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);
        //            firstmsg += infoImage[infoImage.Length - 1] + " " + dim;
                    
        //            byte[] bytes = new byte[bufferSize];
        //            encoder.GetBytes(firstmsg).CopyTo(bytes, 0);
        //            Random rand = new Random();
        //            for (int i = 0; i < (bufferSize - encoder.GetByteCount(firstmsg)); i++)
        //            {
        //                byte b = 1;
        //                bytes.Append(b);
        //            }
        //            NetworkStream stream = client.GetStream();
        //            await stream.WriteAsync(bytes, 0, bufferSize);
        //            // Invio effettivo del file
        //            //bytes = new byte[bufferSize * 64];
        //            long numbPackets = dim / (bufferSize*64);
        //            for (int i = 0; i <= numbPackets; i++)
        //            {
        //                bytes = new byte[bufferSize * 64];
        //                file.Read(bytes, 0, bytes.Length);
        //                await stream.WriteAsync(bytes, 0, bytes.Length);
        //            }
        //            file.Close();
        //            stream.Close();
        //            _referenceData.FileToFinish.Remove(path);
        //        }
        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine($"SocketException: {e}");
        //    }
        //    catch (Exception e)
        //    {
        //        Console.WriteLine($"Exception: {e}");
        //    }
        //    finally
        //    {
        //        client.Close();
        //    }*/
        //}


        //async Task SendListFiles (IPAddress serverAddr, List<string> filenames, bool isProfile)
        //{
        //    try
        //    {

        //        foreach (string path in filenames)
        //        {
        //            if (!isProfile)
        //                if (_referenceData.FileToSend.ContainsKey(path) && _referenceData.FileToSend[path].Equals("inprogress")) continue;
        //            if (Utility.PathToFileName(path).Equals(_referenceData.defaultImage)) continue;
        //            Console.WriteLine("Send " + path + " to user " + serverAddr.ToString());
        //            TcpClient client = new TcpClient(); //serverAddr.ToString(), _referenceData.TCPPort);
        //            await client.ConnectAsync(serverAddr.ToString(), _referenceData.TCPPort).ConfigureAwait(false);
        //            UTF8Encoding encoder = new UTF8Encoding();
        //            //FileStream file = File.OpenRead(path);//"Risultati.pdf");
        //            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        //            {
        //                // Invio primo pacchetto con nome e dimensione
        //                // TODO: vedere altro carattere di separazione che non sia lo spazio, potrebbe essere usato dentro il file
        //                long dim = file.Length;
        //                string firstmsg = "";

        //                // Da cambiare
        //                /*if (_referenceData.hasChangedProfileImage)
        //                {
        //                    firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)
        //                    _referenceData.hasChangedProfileImage = false;
        //                }*/

        //                if (isProfile) firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)

        //                string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);
        //                firstmsg += infoImage[infoImage.Length - 1] + " " + dim;

        //                /* 
        //                 * Questa roba merita purtroppo 2 parole:
        //                 * Il primo pacchetto della sequenza ha solo nome + dimensione e poi è rimempito di byte a caso.
        //                 * Questo perchè a volte il pacchetto iniziale era vuoto e a volte aveva l'inizio del file.
        //                 * Per evitare casini ho fatto la cosa più stupida. Se si possono trovare altre soluzioni sono ben accette
        //                 */
        //                byte[] bytes = new byte[bufferSize];
        //                encoder.GetBytes(firstmsg).CopyTo(bytes, 0);
        //                Random rand = new Random();
        //                for (int i = 0; i < (bufferSize - encoder.GetByteCount(firstmsg)); i++)
        //                {
        //                    byte b = 1;
        //                    bytes.Append(b);
        //                }
        //                NetworkStream stream = client.GetStream();
        //                await stream.WriteAsync(bytes, 0, bufferSize).ConfigureAwait(false);
        //                if (!isProfile)
        //                {
        //                    Dictionary<string, FileSendStatus> test;
        //                    _referenceData.FileToSend.TryGetValue(serverAddr.ToString(), out test);
        //                    _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), (key) => test, (key, oldValue) => { oldValue[path] = FileSendStatus.INPROGRESS; return oldValue; });
        //                }
        //                // Invio effettivo del file
        //                //bytes = new byte[bufferSize * 64];
        //                long numbPackets = dim / (bufferSize * 64);

        //                bytes = new byte[bufferSize * 64];
        //                await file.CopyToAsync(stream, bufferSize).ConfigureAwait(false);
        //                /*for (int i = 0; i <= numbPackets; i++)
        //                    {
        //                        bytes = new byte[bufferSize * 64];
        //                        await file.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        //                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        //                    }*/
        //                stream.Close();
        //            }
        //            //file.Close();
        //            //stream.Flush();
        //            //stream.Close();

        //            //string removeValue = "";
        //            //_referenceData.FileToFinish.TryRemove(path, out removeValue);
        //            //_referenceData.FileToFinish[path] = "inprogress";
        //            if (!isProfile) {
        //                Dictionary<string, FileSendStatus> test;
        //                _referenceData.FileToSend.TryGetValue(serverAddr.ToString(), out test);
        //                _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), (key) => test, (key, oldValue) => { oldValue[path] = FileSendStatus.END; return oldValue; });
        //            }

        //            Console.WriteLine("Fine invio file " + path);

        //            client.Close();
        //        }
        //        // OLD CODE
        //        /*Byte[] bytes = encoder.GetBytes(message);
        //        NetworkStream stream = client.GetStream();
        //        stream.Write(bytes, 0, bytes.Length);
        //        Console.WriteLine($"Send {message} to 192.168.1.69");
        //        stream.Close();
        //        client.Close();
        //        */

        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine($"SocketException: {e}");
        //    }
        //    catch (Exception e)
        //    {
        //        Console.WriteLine($"Exception: {e}");

        //    }
        //}

    /// <summary>
    /// Invia file ad uno o più host.
    /// Per ora invia un singolo file ad un singolo host.
    /// TODO: da fare gestiore di più file e verso più host
    /// </summary>
    /// <param name="message">Nome del file (path assoluto)</param>
    /// TODO: modificare la roba del path, da fare funzioncina che separa ed ottine solo il nome del file
    //public void Send (Object result)//List<string> filenames )
    //{
    //    // TODO: da cambiare!!!
    //    IPAddress serverAddr = null;
    //    if (!_referenceData.hasChangedProfileImage)
    //    {
    //        if (_referenceData.selectedHosts.Count <= 0) return;
    //        List<string> currentlySelectedHost = _referenceData.selectedHosts.ToList();
    //        foreach (string ip in currentlySelectedHost)
    //        {
    //            serverAddr = IPAddress.Parse(ip);
    //        }
    //    }
    //    else
    //    {
    //        serverAddr = IPAddress.Parse(_referenceData.Users.First().Key);//"192.168.1.69");
    //    }
    //    TcpClient client = null;
    //    List<string> filenames = (List<string>)result;
    //    try
    //    {

    //        foreach (string path in filenames)
    //        {
    //            if (_referenceData.FileToSend[path].Equals("inprogress")) continue;

    //            Console.WriteLine("Send " + path + " to user " + serverAddr.ToString());
    //            client = new TcpClient(serverAddr.ToString(), _referenceData.TCPPort);

    //            UTF8Encoding encoder = new UTF8Encoding();
    //            FileStream file = File.OpenRead(@path);//"Risultati.pdf");

    //            // Invio primo pacchetto con nome e dimensione
    //            // TODO: vedere altro carattere di separazione che non sia lo spazio, potrebbe essere usato dentro il file
    //            long dim = file.Length;
    //            string firstmsg = "";

    //            // Da cambiare
    //            if (_referenceData.hasChangedProfileImage)
    //            {
    //                firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)
    //                _referenceData.hasChangedProfileImage = false;
    //            }

    //            string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);
    //            firstmsg += infoImage[infoImage.Length - 1] + " " + dim;

    //            /* 
    //             * Questa roba merita purtroppo 2 parole:
    //             * Il primo pacchetto della sequenza ha solo nome + dimensione e poi è rimempito di byte a caso.
    //             * Questo perchè a volte il pacchetto iniziale era vuoto e a volte aveva l'inizio del file.
    //             * Per evitare casini ho fatto la cosa più stupida. Se si possono trovare altre soluzioni sono ben accette
    //             */
    //            byte[] bytes = new byte[bufferSize];
    //            encoder.GetBytes(firstmsg).CopyTo(bytes, 0);
    //            Random rand = new Random();
    //            for (int i = 0; i < (bufferSize - encoder.GetByteCount(firstmsg)); i++)
    //            {
    //                byte b = 1;
    //                bytes.Append(b);
    //            }
    //            NetworkStream stream = client.GetStream();
    //            stream.Write(bytes, 0, bufferSize);
    //            Dictionary<string, FileSendStatus> test;
    //            //_referenceData.FileToFinish[path] = "inprogress";
    //            _referenceData.FileToSend.TryGetValue(serverAddr.ToString(), out test);
    //            _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), (key) => test, (key, oldValue) => { oldValue[path] = FileSendStatus.INPROGRESS; return oldValue; });

    //            /*lock (_referenceData.FileToFinish[serverAddr.ToString()])
    //            {
    //                test[path] = "inprogress";
    //                _referenceData.FileToFinish.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => { oldValue[path] = "inprogress"; return oldValue; });
    //            }*/

    //            // Invio effettivo del file
    //            //bytes = new byte[bufferSize * 64];
    //            long numbPackets = dim / (bufferSize * 64);
    //            for (int i = 0; i <= numbPackets; i++)
    //            {
    //                bytes = new byte[bufferSize * 64];
    //                file.Read(bytes, 0, bytes.Length);
    //                stream.Write(bytes, 0, bytes.Length);
    //            }
    //            file.Close();
    //            stream.Close();
    //            stream.Flush();
    //            string removeValue = "";
    //            /*Dictionary<string, string> test;
    //            _referenceData.FileToFinish.TryGetValue(serverAddr.ToString(), out test);// TryRemove(path, out removeValue);
    //            lock (_referenceData.FileToFinish[serverAddr.ToString()]){
    //                test.Remove(path);
    //                _referenceData.FileToFinish.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => test);
    //            }*/
    //            _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), (key) => test, (key, oldValue) => { oldValue.Remove(path); return oldValue; });

    //            Console.WriteLine("Fine invio file " + path + "  stato:" + removeValue);
    //            client.Close();

    //        }
    //        //_referenceData.PathFileToSend.Clear();
    //        // OLD CODE
    //        /*Byte[] bytes = encoder.GetBytes(message);
    //        NetworkStream stream = client.GetStream();
    //        stream.Write(bytes, 0, bytes.Length);
    //        Console.WriteLine($"Send {message} to 192.168.1.69");
    //        stream.Close();
    //        client.Close();
    //        */
    //    }
    //    catch (SocketException e)
    //    {
    //        Console.WriteLine($"SocketException: {e}");
    //    }
    //    catch (Exception e)
    //    {
    //        Console.WriteLine($"Exception: {e}");

    //    }
    //    finally
    //    {
    //        if (client != null)
    //            client.Close();
    //    }
    //}
}
}


/////////////////////////// OLD /////////////////////////////
///// <summary>
///// Invia file ad uno o più host.
///// Per ora invia un singolo file ad un singolo host.
///// TODO: da fare gestiore di più file e verso più host
///// </summary>
///// <param name="message">Nome del file (path assoluto)</param>
///// TODO: modificare la roba del path, da fare funzioncina che separa ed ottine solo il nome del file
//public void Send ( Object result)//List<string> filenames )
//{
//    // TODO: da cambiare!!!
//    IPAddress serverAddr = null;
//    if (!_referenceData.hasChangedProfileImage)
//    {
//        if (_referenceData.selectedHosts.Count <= 0) return;
//        List<string> currentlySelectedHost = _referenceData.selectedHosts.ToList();
//        foreach (string ip in currentlySelectedHost) {
//            serverAddr = IPAddress.Parse(ip);
//        }
//    }
//    else
//    {
//        serverAddr = IPAddress.Parse(_referenceData.Users.First().Key);//"192.168.1.69");
//    }
//    TcpClient client = null;
//    List<string> filenames = (List<string>) result;
//    try
//    {

//        foreach (string path in filenames)
//        {
//            if (_referenceData.FileToSend[path].Equals("inprogress")) continue;

//            Console.WriteLine("Send " + path + " to user " + serverAddr.ToString());
//            client = new TcpClient(serverAddr.ToString(), _referenceData.TCPPort);

//            UTF8Encoding encoder = new UTF8Encoding();
//            FileStream file = File.OpenRead(@path);//"Risultati.pdf");

//            // Invio primo pacchetto con nome e dimensione
//            // TODO: vedere altro carattere di separazione che non sia lo spazio, potrebbe essere usato dentro il file
//            long dim = file.Length;
//            string firstmsg = "";

//            // Da cambiare
//            if (_referenceData.hasChangedProfileImage)
//            {
//                firstmsg += "CHIMAGE "; //Da verificare come inviare il nome del file (NO indirizzo assoluto)
//                _referenceData.hasChangedProfileImage = false;
//            }

//            string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);
//            firstmsg += infoImage[infoImage.Length - 1] + " " + dim;

//            /* 
//             * Questa roba merita purtroppo 2 parole:
//             * Il primo pacchetto della sequenza ha solo nome + dimensione e poi è rimempito di byte a caso.
//             * Questo perchè a volte il pacchetto iniziale era vuoto e a volte aveva l'inizio del file.
//             * Per evitare casini ho fatto la cosa più stupida. Se si possono trovare altre soluzioni sono ben accette
//             */
//            byte[] bytes = new byte[bufferSize];
//            encoder.GetBytes(firstmsg).CopyTo(bytes, 0);
//            Random rand = new Random();
//            for (int i = 0; i < (bufferSize - encoder.GetByteCount(firstmsg)); i++)
//            {
//                byte b = 1;
//                bytes.Append(b);
//            }
//            NetworkStream stream = client.GetStream();
//            stream.Write(bytes, 0, bufferSize);
//            Dictionary<string, FileSendStatus> test;
//            //_referenceData.FileToFinish[path] = "inprogress";
//            _referenceData.FileToSend.TryGetValue(serverAddr.ToString(), out test);
//            _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => { oldValue[path] = FileSendStatus.INPROGRESS; return oldValue; });

//            /*lock (_referenceData.FileToFinish[serverAddr.ToString()])
//            {
//                test[path] = "inprogress";
//                _referenceData.FileToFinish.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => { oldValue[path] = "inprogress"; return oldValue; });
//            }*/

//            // Invio effettivo del file
//            //bytes = new byte[bufferSize * 64];
//            long numbPackets = dim / (bufferSize*64);
//            for (int i = 0; i <= numbPackets; i++)
//            {
//                bytes = new byte[bufferSize*64];
//                file.Read(bytes, 0, bytes.Length);
//                stream.Write(bytes, 0, bytes.Length);
//            }
//            file.Close();
//            stream.Close();
//            stream.Flush();
//            string removeValue = "";
//            /*Dictionary<string, string> test;
//            _referenceData.FileToFinish.TryGetValue(serverAddr.ToString(), out test);// TryRemove(path, out removeValue);
//            lock (_referenceData.FileToFinish[serverAddr.ToString()]){
//                test.Remove(path);
//                _referenceData.FileToFinish.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => test);
//            }*/
//            _referenceData.FileToSend.AddOrUpdate(serverAddr.ToString(), ( key ) => test, ( key, oldValue ) => { oldValue.Remove(path); return oldValue; });

//            Console.WriteLine("Fine invio file " + path + "  stato:" + removeValue);
//            client.Close();

//        }
//        //_referenceData.PathFileToSend.Clear();
//        // OLD CODE
//        /*Byte[] bytes = encoder.GetBytes(message);
//        NetworkStream stream = client.GetStream();
//        stream.Write(bytes, 0, bytes.Length);
//        Console.WriteLine($"Send {message} to 192.168.1.69");
//        stream.Close();
//        client.Close();
//        */
//    }
//    catch (SocketException e)
//    {
//        Console.WriteLine($"SocketException: {e}");
//    }
//    catch (Exception e)
//    {
//        Console.WriteLine($"Exception: {e}");

//    }
//    finally
//    {
//        if (client != null)
//            client.Close();
//    }
//}

//public void SendCallback ()
//{
//    if (_referenceData.CallBackIPAddress.Equals("")) return;
//    IPAddress serverAddr = IPAddress.Parse(_referenceData.CallBackIPAddress);
//    TcpClient client = null;

//    try
//    {
//        client = new TcpClient(serverAddr.ToString(), _referenceData.TCPPort);
//        NetworkStream stream = client.GetStream();
//        UTF8Encoding encoder = new UTF8Encoding();
//        byte[] bytes = new byte[bufferSize];
//        encoder.GetBytes("CHNETWORK ").CopyTo(bytes, 0);
//        stream.Write(bytes, 0, bufferSize);
//        stream.Close();
//    }
//    catch (SocketException e)
//    {
//        Console.WriteLine($"SocketException: {e}");
//    }
//    finally
//    {
//        client.Close();
//    }
//}