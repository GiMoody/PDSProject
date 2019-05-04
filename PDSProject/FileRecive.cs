using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDSProject {
    public class FileRecive {
       
        String hostName { get; set; }
        String fileName { get; set; }
        FileRecvStatus statusFile { get; set; } 
        String estimatedTime { get; set; }
        String dataRecived { get; set; }

        public FileRecive(string hostName, string fileName, FileRecvStatus statusFile, string estimatedTime, string dataRecived) {
            this.hostName = hostName;
            this.fileName = fileName;
            this.statusFile = statusFile;
            this.estimatedTime = estimatedTime;
            this.dataRecived = dataRecived;
        }
    }
}
