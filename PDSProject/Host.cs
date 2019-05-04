using System;

using System.Runtime.Serialization;

namespace PDSProject
{
    public enum Status {
        PRIVATE,
        PUBLIC
    }

    public enum PacketType {
        FSEND,
        CIMAGE,
        RFILE,
        YFILE,
        NFILE
    }

    [DataContract]
    public class Host
    {
        [DataMember]
        public string Name { get; set; }
        [DataMember]
        public string Status { get; set; }
        [DataMember]
        public string ProfileImageHash { get; set; }
        [DataMember]
        public string ProfileImagePath { get; set; }

        public string Ip { get; set; }
        
        /// LastPacketTime is expressend in milliseconds
        public long LastPacketTime { get; set; }

        public void UpdateStatus(string status ) {
            Status = status;
        }
        

        /// <summary>
        /// Controlla se due Host sono uguali o no
        /// </summary>
        /// <param name="obj">Oggetto di tipo Host da controllare</param>
        /// <returns>Bool -> vero se sono uguali, falso se no</returns>
        public override bool Equals(Object obj)
        {
            return (obj is Host) && (((Host)obj).Name.Equals(Name) && ((Host)obj).Status.Equals(Status) &&
                                     ((Host)obj).ProfileImageHash.Equals(ProfileImageHash) && ((Host)obj).ProfileImagePath.Equals(ProfileImagePath));
        }
    }

    /// <summary>
    /// Classe che identifica le informazioni dell'utente corrente.
    /// Contiene le informazioni comuni con Host più le informaizioni di configurazione utente
    /// </summary>
    /// 
    [DataContract]
    public class CurrentHostProfile {
        // Common Data
        [DataMember]
        public string Name { get; set; }
        [DataMember]
        public string Status { get; set; }

        [DataMember]
        public string ProfileImageHash { get; set; }
        [DataMember]
        public string ProfileImagePath { get; set; }

        // Configuration data
        [DataMember]
        public string SavePath { get; set; }
        [DataMember]
        public bool AcceptAllFile { get; set; }

        /// <summary>
        /// Converte l'host corrente in un oggetto serializzato di tipo Host
        /// </summary>
        /// <returns>Host</returns>
        public Host ConvertToHost () {
            Host convertHost = new Host();
            convertHost.Name = Name;
            convertHost.Status = Status;
            convertHost.ProfileImageHash = ProfileImageHash;
            convertHost.ProfileImagePath = ProfileImagePath;

            return convertHost;
        }
    }

}
