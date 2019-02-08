using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.IO;
using Path = System.IO.Path;
using System.Reflection;

namespace PDSProject
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// La grafica della finestra è da cambiare pesantemente, tutto è stato fatto per testare il funzionamento
    /// </summary>
    public partial class MainWindow : Window
    {

        public static MainWindow main;  // Usato come riferimento da parte delle altri classi
        SharedInfo _referenceData = SharedInfo.Instance;
        MyTCPListener _TCPListener;
        MyTCPSender _TCPSender;
        MyUDPListener _UDPListener;
        MyUDPSender _UDPSender;

        public MainWindow()
        {
            InitializeComponent();
            main = this;
            _TCPListener = new MyTCPListener();
            _TCPSender = new MyTCPSender();
            _UDPListener = new MyUDPListener();
            _UDPSender = new MyUDPSender();
            
            // Inizializzo  info user
            textUNInfo.Text = _referenceData.LocalUser.Name;
            if (_referenceData.LocalUser.Status.Equals("online"))
                checkUSBox.IsChecked = false;
            else
                checkUSBox.IsChecked = true;

            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
            // TODO: vedere se fare una copia o no e lasciarla interna al sistema
            string filename = _referenceData.LocalUser.ProfileImagePath;
            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
            {
                string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string archiveFolder = Path.Combine(currentDirectory, "Resources");
                string[] files = Directory.GetFiles(archiveFolder, _referenceData.LocalUser.ProfileImagePath);
                filename = files[0];
            }
            ImageProfile.Source = new BitmapImage(new Uri(filename));

            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
            // dell'utente corrente
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();

            //Avvio thread che invia immagine di profilo
            //TODO: da gestire path!!!
            Task.Run(() => { _TCPSender.Send(_referenceData.LocalUser.ProfileImagePath); });

            // Avvia due ulteriori thread per gestire i due ascoltatori TCP e UDP
            Task.Run(() => { _TCPListener.Listener(); });
            Task.Run(() => { _UDPListener.Listener(); });

        }

        private void dispatcherTimer_Tick(object sender, EventArgs e){
            _UDPSender.Sender();
        }

        private void ButtonSend_Click(object sender, RoutedEventArgs e){
            string message = WriteMessage.Text;
            Task.Run(() => { _TCPSender.Send(message); });
        }

        private void HostInfo_MouseUp(object sender, MouseButtonEventArgs e){
            // Per ora è solo un elemento, quindi prendo primo elemeto lista
            if (_referenceData.selectedHost.Equals("")) {
                if (_referenceData.Users.Count != 0) {
                    _referenceData.selectedHost = _referenceData.Users.First().Key;
                    HostSelected.Text += " SELECTED";
                }
            }
            else {
                HostSelected.Text = "Host Info";
                _referenceData.selectedHost = "";
            }
            Console.WriteLine("Ho cliccato host info");
        }

        /// <summary>
        /// In caso di cambiamento di username, vengono aggiornate le informazioni dell'utente e salvate su disco
        /// </summary>
        /// <param name="sender">Non usato</param>
        /// <param name="e">Non usato</param>
        private void TextUNInfo_TextChanged(object sender, TextChangedEventArgs e){
            string message = textUNInfo.Text;
            _referenceData.LocalUser.Name = message;
            _referenceData.SaveJson();
        }

        /// <summary>
        /// In caso di cambiamento di state, vengono aggiornate le informazioni dell'utente e salvate su disco
        /// </summary>
        /// <param name="sender">Non usato</param>
        /// <param name="e">Non usato</param>
        private void CheckUSBox_Checked(object sender, RoutedEventArgs e){
            if (checkUSBox.IsChecked == true){
                _referenceData.LocalUser.Status = "offline";
                _referenceData.SaveJson();
            }
            else if (checkUSBox.IsChecked == false){
                _referenceData.LocalUser.Status = "online";
                _referenceData.SaveJson();
            }

        }

        /// <summary>
        /// Con un click sull'immagine di profilo corrente è possibile cambiare l'immagine profilo
        /// </summary>
        /// <param name="sender">Non usato</param>
        /// <param name="e">Non usato</param>
        private void Image_MouseDown(object sender, MouseButtonEventArgs e){
            // Strumento base di windows per scegliere un file nel filesystem
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            // Formato file di default e filtri sui file che si possono selezionare. In questo caso solo i file immagine.
            dlg.DefaultExt = ".png";
            dlg.Filter = "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png";//"JPEG Files (*.jpeg)|*.jpeg|PNG Files (*.png)|*.png|JPG Files (*.jpg)|*.jpg|GIF Files (*.gif)|*.gif";
            
            // Col metodo ShowDialog viene visualizzata la schermata di OpenFileDialog
            Nullable<bool> result = dlg.ShowDialog();
            
            // Se l'utente seleziona effettivamente qualcosa...
            if (result == true)
            {
                // Apro il file e lo sostituisco all'immagine corrente SOLO se è diverso, in questo caso uso l'hash
                // TODO: da rivedere uso hash + dimensioni
                string filename = dlg.FileName;
                ImageProfile.Width = 50; ImageProfile.Height = 50; 
                using (SHA256 sha = SHA256.Create())
                {
                    FileStream file = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(file);
                    if (!BitConverter.ToString(hash).Replace("-", String.Empty).Equals(_referenceData.LocalUser.ProfileImageHash))
                    {
                        ImageProfile.Source = new BitmapImage(new Uri(filename));
                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

                        if (!hashImage.Equals(_referenceData.LocalUser.ProfileImageHash))
                        {
                            _referenceData.LocalUser.ProfileImageHash = hashImage;
                            _referenceData.LocalUser.ProfileImagePath = filename;
                            _referenceData.SaveJson();

                            //TODO: invio a tutti gli host in rete
                            _TCPSender.Send(filename); // Deve essere inviato a tutti gli utenti connessi 
                        }

                    }
                }
            }
        }

        public void UpdateProfileHost(string ip) {
            MainWindow.main.textNToInsert.Text = _referenceData.Users[ip].Name;
            MainWindow.main.textSToInsert.Text = _referenceData.Users[ip].Status;

            HostImage.Width = 50; HostImage.Height = 50;
            string filename = "";
            if (_referenceData.Users[ip].ProfileImagePath.Equals(_referenceData.defaultImage))
            {
                string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string archiveFolder = Path.Combine(currentDirectory, "Resources");
                string[] files = Directory.GetFiles(archiveFolder, _referenceData.LocalUser.ProfileImagePath);
                filename = files[0];
            }
            else {
                string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string[] files = Directory.GetFiles(currentDirectory, _referenceData.LocalUser.ProfileImagePath);
                filename = files[0];
            }
            var file = File.OpenRead(filename);

            HostImage.Source = new BitmapImage(new Uri(filename));
            file.Close();
        }

    }
}
