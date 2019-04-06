using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Reflection;

namespace PDSProject
{
    /// <summary>
    /// Mantiene le informazioni condivise da ogni classe del programma.
    /// Per garantire che esista una sola istanza durante l'esecuzione del programma è stata implementata come singleton 
    /// utilizzando la classe Lazy<T>. Questa garantisce che venga sempre inizializzata una singola istanza anche in caso di
    /// esecuzione concorrente.
    /// </summary>

    public class SharedInfo
    {
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
        public string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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

        // TCP listener Condition Variable in case of changed network
        public object cvListener = new object(); 

        // Files to send information
        //---------------------------------------------
        public List<string> PathFileToSend = new List<string>();
        public ConcurrentDictionary<string, Dictionary<string,string>> FileToFinish = new ConcurrentDictionary<string, Dictionary<string,string>>();

      
        
        // TO DELETE IN FUTURE
        public bool isFirst = true;
        public bool hasChangedProfileImage = false; // Usato per inviare immagine profilo utente corrente
        public bool useTask= true;
        public string CallBackIPAddress = "";




        /// <summary>
        /// Costruttore privato, evita che possano esistere più istanze della stessa classe 
        /// </summary>
        private SharedInfo () {
            // Controlla se esiste già un profilo dell'utente corrente, se noi lo crea
            if (File.Exists(currentHostPath)) {
                // Operaizione di deserializzazione
                DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
                using (var stream = File.OpenRead(currentHostPath)) {
                    stream.Position = 0;
                    LocalUser = (CurrentHostProfile)sr.ReadObject(stream);
                }
            }
            else {
                // Nel caso non sia presente un file JSON (prima accensione) ne genera uno di default.
                LocalUser.Name = "Username";
                LocalUser.Status = "online";
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
            // Aggiunto come delegato il metodo 'AddressChangedCallback' in caso di cambio di rete
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback); 
            FindAllNetworkInterface();
        }

        /// <summary>
        /// Per ogni cambio controlla per ogni interfaccia di rete non virtuale o non di callback l'indirizzo IP locale e calcola il corrispondende multicast
        /// </summary>
        private void FindAllNetworkInterface() {
            // Prima di tutto pulisco le strutture dati di supporto
            Ips.Clear();
            //if(!LocalIPAddress.Equals(""))
            //    CallBackIPAddress = LocalIPAddress;
            LocalIPAddress = "";
            BroadcastIPAddress = "";
            Users.Clear();

            // Listo tutte i possibili IP delle Network Interface attive sul dispositivo che non siano Loopback o Virtuali
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces()) {
                if ( item.OperationalStatus == OperationalStatus.Up && 
                    !item.Description.Contains("Virtual")           && 
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

        /// <summary>
        /// Metodo invocato ad ogni cambio di rete
        /// </summary>
        static void AddressChangedCallback(object sender, EventArgs e) {
            Console.WriteLine("AddressShcangedCallback");
            Instance.FindAllNetworkInterface();
        }


        /// <summary>
        /// Proprietà che ritorna una copia del rifierimento all'istanza
        /// </summary>
        public static SharedInfo Instance{
            get{
                return _singleton.Value;
            }
        }

        /// <summary>
        /// Aggiorna le informazioni del profilo utente salvate sul file JSON
        /// </summary>
        public void SaveJson(){
            DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(CurrentHostProfile));
            using (var stream = File.Create(currentHostPath)){
                sr.WriteObject(stream, LocalUser);
            }
        }
    }
}
