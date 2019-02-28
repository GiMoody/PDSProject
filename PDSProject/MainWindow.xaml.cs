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
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

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

        private string[] test = null;

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
            if (test != null && test.Length > 0)
                textInfoMessage.Text = test.Length.ToString();
            if (_referenceData.LocalUser.Status.Equals("online"))
                checkUSBox.IsChecked = false;
            else
                checkUSBox.IsChecked = true;

            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
            // TODO: vedere se fare una copia o no e lasciarla interna al sistema
            string filename = _referenceData.LocalUser.ProfileImagePath;
            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToPath("Resources", _referenceData.defaultImage);
            ImageProfile.Source = new BitmapImage(new Uri(filename));

            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
            // dell'utente corrente
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();

            //Avvia due ulteriori thread per gestire i due ascoltatori TCP e UDP
            Task.Run(() => { _TCPListener.Listener(); });
            Task.Run(() => { _UDPListener.Listener(); });
            Task.Run(() => { PipeClient(); });

            // Aggiunge registri per menù contestuale
            /** TODO: AddOptionContextMenu dovrà essere spostato per essere eseguito solo dallo Wizard o durante le operazioni di 
             * configurazioni una sola volta in modalità Admin. Per adesso il codice si limita a controllare se l'esecuzione è in
             * modalità admin o no ed esegue il metodo di conseguenza */
            AddOptionContextMenu();
        }
        
        /// <summary>
        /// Test inserimento opzione menù contestuale (tasto destro), funziona per ora ma non fa nulla
        /// da gestire la comunicazione tra processi o trovare un modo per evitare di avere 2 istanze dello stesso processo
        /// </summary>
        private void AddOptionContextMenu(){
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (principal.IsInRole(WindowsBuiltInRole.Administrator)) {
                string nameExe = Process.GetCurrentProcess().ProcessName + ".exe";
                string pathExe = Utility.FileNameToPath("", nameExe);

                // Set RegistryKey per file
                //RegistryKey _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
                RegistryKey _key = Registry.ClassesRoot.OpenSubKey("*\\Shell", true);
                RegistryKey newkey = _key.CreateSubKey("PDSApp Invia File");
                RegistryKey subNewkey = newkey.CreateSubKey("Command");
                
                //subNewkey.SetValue("", "D:\\Utenti\\GMoody\\Documents\\Università\\Primo Anno Magistrale\\2 - Programmazione di Sistema\\MalnatiProgetto\\PDSProjectGIT\\PDSProject\\PDSProject\\bin\\Debug\\PDSProject.exe %1");//"C:\\Program Files\\7-Zip\\7zG.exe");
                subNewkey.SetValue("", pathExe + " %1");

                // Set RegistryKey per directory
                _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
                newkey = _key.CreateSubKey("PDSApp Invia Cartella");
                subNewkey = newkey.CreateSubKey("Command");
                
                subNewkey.SetValue("", pathExe + " %1");

                subNewkey.Close();
                newkey.Close();
                _key.Close();
            }
        }

        /// <summary>
        /// Implementazione della NamedPipeClient
        /// Questa rimane in ascolto di possibili istanze PSDProject istanziate col menù contestuale, queste inviano il path del file da inviare all'istanza principale
        /// e termina l'esecuzione subito dopo
        /// /// </summary>
        /// <param name="e"></param>
        private void PipeClient()
        {
            while (true)
            {
                using (NamedPipeClientStream pipeClient =
                new NamedPipeClientStream(".", "PSDPipe", PipeDirection.In))
                {
                    pipeClient.Connect();

                    Console.WriteLine("Connected to pipe.");
                    Console.WriteLine("There are currently {0} pipe server instances open.",
                       pipeClient.NumberOfServerInstances);
                    using (StreamReader sr = new StreamReader(pipeClient))
                    {
                        // Display the read text to the console
                        string temp;
                        while ((temp = sr.ReadLine()) != null)
                        {
                            Console.WriteLine("Received from server: {0}", temp);
                            string copia = temp;
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                            {
                                Test(copia);
                            }));
                        }
                    }
                }
            }
        }

        public void Test(string e){
            /*test = e;
            foreach(string s in e) {
                textInfoMessage.Text += s;
            }*/
            textInfoMessage.Text += e;

        }

        private void dispatcherTimer_Tick(object sender, EventArgs e){
            _UDPSender.Sender();
        }

        private void ButtonSend_Click(object sender, RoutedEventArgs e){
            string message = WriteMessage.Text;
            Task.Run(() => { _TCPSender.Send(message); });
        }

        /**
         * TODO: da modificare il sistema di invio file, per ora si clicca un bottone. L'obbiettivo sarebbe inviarlo solo tramite il menù contestuale
         */
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
                            _referenceData.hasChangedProfileImage = true;
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
            if (_referenceData.Users[ip].ProfileImagePath.Equals(_referenceData.defaultImage) || !_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
                filename = Utility.FileNameToPath("Resources",_referenceData.defaultImage);
            else //if (_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
            {
                filename = Utility.FileNameToPath("Resources", _referenceData.Users[ip].ProfileImagePath);

                /*string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string[] files = Directory.GetFiles(currentDirectory, _referenceData.Users[ip].ProfileImagePath);
                filename = files[0];*/
            }
            /*else
            {
                string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                string archiveFolder = Path.Combine(currentDirectory, "Resources");
                string[] files = Directory.GetFiles(archiveFolder, _referenceData.defaultImage);
                filename = files[0];
            }*/
            var file = File.OpenRead(filename);

            HostImage.Source = new BitmapImage(new Uri(filename));
            file.Close();
        }

        public void SendProfileImage() {
            _referenceData.hasChangedProfileImage = true;
            Task.Run(() => { _TCPSender.Send(_referenceData.LocalUser.ProfileImagePath); });
        }

    }
}
