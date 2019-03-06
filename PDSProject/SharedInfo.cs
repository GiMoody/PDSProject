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
        public string defaultImage = "defaultProfile.png"; 

        public string LocalIPAddress;
        public string BroadcastIPAddress;

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
                    string archiveFolder = Path.Combine(currentDirectory, "Resources");
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
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (/*item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && */item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            // Questo snippet permette di definire l'indirizzo broadcast della sottorete
                            //Console.WriteLine("Local Ip on Wireless LAN:" + ip.Address.ToString());

                            Console.WriteLine("Local Ip " + item.NetworkInterfaceType + " :" + ip.Address.ToString());

                            /*
                            LocalIPAddress = ip.Address.ToString();
                            string[] parts = LocalIPAddress.Split('.');
                            parts[3] = "255";
                            BroadcastIPAddress = string.Join(".", parts);

                            Console.WriteLine("Local Ip on Wireless LAN: " + BroadcastIPAddress);*/
                        }
                    }
                }
            }

            // Test avviso cambio di rete
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback);
        }

        static void AddressChangedCallback(object sender, EventArgs e)
        {

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface n in adapters)
            {
                if (/*item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && */n.OperationalStatus == OperationalStatus.Up)
                {
                    if (!n.Name.Contains("Network Adapter") && !n.Name.Contains("Loopback"))
                    {
                        Console.WriteLine("   {0} is {1}", n.Name, n.OperationalStatus);

                        foreach (UnicastIPAddressInformation ip in n.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                // Questo snippet permette di definire l'indirizzo broadcast della sottorete
                                //Console.WriteLine("Local Ip on Wireless LAN:" + ip.Address.ToString());

                                Console.WriteLine("Local Ip " + n.NetworkInterfaceType + " :" + ip.Address.ToString());

                                /*
                                LocalIPAddress = ip.Address.ToString();
                                string[] parts = LocalIPAddress.Split('.');
                                parts[3] = "255";
                                BroadcastIPAddress = string.Join(".", parts);

                                Console.WriteLine("Local Ip on Wireless LAN: " + BroadcastIPAddress);*/
                            }
                        }
                    }
                }
            }
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
