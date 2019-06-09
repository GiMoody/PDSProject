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

namespace PDSProject {
    // Enum status for send file
    public enum FileSendStatus {
        PREPARED,
        READY,
        INPROGRESS,
        REJECTED,
        CONFERMED,
        END,
        RESENT
    }

    // Enum status for recv file
    public enum FileRecvStatus {
        TOCONF,
        YSEND,
        NSEND,
        RECIVED,
        INPROGRESS,
        UNZIP,
        RESENT
    }

    /// <summary>
    /// Shared info between classes.
    /// It exits as a singleton using the Lazy<T> class.
    /// </summary>
    public class SharedInfo {
        // Singleton 
        //---------------------------------------------
        private static readonly Lazy<SharedInfo> _singleton = new Lazy<SharedInfo>(() => new SharedInfo());

        // Hosts in network 
        //---------------------------------------------
        public Dictionary<string, Host> Users = new Dictionary<string, Host>();
        private List<string> selectedHosts = new List<string>();
        private Dictionary<string, string> UserImageChange = new Dictionary<string, string>(); // Key = hash - Value = namefile

        // Info for the current user
        //---------------------------------------------
        private CurrentHostProfile LocalUser = new CurrentHostProfile();
        
        // Default Value
        public string defaultImage = "user.png"; 
        public string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).ToString();
        string currentHostPath = Utility.PathResources() + "\\userProfile.json";

        // Network Confugration
        //---------------------------------------------
        public const Int32 TCPPort = 13000;
        public const Int32 UDPReceivedPort = 20000;

        // Current network information 
        private string LocalIPAddress = "";
        private string BroadcastIPAddress = "";

        // Support data structures used to search a subnetwork with at least one host
        private List<string> LocalIps = new List<string>();
        private List<string> BroadcastIps = new List<string>();
        private Dictionary<string, string> Ips =new Dictionary<string, string>();
        private object lockIps = new object();

        // TCP listener Condition Variable in case of changed network
        public object cvListener = new object();
        public volatile bool isFirst = true;

        // Files to send information
        //---------------------------------------------
        private List<string> PathFileToSend = new List<string>();
        private ConcurrentDictionary<string, Dictionary<string, FileSendStatus>> FileToSend = new ConcurrentDictionary<string, Dictionary<string, FileSendStatus>>();

        // File to recive information
        //---------------------------------------------
        private ConcurrentDictionary<string, Dictionary<string, FileRecvStatus>> FileToRecive = new ConcurrentDictionary<string, Dictionary<string, FileRecvStatus>>();
        
        /// <summary>
        /// Private Constructor, used to avoid call from other classes
        /// </summary>
        private SharedInfo () {
            // Check if an instance of the singleton already exists
            if (File.Exists(currentHostPath)) {
                lock (LocalUser) {
                    // Deserialization of the current local user data saved as a JSON
                    DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                    using (var stream = File.OpenRead(currentHostPath)) {
                        stream.Position = 0;
                        LocalUser = (CurrentHostProfile)sr.ReadObject(stream);
                    }
                }
            }
            else {
                lock (LocalUser) {
                    // If the file doesn't exist, a default one is created.
                    LocalUser.Name = "Username";
                    LocalUser.Status = "offline";
                    LocalUser.AcceptAllFile = false;
                    LocalUser.SavePath = defaultPath;

                    // Create the hash of the profile image
                    using (SHA256 sha = SHA256.Create()) {
                        string file_name = Utility.FileNameToHost(defaultImage);
                        FileStream file = File.OpenRead(file_name);
                        
                        byte[] hash = sha.ComputeHash(file);
                        LocalUser.ProfileImageHash = BitConverter.ToString(hash).Replace("-", String.Empty);
                        LocalUser.ProfileImagePath = defaultImage;
                    }

                    // Deserialization operation
                    DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                    using (var stream = File.Create(currentHostPath)) {
                        sr.WriteObject(stream, LocalUser);
                    }
                }
            }
            // Add delegate to the 'AddressChangedCallback' event in case of a network change configuration
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback); 
            FindAllNetworkInterface();
        }

        /// <summary>
        /// Return a copy of the singleton's instance
        /// </summary>
        public static SharedInfo Instance {
            get {
                return _singleton.Value;
            }
        }

        #region --------------- NETWORK CONFIGURATION ---------------------
        /// <summary>
        /// In case of network configuration change, change the ip and calculate the correspondend brodacast address
        /// </summary>
        private void FindAllNetworkInterface() {
            // Clean all the support structures
            lock (Users) {
                Users.Clear();
            }
            
            lock (FileToRecive){
                foreach(KeyValuePair<string, Dictionary<string, FileRecvStatus>> value in FileToRecive){
                    foreach (KeyValuePair<string, FileRecvStatus> file in value.Value) {
                        string path = Utility.PathTmp() + "\\" + file.Key;
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                }
                FileToRecive.Clear();
            }

            lock (FileToSend) {
                foreach (KeyValuePair<string, Dictionary<string, FileSendStatus>> value in FileToSend) {
                    foreach (KeyValuePair<string, FileSendStatus> file in value.Value) {
                        string path = Utility.PathTmp() + "\\" + file.Key;
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                }
                FileToSend.Clear();
            }

            lock (selectedHosts) {
                selectedHosts.Clear();
            }

            lock (PathFileToSend) {
                PathFileToSend.Clear();
            }

            lock (UserImageChange) {
                UserImageChange.Clear();
            }

            // Lock to ensure the mutual exclusion of the data structured used in case of a network change
            lock (lockIps) {
                Ips.Clear();
                LocalIPAddress = "";
                BroadcastIPAddress = "";

                // List all the network address's IP of the active network interfaces (excluded Virtual and Loopback)
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (item.OperationalStatus == OperationalStatus.Up &&
                        !item.Description.Contains("Virtual") &&
                        !item.Description.Contains("Loopback")) {

                        foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses) {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                                if (!LocalIps.Contains(ip.Address.ToString()))
                                    LocalIps.Add(ip.Address.ToString());

                                string BroadcastIPAddress = Utility.GetMulticastAddress(ip.Address.ToString());

                                if (!BroadcastIps.Contains(BroadcastIPAddress))
                                    BroadcastIps.Add(BroadcastIPAddress);

                                if (!Ips.ContainsKey(ip.Address.ToString()))
                                    Ips.Add(BroadcastIPAddress, ip.Address.ToString());
                                else
                                    Ips[BroadcastIPAddress] = ip.Address.ToString();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// It been called at each network configuration change
        /// </summary>
        static void AddressChangedCallback(object sender, EventArgs e) {
            Instance.FindAllNetworkInterface();
        }

        /// <summary>
        /// Check if a network configuration is setted
        /// </summary>
        /// <returns>Return true if the a configuration is setted, false otherwise</returns>
        public bool CheckIfConfigurationIsSet () {
            lock (lockIps) {
                if (LocalIPAddress.Equals("") && BroadcastIPAddress.Equals(""))
                    return false;
                return true;
            }
        }

        /// <summary>
        /// Return the list of all broadcast ips
        /// </summary>
        /// <returns>List of all the ips</returns>
        public List<string> GetListBroadcastIps () {
            lock (lockIps) {
                return Ips.Keys.ToList();
            }
        }

        /// <summary>
        /// Return the list of all ips
        /// </summary>
        /// <returns>List of all the ips</returns>
        public List<string> GetListIps () {
            lock (lockIps) {
                return Ips.Values.ToList();
            }
        }

        /// <summary>
        /// Return a copy of the current broadcast address
        /// </summary>
        /// <returns>The current broadcast address</returns>
        public string GetBroadcastIPAddress() {
            lock (lockIps) {
                return BroadcastIPAddress;
            }
        }

        /// <summary>
        /// Return a copy of the current ip address
        /// </summary>
        /// <returns>The current local ip address</returns>
        public string GetLocalIPAddress () {
            lock (lockIps) {
                return LocalIPAddress;
            }
        }

        /// <summary>
        /// Update network configuration
        /// </summary>
        /// <param name="ip">Local Ip that reply</param>
        /// <returns>True if the ip subnetwork belongs to one of the network interface of the system</returns>
        public bool UpdateNetworkConfiguration ( string ip ) {
            lock (lockIps) {
                if (LocalIPAddress.Equals("") && BroadcastIPAddress.Equals("")) {
                    // In case at least one address exists...
                    if (Ips.Count > 0) {
                        string MulticastAddrs = Utility.GetMulticastAddress(ip);
                        if (BroadcastIps.Contains(MulticastAddrs)) {
                            BroadcastIPAddress = MulticastAddrs;
                            LocalIPAddress = Ips[MulticastAddrs];
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        #endregion
        
        #region --------------- LOCAL USER CONFIGURATION ---------------------

        /// <summary>
        /// Update the user interface data on the JSON file
        /// </summary>
        public void SaveJson(){
            try {
                DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                using (var stream = File.Create(currentHostPath)) {
                    sr.WriteObject(stream, LocalUser);
                }
            }catch(Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - SaveJson Exception - {e.GetType()} {e.Message}");
                throw e;
            }
        }

        /// <summary>
        /// Update all user data except the status
        /// </summary>
        /// <param name="name">New Username</param>
        /// <param name="pathImage">New Profile Image Path</param>
        /// <param name="pathSave">New Save Path</param>
        /// <param name="isPathDefault">Bool value, true if the path is default (false otherwise)</param>
        /// <param name="acceptAll">Bool value, true if the user accept all files (false otherwise)</param>
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

                // Check if the image is the same (hash check)
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
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UpdateInfoLocalUser Exception - {e.GetType()} {e.Message}");
                    throw e;
                }
            }
        }

        /// <summary>
        /// Update local user current status
        /// </summary>
        /// <param name="status">New status (online/offline)</param>
        public void UpdateStatusLocalUser ( string status ) {
            lock (LocalUser) {
                LocalUser.Status = status.Equals("") ? LocalUser.Status : status;
                try {
                    SaveJson();
                }
                catch (Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UpdateStatusLocalUser Exception - {e.GetType()} {e.Message}");
                    throw e;
                }
            }
        }

        /// <summary>
        /// Return the CurrentLocalHost object
        /// </summary>
        public CurrentHostProfile GetInfoLocalUser () {
            CurrentHostProfile returnData;
            lock (LocalUser) {
                returnData = LocalUser;
            }
            return returnData;
        }

        #endregion
        
        #region --------------- REMOTE USER CONFIGURATION ---------------------

        
        /// <summary>
        /// Return the username of a remote user giving its IP
        /// </summary>
        public string GetRemoteUserName(string ip) {
            lock(Users) {
                if(Users.ContainsKey(ip))
                    return Users[ip].Name;
                return null;
            }
        }
        
        /// <summary>
        /// Return the hash value of a remote user's profile image giving its ip
        /// </summary>
        public string GetRemoteUserHashImage ( string ip ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    return Users[ip].ProfileImageHash;
                return null;
            }
        }

        /// <summary>
        /// Return the path of the profile image of a remote user's profile image giving its ip
        /// </summary>
        public string GetRemoteUserProfileImage ( string ip ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    return Users[ip].ProfileImagePath;
                return null;
            }
        }

        /// <summary>
        /// Update or Add data of a remote user
        /// </summary>
        /// <param name="host">Remote user object</param>
        /// <param name="ip">Ip remote user</param>
        /// <returns>Return true if the host was added or updated, false otherwise</returns>
        public bool UpdateUsersInfo ( Host host, string ip ) {
            lock (Users) {
                bool IsUserUpdate = false;
                
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

                // User already exists
                if (Users.ContainsKey(ip)) {
                    if (!Users[ip].Equals(host)) {
                        Users[ip] = host;
                        IsUserUpdate = true;
                    }
                    else
                        Users[ip].LastPacketTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }
                else {
                    // User doesn't exists
                    host.ProfileImageHash = "";
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
        /// Update profile image data of a remote user. 
        /// In case the file doesn't exists on the filesystem the hash and the name are saved into a data structure
        /// </summary>
        /// <param name="newHash">New hash's value</param>
        /// <param name="newPath">New path file</param>
        /// <param name="ip">Remote Ip host</param>
        /// <returns>Return true if the profile image was update, false otherwise</returns>
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
        /// Return the list of the current selected user
        /// </summary>
        public List<string> GetCurrentSelectedHost () {
            lock (selectedHosts) {
                return selectedHosts.ToList();
            }
        }

        /// <summary>
        /// Return remote user status giving its ip
        /// </summary>
        /// <param name="ip">Remote user IP</param>
        /// <returns>Status of the remote user</returns>
        public string GetUserStatus ( string ip ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    return Users[ip].Status;
                else
                    return "";
            }
        }

        /// <summary>
        /// Return the list of the online remote users
        /// </summary>
        public List<Host> GetOnlineUsers () {
            lock (Users) {
                return Users.Values.Where(( user ) => user.Status == "online").ToList();
            }
        }

        /// <summary>
        /// Update remote user's status
        /// </summary>
        /// <param name="ip">IP remote user</param>
        /// <param name="status">Status to update</param>
        public void UpdateStatusUser ( string ip, string status ) {
            lock (Users) {
                if (Users.ContainsKey(ip))
                    Users[ip].Status = status;
            }
        }

        /// <summary>
        /// Return the path of image profile of the remote user that need to be update
        /// </summary>
        /// <param name="ip">Remote user Ip</param>
        /// <returns>Path of the remote user, if exist</returns>
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
                    if (checkUserImageChange) {
                        lock (UserImageChange) {
                            UserImageChange.Remove(Users[ip].ProfileImageHash);
                        }
                    }
                }
                return filename;
            }
        }

        /// <summary>
        /// Return the list of all the selected remote users 
        /// </summary>
        public List<string> GetSelectedHosts () {
            lock (selectedHosts) {
                return selectedHosts.ToList();
            }
        }

        /// <summary>
        /// Add a remote user to the selected user's list
        /// </summary>
        /// <param name="ip">IP of the remote user to add</param>
        public void AddSelectedHost ( string ip ) {
            lock (selectedHosts) {
                if (!selectedHosts.Contains(ip))
                    selectedHosts.Add(ip);
            }
        }

        /// <summary>
        /// Remove a host to the list of selected host
        /// </summary>
        /// <param name="ip">Remote user's Ip</param>
        public void RemoveSelectedHost ( string ip ) {
            lock (selectedHosts) {
                if (selectedHosts.Contains(ip))
                    selectedHosts.Remove(ip);
            }
        }

        /// <summary>
        /// Remove a set of host on the user's selected listHost
        /// </summary>
        /// <param name="ip">List of remote user's Ip</param>
        public void RemoveSelectedHosts ( List<string> listHosts ) {
            lock (selectedHosts) {
                foreach (string ip in listHosts) {
                    if (!selectedHosts.Contains(ip))
                        selectedHosts.Remove(ip);
                }
            }
        }

        #endregion

        #region --------------- RECIVE FILE CONFIGURATION/CHECK ---------------------

        /// <summary>
        /// Return the status of the recived file status
        /// </summary>
        /// <param name="ipUser">Sender Ip</param>
        /// <param name="pathFile">Path of the file</param>
        /// <returns>Return the status of the recived file, if it was already announced</returns>
        public FileRecvStatus? GetStatusRecvFileForUser(string ipUser, string pathFile) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                if (currentDictionary.ContainsKey(pathFile))
                    return currentDictionary[pathFile];
            }
            return null;
        }

        /// <summary>
        /// Get the list of received file that need to be configured giving the remote user's ip
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <returns>List of files</returns>
        public List<String> GetRecvFileIP(string ipUser) {
            lock(FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                List<String> listFile = currentDictionary.Where(e => e.Value == FileRecvStatus.TOCONF).ToDictionary(v => v.Key, v => v.Value).Keys.ToList();
                return listFile;
            }
        }

        /// <summary>
        /// Get the list of all received file giving the remote user's ip
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <returns>List of files</returns>
        public List<String> GetListRecvFileIP (string ipUser) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                FileToRecive.TryGetValue(ipUser, out currentDictionary);
                List<String> listFile = currentDictionary.Keys.ToList();
                return listFile;
            }
        }

        /// <summary>
        /// Update the status of the announced file
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <param name="pathFile">Path file to receive</param>
        /// <param name="status">File Status</param>
        /// <returns>True if the file status was update, false otherwise</returns>
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
        /// Check if the status of the file was confirmed or need to be resended
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <param name="pathFile">Path file to update</param>
        /// <returns>Return true if the file has the correct settings, false otherwise</returns>
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
        /// Update status of received file only if it was accepted
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <param name="pathFile">Path file to update</param>
        /// <returns>Return true if the file was updated, false otherwise</returns>
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
        /// Check the status of a received file is the same of the one passed as argument
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <param name="pathFile">Path file to update</param>
        /// <param name="status">File Status</param>
        /// <returns>Return true if the file was update, false otherwise</returns>
        public bool CheckRecvFileStatus ( string ipUser, string pathFile, FileRecvStatus status ) {
            lock (FileToRecive) {
                if (FileToRecive.ContainsKey(ipUser) && FileToRecive[ipUser].ContainsKey(pathFile) &&
                    (FileToRecive[ipUser][pathFile] == status))
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Add or update a received file
        /// </summary>
        /// <param name="ipUser">Remote user's ip</param>
        /// <param name="pathFile">Path file to update</param>
        /// <param name="status">File Status</param>
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
        /// Check if the file was received or not
        /// </summary>
        /// <param name="fileName">FileName</param>
        /// <returns>True if the file was received, false otherwisw</returns>
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
        /// Delete a file if it was received
        /// </summary>
        /// <param name="fileName">FileName</param>
        public void RemoveRecvFile ( string fileName ) {
            lock (FileToRecive) {
                foreach (KeyValuePair<string, Dictionary<string, FileRecvStatus>> element in FileToRecive) {
                    element.Value.Remove(fileName);
                }
            }
        }

        /// <summary>
        /// Update the name of a received file
        /// </summary>
        /// <param name="OriginalFileName">original filename</param>
        /// <param name="fileName">update filename</param>
        /// <param name="ip">Remote user's ip</param>
        /// <param name="status">Status of the file</param>
        public void UpdateFileName (string originalFileName, string fileName, string ip, FileRecvStatus status ) {
            lock (FileToRecive) {
                Dictionary<string, FileRecvStatus> currentDictionary;
                if (FileToRecive.TryGetValue(ip, out currentDictionary)) {
                    if (currentDictionary.ContainsKey(originalFileName) && currentDictionary[originalFileName] == status) {
                        currentDictionary.Remove(originalFileName);
                        currentDictionary.Add(fileName, status);
                        FileToRecive.AddOrUpdate(ip, ( key ) => currentDictionary, ( key, oldValue ) => {
                            oldValue = currentDictionary;
                            return oldValue;
                        });
                    }
                }
            }
        }

        #endregion

        #region --------------- SEND FILE CONFIGURATION/CHECK ---------------------

        /// <summary>
        /// Add or update a sended file
        /// </summary>
        /// <param name="ipUser">Destination ip</param>
        /// <param name="dictionary">Set of file to add/update</param>
        public void AddOrUpdateSendFile ( string ipUser, Dictionary<string, FileSendStatus> dictionary ) {
            lock (FileToSend) {
                FileToSend.AddOrUpdate(ipUser, ( key ) => dictionary, ( key, oldValue ) => {
                    return oldValue.Concat(dictionary).ToDictionary(x => x.Key, x => x.Value);
                });
            }
        }

        /// <summary>
        /// Update send file status
        /// </summary>
        /// <param name="ipUser">Destination ip</param>
        /// <param name="pathFile">Path file</param>
        /// <param name="status">Status to update</param>
        /// <returns>True if the status is update, false otherwise</returns>
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
        /// CHeck if the send file status is the same as the one received as argument
        /// </summary>
        /// <param name="ipUser">Destination Ip</param>
        /// <param name="pathFile">Path file</param>
        /// <param name="status">Status to update</param>
        /// <returns>Return true if the status is the same, false otherwise</returns>
        public bool CheckSendStatusFile ( string ipUser, string pathFile, FileSendStatus status ) {
            lock (FileToSend) {
                if (FileToSend.ContainsKey(ipUser) && FileToSend[ipUser].ContainsKey(pathFile) &&
                    (FileToSend[ipUser][pathFile] == status))
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Check if the file was sended to all the remote hosts
        /// </summary>
        /// <param name="fileName">File Name</param>
        /// <returns>Return true if the file was sended to everyone, false otherwise</returns>
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
        /// Check if the sended file status was confirmed or need to be resended
        /// </summary>
        /// <param name="ipUser">Destination ip</param>
        /// <param name="pathFile">Path file</param>
        /// <returns>Return true if the file has the correct configuration, false otherwise</returns>
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
        /// Remove an sended file to the list
        /// </summary>
        /// <param name="path">Path file to remove</param>
        public void RemoveSendFile ( string fileName ) {
            lock (FileToSend) {
                foreach (KeyValuePair<string, Dictionary<string, FileSendStatus>> element in FileToSend) {
                    element.Value.Remove(fileName);
                }
            }
        }

        /// <summary>
        /// Clean the list of file to send
        /// </summary>
        public void ClearPathToSend (List<string> listCurrent ) {
            lock (PathFileToSend) {
                foreach (string path in listCurrent)
                    PathFileToSend.Remove(path);
            }
        }
        
        /// <summary>
        /// Add a file to the list of file to send
        /// </summary>
        /// <param name="path">File path</param>
        public void AddPathToSend(string path ) {
            lock (PathFileToSend) {
                PathFileToSend.Add(path);
            }
        }

        /// <summary>
        /// Return list of the current files to send
        /// </summary>
        /// <returns>List of the files to send</returns>
        public List<string> GetPathFileToSend () {
            lock (PathFileToSend) {
                return PathFileToSend.ToList();
            }
        }

        /// <summary>
        /// Clear the Path file to send list
        /// </summary>
        public void ClearPathFileToSend() {
            lock(PathFileToSend) {
                PathFileToSend.Clear();
            }
        }

        /// <summary>
        /// Get dictionary of all the file to send giving the destination ip
        /// </summary>
        /// <param name="ip">Destination ip</param>
        /// <returns>Dictionary with all the files and status</returns>
        public Dictionary<string, FileSendStatus> GetSendFilesByIp(string ip) {
            lock (FileToSend) {
                if (FileToSend.ContainsKey(ip))
                    return FileToSend[ip];
                else
                    return null;
            }
        }

        #endregion
    }
}
