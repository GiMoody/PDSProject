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
        /// Check if two users are the same or not
        /// </summary>
        /// <param name="obj">Object to comapre</param>
        /// <returns>Bool -> true if they are the same, false otherwise</returns>
        public override bool Equals(Object obj)
        {
            return (obj is Host) && (((Host)obj).Name.Equals(Name) && ((Host)obj).Status.Equals(Status) &&
                                     ((Host)obj).ProfileImageHash.Equals(ProfileImageHash) && ((Host)obj).ProfileImagePath.Equals(ProfileImagePath));
        }
    }

    /// <summary>
    /// Contains all the local user data
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
        /// Convert a CurrentHostProfile object into a Host one
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
