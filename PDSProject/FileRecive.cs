using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDSProject {
    public class FileRecive : INotifyPropertyChanged {
        private string _hostName;
        private string _fileName;
        private string _statusFile;
        private string _estimatedTime;
        private double _dataRecived;

        public event PropertyChangedEventHandler PropertyChanged;

        public String hostName { get { return _hostName; } set { _hostName = value; OnPropertyChanged("hostName"); } }
        public String fileName { get { return _fileName; } set { _fileName = value; OnPropertyChanged("fileName"); } }
        public String statusFile { get { return _statusFile; } set { _statusFile = value; OnPropertyChanged("statusFile"); } }
        public String estimatedTime { get { return _estimatedTime; } set { _estimatedTime = value; OnPropertyChanged("estimatedTime"); } }
        public double dataRecived { get { return _dataRecived; } set { _dataRecived = value; OnPropertyChanged("dataRecived"); } }

        public FileRecive(string hostName, string fileName, String statusFile, string estimatedTime, double dataRecived) {
            this.hostName = hostName;
            this.fileName = fileName;
            this.statusFile = statusFile;
            this.estimatedTime = estimatedTime;
            this.dataRecived = dataRecived;
        }

        private void OnPropertyChanged (string propertyName) {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public FileRecive() {
        }
    }
}
