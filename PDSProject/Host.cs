using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace PDSProject
{
    public enum Status {
        PRIVATE,
        PUBLIC
    }
    
    /// <summary>
    /// Serializzatore usato per identificare i vari componenti dell'oggetto JSON che descrive i profili utenti
    /// TODO: mancano tutte le informazioni di configurazione
    /// </summary>
    //[DataContract]
    //public class CurrentHostProfile
    //{
    //    [DataMember]
    //    public string Name;
    //    [DataMember]
    //    public Status Status;

    //    [DataMember]
    //    public string ProfileImageHash; // Vedere se tenerlo
    //    [DataMember]
    //    public string ProfileImagePath;

    //    // Opzioni configurazione utente


    //    /// <summary>
    //    /// Controlla se due Host sono uguali o no
    //    /// </summary>
    //    /// <param name="obj">Oggetto di tipo Host da controllare</param>
    //    /// <returns>Bool -> vero se sono uguali, falso se no</returns>
    //    public override bool Equals(Object obj)
    //    {
    //        return (obj is Host) && (((Host)obj).Name.Equals(Name) && ((Host)obj).Status.Equals(Status) && 
    //                                 ((Host)obj).ProfileImageHash.Equals(ProfileImageHash) && ((Host)obj).ProfileImagePath.Equals(ProfileImagePath));
    //    }
    //}

    [DataContract]
    public class Host
    {
        [DataMember]
        public string Name;
        [DataMember]
        public string Status;
        [DataMember]
        public string ProfileImageHash; // Vedere se tenerlo
        [DataMember]
        public string ProfileImagePath;


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

}
