using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDSProject {
    public class FileRecive {
       
        public String hostName { get; set; }
        public String fileName { get; set; }
        public String statusFile { get; set; } 
        public String estimatedTime { get; set; }
        public double dataRecived { get; set; }

        public FileRecive(string hostName, string fileName, String statusFile, string estimatedTime, double dataRecived) {
            this.hostName = hostName;
            this.fileName = fileName;
            this.statusFile = statusFile;
            this.estimatedTime = estimatedTime;
            this.dataRecived = dataRecived;
        }

        public FileRecive() {
        }
    }
}
