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

        public string hostName { get { return _hostName; } set { _hostName = value; OnPropertyChanged("hostName"); } }
        public string fileName { get { return _fileName; } set { _fileName = value; OnPropertyChanged("fileName"); } }
        public string statusFile { get { return _statusFile; } set { _statusFile = value; OnPropertyChanged("statusFile"); } }
        public string estimatedTime { get { return _estimatedTime; } set { _estimatedTime = value; OnPropertyChanged("estimatedTime"); } }
        public double dataRecived { get { return _dataRecived; } set { _dataRecived = value; OnPropertyChanged("dataRecived"); } }
        public bool isRecived { get; set; }
        public long TimestampResend { get; set; }
        public string ip { get; set; }

        public FileRecive (string hostName, string fileName, FileRecvStatus? statusFile, string estimatedTime, double dataRecived) {
            this.hostName = hostName;
            this.fileName = fileName;
            if(statusFile != null)
                UpdateStatusString(statusFile.Value);
            this.estimatedTime = estimatedTime;
            this.dataRecived = dataRecived;
            isRecived = true;
            TimestampResend = 0;
        }

        public FileRecive ( string hostName, string fileName, FileSendStatus? statusFile, string estimatedTime, double dataRecived ) {
            this.hostName = hostName;
            this.fileName = fileName;
            if (statusFile != null)
                UpdateStatusString(statusFile.Value);
            this.estimatedTime = estimatedTime;
            this.dataRecived = dataRecived;
            isRecived = false;
        }

        public void UpdateStatusString(FileRecvStatus status ) {
            switch (status) {
                case FileRecvStatus.TOCONF :
                    statusFile = "Da Confermare";
                    break;
                case FileRecvStatus.YSEND :
                    statusFile = "Confermato";
                    break;
                case FileRecvStatus.NSEND :
                    statusFile = "Annullato";
                    break;
                case FileRecvStatus.RECIVED :
                    statusFile = "Ricevuto";
                    break;
                case FileRecvStatus.INPROGRESS :
                    statusFile = "Download in corso...";
                    break;
                case FileRecvStatus.UNZIP:
                    statusFile = "Fase unzip...";
                    break;
                case FileRecvStatus.RESENT:
                    statusFile = "In attesa di rinvio";
                    break;
            }
        }

        public void UpdateStatusString ( FileSendStatus status ) {
            switch (status) {
                case FileSendStatus.PREPARED:
                    statusFile = "In preparazione per l'invio...";
                    break;
                case FileSendStatus.READY:
                    statusFile = "Pronto per l'invio";
                    break;
                case FileSendStatus.CONFERMED:
                    statusFile = "Confermato";
                    break;
                case FileSendStatus.REJECTED:
                    statusFile = "Annullato";
                    break;
                case FileSendStatus.RESENT:
                    statusFile = "Da rinviare";
                    break;
                case FileSendStatus.INPROGRESS:
                    statusFile = "Invio in corso...";
                    break;
                case FileSendStatus.END:
                    statusFile = "Fine invio";
                    break;
            }
        }

        private void OnPropertyChanged (string propertyName) {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
