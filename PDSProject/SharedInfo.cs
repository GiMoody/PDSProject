using System;
using System.Collections.Generic;
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
        private static readonly Lazy<SharedInfo> _singleton = new Lazy<SharedInfo>(() => new SharedInfo());

        public Dictionary<string, Host> Users = new Dictionary<string, Host>();
        public Host LocalUser = new Host();

        // TODO: da rivedere
        public string defaultImage = "fox.jpg"; 

        public string LocalIPAddress = "";
        public string BroadcastIPAddress = "";
        public string CallBackIPAddress = "";

        // TODO: da rivedere, per ora sono fisse
        public Int32 TCPPort = 13000;
        public Int32 UDPReceivedPort = 20000;

        //TODO: cose temporanee
        public string selectedHost = "";
        public bool isFirst = true;
        public bool hasChangedProfileImage = false; // Usato per inviare immagine profilo utente corrente

        //TODO: cose temporanea per invio immagine profilo
        public Dictionary<string, string> UserImageChange = new Dictionary<string, string>(); // Key = hash - Value = namefile

        public List<string> PathFileToSend = new List<string>();
        public Dictionary<string, string> FileToFinish = new Dictionary<string,string>();

        // Data structures di supporto da usare nella ricerca della sottorete in cui sono presenti degli Host
        public List<string> LocalIps = new List<string>();
        public List<string> BroadcastIps = new List<string>();
        public Dictionary<string, string> Ips =new Dictionary<string, string>();

        public object cvListener = new object();

        public bool useTask= true;

        /// <summary>
        /// Costruttore privato, evita che possano esistere più istanze della stessa classe 
        /// </summary>
        private SharedInfo()
        {
           /* TODO: da rivedere.
            *  Per ora crea un file JSON come profilo dell'utente.
            *  Il grosso funziona ma ancora non è implementato l'aggiornamento dell'immagine di profilo.
            */
            if (File.Exists("test.json"))
            {
                // Operaizione di deserializzazione
                DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(Host));
                using (var stream = File.OpenRead("test.json"))
                {
                    stream.Position = 0;
                    LocalUser = (Host)sr.ReadObject(stream);
                }
            }
            else
            {
                // TODO: Nel caso non sia presente un file JSON (prima accensione) ne genera uno di default.
                LocalUser.Name = "Username";
                LocalUser.Status = "online";

                /* Questa cosa è da rivedere, praticamente l'idea per la diversa immagine di profilo sarebbe stato confrontare l'hash SHA256
                 * dell'immagine di profilo.
                 */
                using (SHA256 sha = SHA256.Create())
                {
                    // Costruisco il path assoluto dell'immagine di profilo
                    string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                    string archiveFolder = Path.Combine(currentDirectory);//, "Resources");
                    string[] files = Directory.GetFiles(archiveFolder, defaultImage);
                    FileStream file = File.OpenRead(files[0]);

                    // Calcolo effettivo dell'hash
                    byte[] hash = sha.ComputeHash(file);
                    LocalUser.ProfileImageHash = BitConverter.ToString(hash).Replace("-", String.Empty);
                    LocalUser.ProfileImagePath = defaultImage;
                }

                // Operaizione di deserializzazione
                DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(Host));
                using (var stream = File.Create("test.json"))
                {
                    sr.WriteObject(stream, LocalUser);
                }
            }

            /* TODO: funzionale per ogni rete locale.
             *  Poichè a ho solo una scheda di rete Wifi e non posso connettermi col cavo Ethernet il codice seguente funziona solo per LAN Wifi.
             *  Sarebbe da gestire il caso dove ci sono più reti LAN
             */
          
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback);

            FindAllNetworkInterface();
        }

        private void FindAllNetworkInterface() {
            // Prima di tutto pulisco le strutture dati di supporto
            Ips.Clear();
            if(!LocalIPAddress.Equals(""))
                CallBackIPAddress = LocalIPAddress;
            LocalIPAddress = "";
            BroadcastIPAddress = "";
            Users.Clear();

            // Listo tutte i possibili IP delle Network Interface attive sul dispositivo che non siano Loopback o Virtuali
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces()) {
                if (item.OperationalStatus == OperationalStatus.Up && 
                    !item.Description.Contains("Virtual") && !item.Description.Contains("Loopback")) {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses) {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {

                            Console.WriteLine("Local Ip on " + item.NetworkInterfaceType + " LAN:" + ip.Address.ToString());

                            // Salvo dati all'interno delle struture dati di supporto 
                            /// TODO: vedere se posso sfoltirle un po'
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

        static void AddressChangedCallback(object sender, EventArgs e) {
            Console.WriteLine("AddressShcangedCallback");
            //Invio callback

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

            DataContractJsonSerializer sr = new DataContractJsonSerializer(typeof(Host));
            using (var stream = File.Create("test.json")){
                sr.WriteObject(stream, LocalUser);
            }
        }
    }
}
