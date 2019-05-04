using System;

using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

using System.Collections.Generic;
using System.Collections.Concurrent;

using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.ObjectModel;

namespace PDSProject
{
    /// <summary>
    /// Mantiene le informazioni condivise da ogni classe del programma.
    /// Per garantire che esista una sola istanza durante l'esecuzione del programma è stata implementata come singleton 
    /// utilizzando la classe Lazy<T>. Questa garantisce che venga sempre inizializzata una singola istanza anche in caso di
    /// esecuzione concorrente.
    /// </summary>


    public enum FileSendStatus {
        PREPARED,
        READY,
        INPROGRESS,
        REJECTED,
        CONFERMED,
        END,
        RESENT
    }

    public enum FileRecvStatus {
        TOCONF,
        YSEND,
        NSEND,
        RECIVED,
        INPROGRESS,
        RESENT
    }

    public class SharedInfo {
        // Singleton 
        //---------------------------------------------
        private static readonly Lazy<SharedInfo> _singleton = new Lazy<SharedInfo>(() => new SharedInfo());

        // Hosts in network 
        //---------------------------------------------
        public Dictionary<string, Host> Users = new Dictionary<string, Host>();
        public List<string> selectedHosts = new List<string>();
        public Dictionary<string, string> UserImageChange = new Dictionary<string, string>(); // Key = hash - Value = namefile

        // Info for the current user
        //---------------------------------------------
        public CurrentHostProfile LocalUser = new CurrentHostProfile();
        
        //Default Value
        public string defaultImage = "user.png"; 
        public string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).ToString();
        string currentHostPath = Utility.PathResources() + "\\userProfile.json";

        // Network Confugration
        //---------------------------------------------
        public Int32 TCPPort = 13000;
        public Int32 UDPReceivedPort = 20000;

        // Current network information 
        public string LocalIPAddress = "";
        public string BroadcastIPAddress = "";

        // Data structures di supporto da usare nella ricerca della sottorete in cui sono presenti degli Host
        public List<string> LocalIps = new List<string>();
        public List<string> BroadcastIps = new List<string>();
        public Dictionary<string, string> Ips =new Dictionary<string, string>();
        public object lockIps = new object();

        // TCP listener Condition Variable in case of changed network
        public object cvListener = new object();
        public volatile bool isFirst = true;

        // Files to send information
        //---------------------------------------------
        public List<string> PathFileToSend = new List<string>();
        public ConcurrentDictionary<string, Dictionary<string, FileSendStatus>> FileToSend = new ConcurrentDictionary<string, Dictionary<string, FileSendStatus>>();

        // File to recive information
        //---------------------------------------------
        public ConcurrentDictionary<string, Dictionary<string, FileRecvStatus>> FileToRecive = new ConcurrentDictionary<string, Dictionary<string, FileRecvStatus>>();
        public ObservableCollection<FileRecive> fileReciveList = new ObservableCollection<FileRecive>();
        
        /// <summary>
        /// Costruttore privato, evita che possano esistere più istanze della stessa classe 
        /// </summary>
        private SharedInfo () {
            // Controlla se esiste già un profilo dell'utente corrente, se noi lo crea
            if (File.Exists(currentHostPath)) {
                lock (LocalUser) {
                    // Operaizione di deserializzazione
                    DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                    using (var stream = File.OpenRead(currentHostPath)) {
                        stream.Position = 0;
                        LocalUser = (CurrentHostProfile)sr.ReadObject(stream);
                    }
                }
            }
            else {
                lock (LocalUser) {
                    // Nel caso non sia presente un file JSON (prima accensione) ne genera uno di default.
                    LocalUser.Name = "Username";
                    LocalUser.Status = "offline";
                    LocalUser.AcceptAllFile = false;
                    LocalUser.SavePath = defaultPath;

                    // Crea l'hash dell'immagine di default
                    using (SHA256 sha = SHA256.Create()) {
                        string file_name = Utility.FileNameToHost(defaultImage);
                        FileStream file = File.OpenRead(file_name);

                        // Calcolo effettivo dell'hash
                        byte[] hash = sha.ComputeHash(file);
                        LocalUser.ProfileImageHash = BitConverter.ToString(hash).Replace("-", String.Empty);
                        LocalUser.ProfileImagePath = defaultImage;
                    }

                    // Operazione di deserializzazione
                    DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                    using (var stream = File.Create(currentHostPath)) {
                        sr.WriteObject(stream, LocalUser);
                    }
                }
            }
            // Aggiunto come delegato il metodo 'AddressChangedCallback' in caso di cambio di rete
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback); 
            FindAllNetworkInterface();
        }

        /// <summary>
        /// Proprietà che ritorna una copia del rifierimento all'istanza
        /// </summary>
        public static SharedInfo Instance {
            get {
                return _singleton.Value;
            }
        }

        /// NETWORK CONFIGURATION METHODS ///
        ///-------------------------------///

        /// <summary>
        /// Per ogni cambio controlla per ogni interfaccia di rete non virtuale o non di callback l'indirizzo IP locale e calcola il corrispondende multicast
        /// </summary>
        private void FindAllNetworkInterface() {
            // Prima di tutto pulisco le strutture dati di supporto
            lock (Users) {
                // Lock usato per assicurare la pulizia della struttura dati in mutua esclusione
                Users.Clear();
            }
            
            // Lock usato per assicurare l'accesso alle strutture dati di supporto per la ricerca dei vari indirizzi
            // delle interfaccie di rete che dispone il sistema 
            lock (lockIps) {
                Ips.Clear();
                LocalIPAddress = "";
                BroadcastIPAddress = "";

                // Listo tutte i possibili IP delle Network Interface attive sul dispositivo che non siano Loopback o Virtuali
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (item.OperationalStatus == OperationalStatus.Up &&
                        !item.Description.Contains("Virtual") &&
                        !item.Description.Contains("Loopback")) {

                        foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses) {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                                Console.WriteLine("Local Ip on " + item.NetworkInterfaceType + " LAN:" + ip.Address.ToString());


                                if (!LocalIps.Contains(ip.Address.ToString()))
                                    LocalIps.Add(ip.Address.ToString());

                                string BroadcastIPAddress = Utility.GetMulticastAddress(ip.Address.ToString());

                                if (!BroadcastIps.Contains(BroadcastIPAddress))
                                    BroadcastIps.Add(BroadcastIPAddress);

                                if (!Ips.ContainsKey(ip.Address.ToString()))
                                    Ips.Add(BroadcastIPAddress, ip.Address.ToString());
                                else
                                    Ips[BroadcastIPAddress] = ip.Address.ToString();

                                Console.WriteLine("Multicast address on " + item.NetworkInterfaceType + " LAN:" + BroadcastIPAddress);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Metodo invocato ad ogni cambio di rete
        /// </summary>
        static void AddressChangedCallback(object sender, EventArgs e) {
            Console.WriteLine("AddressShcangedCallback");
            Instance.FindAllNetworkInterface();
        }

        /// <summary>
        /// Controlla se c'è adesso una configurazione di rete
        /// </summary>
        /// <returns></returns>
        public bool CheckIfConfigurationIsSet () {
            lock (lockIps) {
                if (LocalIPAddress.Equals("") && BroadcastIPAddress.Equals(""))
                    return false;
                return true;
            }
        }

        /// <summary>
        /// Ritorna la lista di tutti gli IPs delle varie interfaccie di rete
        /// </summary>
        /// <returns></returns>
        public List<string> GetListIps () {
            lock (lockIps) {
                return Ips.Keys.ToList();
            }
        }
        
        /// <summary>
        /// Ritorna copia dell'indirizzo multicast corrente
        /// </summary>
        /// <returns></returns>
        public string GetBroadcastIPAddress() {
            lock (lockIps) {
                return BroadcastIPAddress;
            }
        }

        /// <summary>
        /// Ritorna copia dell'indirizzo corrente
        /// </summary>
        /// <returns></returns>
        public string GetLocalIPAddress () {
            lock (lockIps) {
                return LocalIPAddress;
            }
        }

        /// <summary>
        /// Aggirona configurazioni di rete
        /// </summary>
        /// <param name="ip">Ip locale a cui si è ricevuto risposta</param>
        /// <returns>Ritorna true se l'ip è di una delle interfaccie di rete della macchina, falso altrimenti</returns>
        public bool UpdateNetworkConfiguration ( string ip ) {
            lock (lockIps) {
                // Accesso in mutua esclusione alle strutture dati
                if (LocalIPAddress.Equals("") && BroadcastIPAddress.Equals("")) {
                    // Solo nel caso ci sia almeno un indirizzo di rete...
                    if (Ips.Count > 0) {

                        // Controllo se l'IP dell'host di cui ho ricevuto il pacchetto ha l'IP di una delle interfaccie di rete
                        // della macchina corrente
                        string MulticastAddrs = Utility.GetMulticastAddress(ip);
                        if (BroadcastIps.Contains(MulticastAddrs)) {
                            BroadcastIPAddress = MulticastAddrs;
                            LocalIPAddress = Ips[MulticastAddrs];

                            Console.WriteLine("Find subnet with multicast address: " + MulticastAddrs);
                            return true;
                        }
                    }
                }
                return false;
            }
        }


        
        /// LOCAL USER CONFIGURATION METHODS ///
        ///---------------------------------///

        /// <summary>
        /// Aggiorna le informazioni del profilo utente salvate sul file JSON
        /// </summary>
        public void SaveJson(){
            try {
                DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                using (var stream = File.Create(currentHostPath)) {
                    sr.WriteObject(stream, LocalUser);
                }
            }catch(Exception e) {
                Console.WriteLine($"On SaveJson method on SharedInfo - Exception thrown: {e}");
                throw e;
            }
        }

        /// <summary>
        /// Aggiorna le informazioni dell'utente locale tranne lo status
        /// </summary>
        /// <param name="name">Nuovo nome utente</param>
        /// <param name="pathImage">Nuovo path immagine profilo</param>
        /// <param name="pathSave">Nuovo path di salvataggio file</param>
        /// <param name="isPathDefault">Bool per controllare se path default o no</param>
        /// <param name="acceptAll">Bool che definisce la configurazione di salvataggio automatico</param>
        public void UpdateInfoLocalUser(string name, string pathImage, string pathSave, bool? isPathDefault, bool? acceptAll) {
            lock (LocalUser) {
                LocalUser.Name = name.Equals("") ? LocalUser.Name : name;
                LocalUser.AcceptAllFile = acceptAll == null ? LocalUser.AcceptAllFile : acceptAll.Value;
                if (isPathDefault != null) {
                    if (!isPathDefault.Value)
                        LocalUser.SavePath = pathSave.Equals("") ? defaultPath : pathSave;
                    else
                        LocalUser.SavePath = defaultPath;
                }
                // Contolla se l'immagine è la stessa (controlla hash)
                if (!pathImage.Equals("")) {
                    using (SHA256 sha = SHA256.Create()) {
                        FileStream file = File.OpenRead(pathImage);
                        byte[] hash = sha.ComputeHash(file);
                        if (!BitConverter.ToString(hash).Replace("-", String.Empty)
                            .Equals(LocalUser.ProfileImageHash)) {

                            string hashString = BitConverter.ToString(hash).Replace("-", String.Empty);

                            if (!hashString.Equals(LocalUser.ProfileImageHash)) {
                                LocalUser.ProfileImageHash = hashString;
                                LocalUser.ProfileImagePath = pathImage;
                            }
                        }
                    }
                }
                try {
                    SaveJson();
                }
                catch(Exception e) {
                    Console.WriteLine($"On UpdateInfoLocalUser method on SharedInfo - Exception thrown: {e}");
                    throw e;
                }
            }
        }

        /// <summary>
        /// Aggiorna lo satus dell'utente corrente
        /// </summary>
        /// <param name="status">Nuovo status (online/offline)</param>
        public void UpdateStatusLocalUser ( string status ) {
            lock (LocalUser) {
                LocalUser.Status = status.Equals("") ? LocalUser.Status : status;
                try {
                    SaveJson();
                }
                catch (Exception e) {
                    Console.WriteLine($"On UpdateStatusLocalUser method on SharedInfo - Exception thrown: {e}");
                    throw e;
                }
            }
        }

        /// <summary>
        /// Ritorna l'oggetto currentHost corrente
        /// </summary>
        public CurrentHostProfile GetInfoLocalUser () {
            CurrentHostProfile returnData;
            lock (LocalUser) {
                returnData = LocalUser;
            }
            return returnData;
        }


        /// REMOTE USER CONFIGURATION METHODS ///
        ///-----------------------------------///

        /// <summary>
        /// Aggirona od inserisce informazioni di un'utente remoto
        /// </summary>
        /// <param name="host">Istanza Host</param>
        /// <param name="ip">Ip Host remoto</param>
        /// <returns>Ritorna true se l'host è stato aggiornato o inserito, falso altrimenti</returns>
        public bool UpdateUsersInfo ( Host host, string ip ) {
            lock (Users) {
                bool IsUserUpdate = false;
                // Aggiungo informazioni utili oggetto dati Host
                string Path = Utility.PathHost() + "\\" + Utility.PathToFileName(host.ProfileImagePath);
                try {
                    File.OpenRead(Path);
                }
                catch (Exception) {
                    Path = Utility.PathHost() + "\\" + defaultImage;
                    if(Users.ContainsKey(ip))
                        host.ProfileImageHash = Users[ip].ProfileImageHash;
                }
                host.ProfileImagePath = Path;
                host.Ip = ip;
                host.LastPacketTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                // Caso Utente già presente
                if (Users.ContainsKey(ip)) {
                    if (!Users[ip].Equals(host)) {
                        Console.WriteLine("Aggiornamento info utente " + ip);
                        Users[ip] = host;
                        IsUserUpdate = true;
                    }
                    else
                        Users[ip].LastPacketTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }
                // Caso Host mom ancora presente
                else {
                    host.ProfileImageHash = "";
                    Console.WriteLine("Connesso nuovo utente " + ip);
                    Users[ip] = host;
                    IsUserUpdate = true;
                }

                lock (UserImageChange) {
                    if(UserImageChange.ContainsKey(host.ProfileImageHash))
                        Users[ip].ProfileImagePath = UserImageChange[host.ProfileImageHash];
                }

                return IsUserUpdate;
            }
        }

        /// <summary>
        /// Aggirona info immagine di profilo utente. In caso non siano state ricevute ancora informazioni utente
        /// questo viene salvato su una struttura dati apposita
        /// </summary>
        /// <param name="newHash">Valore hash dell'immagine di profilo</param>
        /// <param name="newPath">Path immagine di profilo</param>
        /// <param name="ip">Ip host remoto</param>
        /// <returns>Ritorna true se l'immagine di profilo dell'host è stata aggiornata, falso altrimenti</returns>
        public bool ProfileImageUpdate(string newHash, string newPath, string ip ){
            lock (Users) {
                if (Users.ContainsKey(ip) && Users[ip].ProfileImageHash.Equals(newHash))
                    return false;
                else {
                    lock (UserImageChange) {
                        UserImageChange.Add(newHash, newPath);
                    }
                    return true;
                }
            }
        }

        /// <summary>
        /// Ritorna la lista degli utenti attualmente selezionati
        /// </summary>
        public List<string> GetCurrentSelectedHost () {
            lock (selectedHosts) {
                return selectedHosts.ToList();
            }
        }

        /// <summary>
        /// Ritorna lo status dell'host remoto
        /// </summary>
        /// <param name="ip">Ip host remoto</param>
        /// <returns>Status dell'host se questo esiste</returns>
        public string GetUserStatus ( string ip ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    return Users[ip].Status;
                else
                    return "";
            }
        }

        /// <summary>
        /// Ritorna la lista degli utenti attualmente in linea
        /// </summary>
        public List<Host> GetOnlineUsers () {
            lock (Users) {
                return Users.Values.Where(( user ) => user.Status == "online").ToList();
            }
        }

        /// <summary>
        /// Aggiorna lo status dell'utente remoto
        /// </summary>
        /// <param name="ip">Ip host remoto</param>
        /// <param name="status">Status dav aggiornare</param>
        public void UpdateStatusUser ( string ip, string status ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    Users[ip].Status = status;
            }
        }

        /// <summary>
        /// Ritorna il path dell'immagine di profilo dell'host da aggiornare
        /// </summary>
        /// <param name="ip">Ip host remoto</param>
        /// <returns>Path del file se esiste</returns>
        public string GetPathProfileImageHost ( string ip ) {
            string filename = "";
            string tmp_name = "";
            lock (Users) {
                if (!(tmp_name = Users[ip].ProfileImagePath).Equals("") && File.Exists(tmp_name))
                    filename = Users[ip].ProfileImagePath;
                else {
                    bool checkUserImageChange = false;
                    lock (UserImageChange) {
                        checkUserImageChange = UserImageChange.ContainsKey(Users[ip].ProfileImageHash);
                    }
                    if (Users[ip].ProfileImagePath.Equals(defaultImage) || !checkUserImageChange)
                        filename = Utility.FileNameToHost(defaultImage);
                    else {
                        filename = Users[ip].ProfileImagePath;
                    }
                }
                return filename;
            }
        }

        /// <summary>
        /// Ritorna la lista degli host attualmente selezionati
        /// </summary>
        public List<string> GetSelectedHosts () {
            lock (selectedHosts) {
                return selectedHosts.ToList();
            }
        }

        /// <summary>
        /// Aggiunge alla lista degli host selezionati un host
        /// </summary>
        /// <param name="ip">Ip host da inserire</param>
        public void AddSelectedHost ( string ip ) {
            lock (selectedHosts) {
                if (!selectedHosts.Contains(ip))
                    selectedHosts.Add(ip);
            }
        }

        /// <summary>
        /// Rimuove dalla lista degli host selezionati un host
        /// </summary>
        /// <param name="ip">Ip host da eliminare</param>
        public void RemoveSelectedHost ( string ip ) {
            lock (selectedHosts) {
                if (!selectedHosts.Contains(ip))
                    selectedHosts.Remove(ip);
            }
        }

        /// <summary>
        /// Rimuove dalla lista degli host selezionati un set di Host
        /// </summary>
        /// <param name="ip">Ip host da eliminare</param>
        public void RemoveSelectedHosts ( List<string> listHosts ) {
            lock (selectedHosts) {
                foreach (string ip in listHosts) {
                    if (!selectedHosts.Contains(ip))
                        selectedHosts.Remove(ip);
                }
            }
        }

        /// RECIVE FILE CONFIGURATION/CHECK METHODS ///
        ///-----------------------------------------///

        /// <summary>
        /// Ritorna lo stato dell file da ricevere dati ip del mittente e nome del file
        /// </summary>
        /// <param name="ipUser">Ip mittente file</param>
        /// <param name="pathFile">Path del file di cui ha inviato la richiesta </param>
        /// <returns>Ritorna lo stato del file se questo è stato già annunciato, null altrimenti</returns>
        public FileRecvStatus? GetStatusRecvFileForUser(string ipUser, string pathFile) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                if (currentDictionary.ContainsKey(pathFile))
                    return currentDictionary[pathFile];
            }
            return null;
        }

        public List<String> GetRecvFileIP(string ipUser) {
            lock(FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                List<String> listFile = currentDictionary.Where(e => e.Value == FileRecvStatus.TOCONF).ToDictionary(v => v.Key, v => v.Value).Keys.ToList();
                return listFile;
            }
            
        }


            /// <summary>
            /// Aggiorna status di un file di cui è stata annunciata la ricezione
            /// </summary>
            /// <param name="ipUser">Ip host mittente</param>
            /// <param name="pathFile">Path del file da ricevere</param>
            /// <param name="status">Status da aggiornare</param>
            /// <returns>Ritorna true se lo stato viene aggiornato, falso altrimenti</returns>
            public bool UpdateStatusRecvFileForUser ( string ipUser, string pathFile, FileRecvStatus status ) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                if (currentDictionary.ContainsKey(pathFile)) {
                    FileToRecive.AddOrUpdate(ipUser, ( key ) => currentDictionary, ( key, oldValue ) => {
                        oldValue[pathFile] = status;
                        return oldValue;
                    });
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Controlla se status file da ricevere è stato confermato dall'utente o si vuole rinviare
        /// </summary>
        /// <param name="ipUser">Ip host mittente</param>
        /// <param name="pathFile">Path del file</param>
        /// <returns>Ritorna true se il file da ricevere ha le corrente impostazioni, falso altrimenti </returns>
        public bool CheckPacketRecvFileStatus ( string ipUser, string pathFile ) {
            lock (FileToRecive) {
                if (FileToRecive.ContainsKey(ipUser) && FileToRecive[ipUser].ContainsKey(pathFile) &&
                    (FileToRecive[ipUser][pathFile] == FileRecvStatus.RESENT ||
                     FileToRecive[ipUser][pathFile] == FileRecvStatus.YSEND))
                    return false;
                return true;
            }
        }

        /// <summary>
        /// Aggiorna status file in ricezione in InProgress se e solo se il file non è stato rifiutato
        /// </summary>
        /// <param name="ipUser">Ip host mittente</param>
        /// <param name="pathFile">Path del file</param>
        /// <returns>Ritorna true se il file è stato aggiornato, falso altrimenti</returns>
        public bool CheckAndUpdateRecvFileStatus ( string ipUser, string pathFile ) {
            lock (FileToRecive) {
                if (FileToRecive.ContainsKey(ipUser) && FileToRecive[ipUser].ContainsKey(pathFile) &&
                    (FileToRecive[ipUser][pathFile] != FileRecvStatus.NSEND)) {
                    FileToRecive[ipUser][pathFile] = FileRecvStatus.INPROGRESS;
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Controlla se lo status del file in ricezione è uguale a quello ricevuto come argomento
        /// </summary>
        /// <param name="ipUser">Ip host mittente</param>
        /// <param name="pathFile">Path del file</param>
        /// <param name="status">Status da confrontare</param>
        /// <returns>Ritorna true se il file è stato aggiornato, falso altrimenti</returns>
        public bool CheckRecvFileStatus ( string ipUser, string pathFile, FileRecvStatus status ) {
            lock (FileToRecive) {
                if (FileToRecive.ContainsKey(ipUser) && FileToRecive[ipUser].ContainsKey(pathFile) &&
                    (FileToRecive[ipUser][pathFile] == status))
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Aggiunge o aggiorna un file in ricezione
        /// </summary>
        /// <param name="ipUser">Ip host mittente</param>
        /// <param name="pathFile">Path del file</param>
        /// <param name="status">Status aggiornare</param>
        public void AddOrUpdateRecvStatus ( string ipUser, string pathFile, FileRecvStatus status ) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                if (!FileToRecive.TryGetValue(ipUser, out currentDictionary))
                    currentDictionary = new Dictionary<string, FileRecvStatus>();
                currentDictionary.Add(pathFile, status);
                FileToRecive.AddOrUpdate(ipUser, ( key ) => currentDictionary, ( key, oldValue ) => {
                    if (oldValue.ContainsKey(pathFile)) {
                        oldValue[pathFile] = status;
                        return oldValue;
                    }
                    else
                        return oldValue.Concat(currentDictionary).ToDictionary(x => x.Key, x => x.Value);
                });
            }
        }

        /// <summary>
        /// Controlla se un file è stato ricevuto o no
        /// </summary>
        /// <param name="fileName">Nome del file</param>
        /// <returns>Ritorna true se il file è stato ricevuto, falso altrimenti</returns>
        public bool FileReceiveFromAllUsers ( string fileName ) {
            lock (FileToRecive) {
                int users = FileToRecive.Where(c => c.Value.ContainsKey(fileName)).Count();
                int currentCount = FileToRecive.Where(c => c.Value.ContainsKey(fileName) && ((c.Value)[fileName] == FileRecvStatus.RECIVED || (c.Value)[fileName] == FileRecvStatus.NSEND)).Count();
                if (users != 0 && currentCount >= users)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Cancella un file dalla lista dei file ricevuti
        /// </summary>
        /// <param name="fileName">Nome del file</param>
        public void RemoveRecvFile ( string fileName ) {
            lock (FileToRecive) {
                foreach (KeyValuePair<string, Dictionary<string, FileRecvStatus>> element in FileToRecive) {
                    element.Value.Remove(fileName);
                }
            }
        }



        /// SEND FILE CONFIGURATION/CHECK METHODS ///
        ///---------------------------------------///

        /// <summary>
        /// Aggiunge o aggiorna un file d'invio
        /// </summary>
        /// <param name="ipUser">Ip destinazione</param>
        /// <param name="dictionary">Set di file da aggiungere/aggiornare</param>
        public void AddOrUpdateSendFile ( string ipUser, Dictionary<string, FileSendStatus> dictionary ) {
            lock (FileToSend) {
                FileToSend.AddOrUpdate(ipUser, ( key ) => dictionary, ( key, oldValue ) => {
                    return oldValue.Concat(dictionary).ToDictionary(x => x.Key, x => x.Value);
                });
            }
        }

        /// <summary>
        /// Aggiorna status di un file che si vuole inviare ad uno o più utenti
        /// </summary>
        /// <param name="ipUser">Ip host desitnazione</param>
        /// <param name="pathFile">Path del file da inviare</param>
        /// <param name="status">Status da aggiornare</param>
        /// <returns>Ritorna true se lo stato viene aggiornato, falso altrimenti</returns>
        public bool UpdateSendStatusFileForUser ( string ipUser, string pathFile, FileSendStatus status ) {
            lock (FileToSend) {
                Dictionary<string, FileSendStatus> currentDictionary;
                FileToSend.TryGetValue(ipUser, out currentDictionary);
                if (currentDictionary.ContainsKey(pathFile)) {
                    FileToSend.AddOrUpdate(ipUser, ( key ) => currentDictionary, ( key, oldValue ) => {
                        oldValue[pathFile] = status;
                        return oldValue;
                    });
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Controlla se lo status del file in ricezione è uguale a quello ricevuto come argomento
        /// </summary>
        /// <param name="ipUser">Ip host destinazione</param>
        /// <param name="pathFile">Path del file</param>
        /// <param name="status">Status da confrontare</param>
        /// <returns>Ritorna true se il file è stato aggiornato, falso altrimenti</returns>
        public bool CheckSendStatusFile ( string ipUser, string pathFile, FileSendStatus status ) {
            lock (FileToSend) {
                if (FileToSend.ContainsKey(ipUser) && FileToSend[ipUser].ContainsKey(pathFile) &&
                    (FileToSend[ipUser][pathFile] == FileSendStatus.READY))
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Controllo se il file è stato inviato a tutti gli host
        /// </summary>
        /// <param name="fileName">Nome file</param>
        /// <returns>Ritorna true se il file è stato inviato a tutti, falso altrimenti</returns>
        public bool FileSendForAllUsers ( string fileName ) {
            lock (FileToSend) {
                int users = FileToSend.Where(c => c.Value.ContainsKey(fileName)).Count();
                int currentCount = FileToSend.Where(c => c.Value.ContainsKey(fileName) && ((c.Value)[fileName] == FileSendStatus.END || (c.Value)[fileName] == FileSendStatus.REJECTED)).Count();
                if (users != 0 && currentCount >= users)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Controlla se status file da inviare è stato confermato dall'utente o si vuole rinviare
        /// </summary>
        /// <param name="ipUser">Ip host destinazione</param>
        /// <param name="pathFile">Path del file</param>
        /// <returns>Ritorna true se il file da ricevere ha le corrente impostazioni, falso altrimenti </returns>
        public bool CheckPacketSendFileStatus ( string ipUser, string pathFile ) {
            lock (FileToSend) {
                if (FileToSend.ContainsKey(ipUser) && FileToSend[ipUser].ContainsKey(pathFile) &&
                    (FileToSend[ipUser][pathFile] != FileSendStatus.CONFERMED &&
                     FileToSend[ipUser][pathFile] != FileSendStatus.RESENT))
                    return false;
                return true;
            }
        }

        /// <summary>
        /// Rimuove un file da inviare nella lista
        /// </summary>
        /// <param name="path">Path del file da inviare</param>
        public void RemoveSendFile ( string fileName ) {
            lock (FileToSend) {
                foreach (KeyValuePair<string, Dictionary<string, FileSendStatus>> element in FileToSend) {
                    element.Value.Remove(fileName);
                }
            }
        }

        /// <summary>
        /// Pulisco lista file da inviare
        /// </summary>
        public void ClearPathToSend (List<string> listCurrent ) {
            lock (PathFileToSend) {
                foreach (string path in listCurrent)
                    PathFileToSend.Remove(path);
            }
        }
        
        /// <summary>
        /// Aggiunge un file da inviare nella lista
        /// </summary>
        /// <param name="path">Path del file da inviare</param>
        public void AddPathToSend(string path ) {
            lock (PathFileToSend) {
                PathFileToSend.Add(path);
            }
        }

        /// <summary>
        /// Ritorna lista corrente dei file da inviare
        /// </summary>
        public List<string> GetPathFileToSend () {
            lock (PathFileToSend) {
                return PathFileToSend.ToList();
            }
        }






    }
}
