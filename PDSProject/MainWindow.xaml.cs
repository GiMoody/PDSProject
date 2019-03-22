﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using System.Threading;

namespace PDSProject {
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window {

        bool selected = false;
        private string save_path;

        public static MainWindow main; // Usato come riferimento da parte delle altri classi
        SharedInfo _referenceData = SharedInfo.Instance;
        MyTCPListener _TCPListener;
        MyTCPSender _TCPSender;
        MyUDPListener _UDPListener;
        MyUDPSender _UDPSender;
        CancellationTokenSource source;

        private System.Windows.Forms.ContextMenu contextMenu;
        
        System.Windows.Forms.NotifyIcon ni = new System.Windows.Forms.NotifyIcon();

        public MainWindow() {

            InitializeComponent();
            ChoosePath.IsChecked = true;
            pathName.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Initialize contextMenu 
            this.contextMenu = new System.Windows.Forms.ContextMenu();
            this.contextMenu.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Show", new System.EventHandler(Show_Click)));
            this.contextMenu.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Hide", new System.EventHandler(Hide_Click)));
            this.contextMenu.MenuItems.Add(2, new System.Windows.Forms.MenuItem("Exit", new System.EventHandler(Exit_Click)));
            
            //Initialize icon
            ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_white.ico"));
            ni.Visible = true;
            ni.ContextMenu = this.contextMenu;
            ni.Text = "PDS_Condividi";
            ni.DoubleClick +=
                delegate (object sender, EventArgs args){
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };

            main = this;
            _TCPListener = new MyTCPListener();
            _TCPSender = new MyTCPSender();
            _UDPListener = new MyUDPListener();
            _UDPSender = new MyUDPSender();

            source = new CancellationTokenSource();

            // Inizializzo info user
            // da modificare parte URI
            textUserName.Text = _referenceData.LocalUser.Name;
            if (_referenceData.LocalUser.Status.Equals("Online")) {
                comboStatus.Text = "Online";
                statusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
            } else {
                comboStatus.Text = "Offline";
                statusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
            }

            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
            // TODO: vedere se fare una copia o no e lasciarla interna al sistema
            string filename = _referenceData.LocalUser.ProfileImagePath;
            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));

            ImageProfile.Fill = imgBrush;

            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
            // dell'utente corrente
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
            if (_referenceData.useTask) {
                Task.Run(async () => { await _UDPListener.ListenerA(source.Token); });
                Task.Run(() => { PipeClient(); });
            } else {
                Thread t = new Thread(new ThreadStart(_UDPListener.Listener));
                t.Start();
                Thread tc = new Thread(new ThreadStart(PipeClient));
                tc.Start();
            }

            StartTCPListener();

            _referenceData.isFirst = true;

            // Aggiunge registri per menù contestuale
            /** TODO: AddOptionContextMenu dovrà essere spostato per essere eseguito solo dallo Wizard o durante le operazioni di 
             * configurazioni una sola volta in modalità Admin. Per adesso il codice si limita a controllare se l'esecuzione è in
             * modalità admin o no ed esegue il metodo di conseguenza */
            AddOptionContextMenu();
        }
        
        /// <summary>
        /// Gestione eventi del context menù (icona in basso)
        /// </summary>
        protected void Exit_Click(Object sender, System.EventArgs e){
            Close();
        }
        protected void Hide_Click(Object sender, System.EventArgs e){
            Hide();
        }
        protected void Show_Click(Object sender, System.EventArgs e) {
            Show();
        }
        ///
        

        /// <summary>
        /// Test inserimento opzione menù contestuale (tasto destro), funziona per ora ma non fa nulla
        /// da gestire la comunicazione tra processi o trovare un modo per evitare di avere 2 istanze dello stesso processo
        /// </summary>
        private void AddOptionContextMenu() {
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
        private void PipeClient() {
            while (true) {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".", "PSDPipe", PipeDirection.In)) {
                    pipeClient.Connect();

                    Console.WriteLine("Connected to pipe.");
                    Console.WriteLine("There are currently {0} pipe server instances open.",
                        pipeClient.NumberOfServerInstances);
                    using (StreamReader sr = new StreamReader(pipeClient)) {
                        // Display the read text to the console
                        string temp;
                        while ((temp = sr.ReadLine()) != null) {
                            Console.WriteLine("Received from server: {0}", temp);
                            string copia = temp;
                            _referenceData.PathFileToSend.Add(copia);
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { Test(copia); }));
                        }
                    }
                }
            }
        }

        public void StartTCPListener() {
            if (_referenceData.useTask) {
                CancellationToken token = source.Token;
                Task.Run(async () => { await _TCPListener.ListenerA(token); });
            } else {
                Thread t = new Thread(new ThreadStart(_TCPListener.ListenerB));
                t.Start();
            }

        }

        public void SendCallback() {
            //_TCPSender. SendCallback();
            _TCPListener.StopServer();
        }

        public void Test(string e) {
            /*test = e;
            foreach(string s in e) {
                textInfoMessage.Text += s;
            }*/
            textInfoMessage.Text += e;
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e) {
            // Controllo se c'è stato un cambio di rete 
            if (_referenceData.LocalIPAddress.Equals("") && _referenceData.BroadcastIPAddress.Equals("")) {
                foreach (KeyValuePair<string, string> t in _referenceData.Ips) {
                    _UDPSender.Sender(t.Key);
                }
            } else
                _UDPSender.Sender(_referenceData.BroadcastIPAddress);
        }


        /// <summary>
        /// Gestione stato Minimize (icona nella tray)
        /// </summary>
        protected override void OnStateChanged(EventArgs e) {
            if (WindowState == System.Windows.WindowState.Minimized)
                this.Hide();

            base.OnStateChanged(e);
        }

        /// <summary>
        /// Gestione evento OnClosing (messageBox)
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e) {

            // Configure the message box to be displayed
            string messageBoxText = "Vuoi chiudere l'applicazione?\n(Cliccando ''NO'' rimarrà attiva in basso)";
            string caption = "Attenzione";
            MessageBoxButton button = MessageBoxButton.YesNo;
            MessageBoxImage icon = MessageBoxImage.Question;

            // Display message box
            MessageBoxResult result = MessageBox.Show(messageBoxText, caption, button, icon);

            switch (result) {
                case MessageBoxResult.Yes:
                    e.Cancel = false;
                    if (_referenceData.useTask) {
                        source.Cancel();
                    }
                    _TCPListener.StopServer();
                    break;
                case MessageBoxResult.No:
                    this.WindowState = WindowState.Minimized;
                    e.Cancel = true;
                    break;
            }

        }

        /// <summary>
        /// Selezionando l'icona dell'amico, appare/scompare un canvas blu
        /// </summary>
        private void Canvas_Visible(object sender, MouseButtonEventArgs e) {

            if (selected == false) {
                canvasSelect.Background = new SolidColorBrush(Colors.SkyBlue);
                SendButton.Visibility = Visibility.Visible;
                UndoButton.Visibility = Visibility.Visible;
                selected = true;
                if (_referenceData.Users.Count > 0) {
                    _referenceData.selectedHost = _referenceData.Users.First().Key;
                }
            } else {
                canvasSelect.Background = new SolidColorBrush(Colors.AliceBlue);
                SendButton.Visibility = Visibility.Hidden;
                UndoButton.Visibility = Visibility.Hidden;
                selected = false;
                _referenceData.selectedHost = "";
            }
        }

        /// <summary>
        /// Modifica immagine profilo
        /// </summary>
        private async void Image_MouseDown(object sender, MouseButtonEventArgs e) {
            // Strumento base di windows per scegliere un file nel filesystem
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            // Formato file di default e filtri sui file che si possono selezionare. In questo caso solo i file immagine.
            dlg.DefaultExt = ".png";
            dlg.Filter =
                "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png"; //"JPEG Files (*.jpeg)|*.jpeg|PNG Files (*.png)|*.png|JPG Files (*.jpg)|*.jpg|GIF Files (*.gif)|*.gif";

            // Col metodo ShowDialog viene visualizzata la schermata di OpenFileDialog
            Nullable<bool> result = dlg.ShowDialog();

            //Se l'utente seleziona effettivamente qualcosa...
            if (result == true) {
                // Apro il file e lo sostituisco all'immagine corrente SOLO se è diverso, in questo caso uso l'hash
                // TODO: da rivedere uso hash + dimensioni
                string filename = dlg.FileName;
                //ImageProfile.Width = 50; ImageProfile.Height = 50;
                using (SHA256 sha = SHA256.Create()) {
                    FileStream file = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(file);
                    if (!BitConverter.ToString(hash).Replace("-", String.Empty)
                        .Equals(_referenceData.LocalUser.ProfileImageHash)) {
                        ImageBrush imgBrush = new ImageBrush();
                        imgBrush.ImageSource = new BitmapImage(new Uri(filename));

                        ImageProfile.Fill = imgBrush;

                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

                        if (!hashImage.Equals(_referenceData.LocalUser.ProfileImageHash)) {
                            _referenceData.LocalUser.ProfileImageHash = hashImage;
                            _referenceData.LocalUser.ProfileImagePath = filename;
                            _referenceData.SaveJson();
                            _referenceData.hasChangedProfileImage = true;
                            //TODO: invio a tutti gli host in rete
                            if (_referenceData.useTask)
                            {
                                _referenceData.FileToFinish.Add(_referenceData.LocalUser.ProfileImagePath, "start");
                                await _TCPSender.SendA(new List<string>() { _referenceData.LocalUser.ProfileImagePath }); // Deve essere inviato a tutti gli utenti connessi 
                            }
                            else
                            {
                                Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                                t.Start(new List<string>() { _referenceData.LocalUser.ProfileImagePath });
                            }
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Setting Visible
        /// </summary>
        private void Settings_visible(object sender, MouseButtonEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Main Visible
        /// </summary>
        private void BackButton_OnClick(object sender, RoutedEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Hidden;
            MainCanvas.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Applicazione modifiche utente
        /// Gestione caso username vuoto
        /// </summary>
        private void ApplyButton_OnClick(object sender, RoutedEventArgs e) {

            if (this.textChangeName.Text == "") {
                string messageWarning = "Inserire Username";
                string caption_warning = "Attenzione";
                MessageBoxImage icon_warning = MessageBoxImage.Information;
                MessageBoxButton button_warning = MessageBoxButton.OK;

                MessageBox.Show(messageWarning, caption_warning, button_warning, icon_warning);
            } else {

                // Configure the message box to be displayed
                string messageBoxText = "Modifiche Applicate";
                string caption = "Attenzione";
                MessageBoxImage icon = MessageBoxImage.Information;
                MessageBoxButton button = MessageBoxButton.OK;

                MessageBox.Show(messageBoxText, caption, button, icon);

                this.textUserName.Text = this.textChangeName.Text;

                _referenceData.LocalUser.Name = this.textChangeName.Text;
                _referenceData.SaveJson();
            }

        }

        /// <summary>
        /// Modifica path salvataggio
        /// </summary>
        private void CheckBox_Uncheck(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Visible;
            pathName.IsReadOnly = false;
            pathName.Background = new SolidColorBrush(Colors.White);
        }

        /// <summary>
        /// Path Predefinito
        /// </summary>
        private void CheckBox_Check(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Hidden;
            pathName.IsReadOnly = true;
            pathName.Background = new SolidColorBrush(Colors.LightGray);
            pathName.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// Selezione path personalizzato
        /// </summary>
        private void FolderButton_OnClick(object sender, RoutedEventArgs e) {

            var save_dlg = new CommonOpenFileDialog();
            save_dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            save_dlg.IsFolderPicker = true;
            CommonFileDialogResult result = save_dlg.ShowDialog();
            if (result.ToString() == "Ok") {
                save_path = save_dlg.FileName;
                pathName.Text = save_path;
            }
        }

        /// <summary>
        /// Combobox Status changed (non sicura funzionamento)
        /// </summary>
        private void ComboStatus_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (comboStatus.Text == "Online") {
                statusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                _referenceData.LocalUser.Status = "offline";
                _referenceData.SaveJson();
            } else if (comboStatus.Text == "Offline") {
                statusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                _referenceData.LocalUser.Status = "online";
                _referenceData.SaveJson();
            }
        }

        /// <summary>
        /// Modifica info amico
        /// </summary>
        public void UpdateProfileHost(string ip) {
            MainWindow.main.textNFriend.Text = _referenceData.Users[ip].Name;
            MainWindow.main.textSFriend.Text = _referenceData.Users[ip].Status;

            if (_referenceData.Users[ip].Status == "online") {
                MainWindow.main.textSFriend.Foreground = new SolidColorBrush(Colors.Green);
            } else if (_referenceData.Users[ip].Status == "offline" || _referenceData.Users[ip].Status == "") {
                MainWindow.main.textSFriend.Foreground = new SolidColorBrush(Colors.DarkRed);
            }

            //HostImage.Width = 50; HostImage.Height = 50;
            string filename = "";
            string tmp_name = "";

            if (!(tmp_name = _referenceData.Users[ip].ProfileImagePath).Equals("") && File.Exists(tmp_name))
                filename = _referenceData.Users[ip].ProfileImagePath;
            else {
                if (_referenceData.Users[ip].ProfileImagePath.Equals(_referenceData.defaultImage) ||
                    !_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
                    filename = Utility.FileNameToHost(_referenceData.defaultImage);
                else //if (_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
                {
                    filename = _referenceData.Users[ip].ProfileImagePath;

                    /*string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                    string[] files = Directory.GetFiles(currentDirectory, _referenceData.Users[ip].ProfileImagePath);
                    filename = files[0];*/
                }
            }

            if (filename.Equals("")) return;
                try {
                    var file = File.OpenRead(filename);
                    ImageBrush imgBrush = new ImageBrush();
                    imgBrush.ImageSource = new BitmapImage(new Uri(filename));

                    imageFriend.Fill = imgBrush;
                    file.Close();
                } catch (UnauthorizedAccessException e) {
                    Console.WriteLine($"File not yet reciced : {e.Message}");
                } catch (Exception e) {
                    Console.WriteLine($"Exception : {e.Message}");
                }

                //HostImage.Source = new BitmapImage(new Uri(filename));
            }
        
        /// <summary>
        /// Aggiorna immagine del profilo (invia l'immagine)
        /// </summary>
        public async void SendProfileImage() {
            _referenceData.hasChangedProfileImage = true;
            _referenceData.FileToFinish.Add(_referenceData.LocalUser.ProfileImagePath, "start");
            if (_referenceData.useTask)
            {
                await Task.Run(async() => {
                    await _TCPSender.SendA(new List<string>() {_referenceData.LocalUser.ProfileImagePath});
                }); 
            }
            else
            {

                Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                t.Start(new List<string>() { _referenceData.LocalUser.ProfileImagePath });
            }
        }

        /// <summary>
        /// Invio file
        /// </summary>
        private async void ButtonSend_Click(object sender, RoutedEventArgs e) {

            if (_referenceData.PathFileToSend.Count > 0 && _referenceData.selectedHost != "") {
                //Task.Run(() => { _TCPSender.Send(_referenceData.PathFileToSend); });
                foreach (string path in _referenceData.PathFileToSend)
                    _referenceData.FileToFinish.Add(path, "start");
                _referenceData.PathFileToSend.Clear();

                if (_referenceData.useTask)
                    await _TCPSender.SendA(_referenceData.FileToFinish.Keys.ToList());

                else {
                    Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                    t.Start(_referenceData.FileToFinish.Keys.ToList());
                }
            }
        }
    }
}


//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;
//using System.Windows.Threading;
//using System.Security.Cryptography;
//using System.IO;
//using Path = System.IO.Path;
//using System.Reflection;
//using Microsoft.Win32;
//using System.Diagnostics;
//using System.IO.Pipes;
//using System.Security.Principal;

//namespace PDSProject
//{
//    /// <summary>
//    /// Logica di interazione per MainWindow.xaml
//    /// La grafica della finestra è da cambiare pesantemente, tutto è stato fatto per testare il funzionamento
//    /// </summary>
//    public partial class MainWindow : Window
//    {

//        public static MainWindow main;  // Usato come riferimento da parte delle altri classi
//        SharedInfo _referenceData = SharedInfo.Instance;
//        MyTCPListener _TCPListener;
//        MyTCPSender _TCPSender;
//        MyUDPListener _UDPListener;
//        MyUDPSender _UDPSender;

//        private string[] test = null;

//        public MainWindow()
//        {
            
//            InitializeComponent();
            
//            main = this;
//            _TCPListener = new MyTCPListener();
//            _TCPSender = new MyTCPSender();
//            _UDPListener = new MyUDPListener();
//            _UDPSender = new MyUDPSender();
            
//            // Inizializzo  info user
//            textUNInfo.Text = _referenceData.LocalUser.Name;
//            if (test != null && test.Length > 0)
//                textInfoMessage.Text = test.Length.ToString();
//            if (_referenceData.LocalUser.Status.Equals("online"))
//                checkUSBox.IsChecked = false;
//            else
//                checkUSBox.IsChecked = true;

//            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
//            // TODO: vedere se fare una copia o no e lasciarla interna al sistema
//            string filename = _referenceData.LocalUser.ProfileImagePath;
//            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
//                filename = Utility.FileNameToPath("Resources", _referenceData.defaultImage);
//            ImageProfile.Source = new BitmapImage(new Uri(filename));

//            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
//            // dell'utente corrente
//            DispatcherTimer dispatcherTimer = new DispatcherTimer();
//            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
//            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
//            dispatcherTimer.Start();

//            //Avvia due ulteriori thread per gestire i due ascoltatori TCP e UDP
//            Task.Run(() => { _TCPListener.Listener(); });
//            Task.Run(() => { _UDPListener.Listener(); });
//            Task.Run(() => { PipeClient(); });

//            // Aggiunge registri per menù contestuale
//            /** TODO: AddOptionContextMenu dovrà essere spostato per essere eseguito solo dallo Wizard o durante le operazioni di 
//             * configurazioni una sola volta in modalità Admin. Per adesso il codice si limita a controllare se l'esecuzione è in
//             * modalità admin o no ed esegue il metodo di conseguenza */
//            AddOptionContextMenu();
//        }
        
//        /// <summary>
//        /// Test inserimento opzione menù contestuale (tasto destro), funziona per ora ma non fa nulla
//        /// da gestire la comunicazione tra processi o trovare un modo per evitare di avere 2 istanze dello stesso processo
//        /// </summary>
//        private void AddOptionContextMenu(){
//            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
//            if (principal.IsInRole(WindowsBuiltInRole.Administrator)) {
//                string nameExe = Process.GetCurrentProcess().ProcessName + ".exe";
//                string pathExe = Utility.FileNameToPath("", nameExe);

//                // Set RegistryKey per file
//                //RegistryKey _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
//                RegistryKey _key = Registry.ClassesRoot.OpenSubKey("*\\Shell", true);
//                RegistryKey newkey = _key.CreateSubKey("PDSApp Invia File");
//                RegistryKey subNewkey = newkey.CreateSubKey("Command");
                
//                //subNewkey.SetValue("", "D:\\Utenti\\GMoody\\Documents\\Università\\Primo Anno Magistrale\\2 - Programmazione di Sistema\\MalnatiProgetto\\PDSProjectGIT\\PDSProject\\PDSProject\\bin\\Debug\\PDSProject.exe %1");//"C:\\Program Files\\7-Zip\\7zG.exe");
//                subNewkey.SetValue("", pathExe + " %1");

//                // Set RegistryKey per directory
//                _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
//                newkey = _key.CreateSubKey("PDSApp Invia Cartella");
//                subNewkey = newkey.CreateSubKey("Command");
                
//                subNewkey.SetValue("", pathExe + " %1");

//                subNewkey.Close();
//                newkey.Close();
//                _key.Close();
//            }
//        }

//        /// <summary>
//        /// Implementazione della NamedPipeClient
//        /// Questa rimane in ascolto di possibili istanze PSDProject istanziate col menù contestuale, queste inviano il path del file da inviare all'istanza principale
//        /// e termina l'esecuzione subito dopo
//        /// /// </summary>
//        /// <param name="e"></param>
//        private void PipeClient()
//        {
//            while (true)
//            {
//                using (NamedPipeClientStream pipeClient =
//                new NamedPipeClientStream(".", "PSDPipe", PipeDirection.In))
//                {
//                    pipeClient.Connect();

//                    Console.WriteLine("Connected to pipe.");
//                    Console.WriteLine("There are currently {0} pipe server instances open.",
//                       pipeClient.NumberOfServerInstances);
//                    using (StreamReader sr = new StreamReader(pipeClient))
//                    {
//                        // Display the read text to the console
//                        string temp;
//                        while ((temp = sr.ReadLine()) != null)
//                        {
//                            Console.WriteLine("Received from server: {0}", temp);
//                            string copia = temp;
//                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
//                            {
//                                Test(copia);
//                            }));
//                        }
//                    }
//                }
//            }
//        }

//        public void Test(string e){
//            /*test = e;
//            foreach(string s in e) {
//                textInfoMessage.Text += s;
//            }*/
//            textInfoMessage.Text += e;

//        }

//        private void dispatcherTimer_Tick(object sender, EventArgs e){
//            _UDPSender.Sender();
//        }

//        private void ButtonSend_Click(object sender, RoutedEventArgs e){
//            string message = WriteMessage.Text;
//            Task.Run(() => { _TCPSender.Send(message); });
//        }

//        /**
//         * TODO: da modificare il sistema di invio file, per ora si clicca un bottone. L'obbiettivo sarebbe inviarlo solo tramite il menù contestuale
//         */
//        private void HostInfo_MouseUp(object sender, MouseButtonEventArgs e){
//            // Per ora è solo un elemento, quindi prendo primo elemeto lista
//            if (_referenceData.selectedHost.Equals("")) {
//                if (_referenceData.Users.Count != 0) {
//                    _referenceData.selectedHost = _referenceData.Users.First().Key;
//                    HostSelected.Text += " SELECTED";
//                }
//            }
//            else {
//                HostSelected.Text = "Host Info";
//                _referenceData.selectedHost = "";
//            }
//            Console.WriteLine("Ho cliccato host info");
//        }

//        /// <summary>
//        /// In caso di cambiamento di username, vengono aggiornate le informazioni dell'utente e salvate su disco
//        /// </summary>
//        /// <param name="sender">Non usato</param>
//        /// <param name="e">Non usato</param>
//        private void TextUNInfo_TextChanged(object sender, TextChangedEventArgs e){
//            string message = textUNInfo.Text;
//            _referenceData.LocalUser.Name = message;
//            _referenceData.SaveJson();
//        }

//        /// <summary>
//        /// In caso di cambiamento di state, vengono aggiornate le informazioni dell'utente e salvate su disco
//        /// </summary>
//        /// <param name="sender">Non usato</param>
//        /// <param name="e">Non usato</param>
//        private void CheckUSBox_Checked(object sender, RoutedEventArgs e){
//            if (checkUSBox.IsChecked == true){
//                _referenceData.LocalUser.Status = "offline";
//                _referenceData.SaveJson();
//            }
//            else if (checkUSBox.IsChecked == false){
//                _referenceData.LocalUser.Status = "online";
//                _referenceData.SaveJson();
//            }

//        }

//        /// <summary>
//        /// Con un click sull'immagine di profilo corrente è possibile cambiare l'immagine profilo
//        /// </summary>
//        /// <param name="sender">Non usato</param>
//        /// <param name="e">Non usato</param>
//        private void Image_MouseDown(object sender, MouseButtonEventArgs e){
//            // Strumento base di windows per scegliere un file nel filesystem
//            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

//            // Formato file di default e filtri sui file che si possono selezionare. In questo caso solo i file immagine.
//            dlg.DefaultExt = ".png";
//            dlg.Filter = "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png";//"JPEG Files (*.jpeg)|*.jpeg|PNG Files (*.png)|*.png|JPG Files (*.jpg)|*.jpg|GIF Files (*.gif)|*.gif";
            
//            // Col metodo ShowDialog viene visualizzata la schermata di OpenFileDialog
//            Nullable<bool> result = dlg.ShowDialog();
            
//            // Se l'utente seleziona effettivamente qualcosa...
//            if (result == true)
//            {
//                // Apro il file e lo sostituisco all'immagine corrente SOLO se è diverso, in questo caso uso l'hash
//                // TODO: da rivedere uso hash + dimensioni
//                string filename = dlg.FileName;
//                ImageProfile.Width = 50; ImageProfile.Height = 50; 
//                using (SHA256 sha = SHA256.Create())
//                {
//                    FileStream file = File.OpenRead(filename);
//                    byte[] hash = sha.ComputeHash(file);
//                    if (!BitConverter.ToString(hash).Replace("-", String.Empty).Equals(_referenceData.LocalUser.ProfileImageHash))
//                    {
//                        ImageProfile.Source = new BitmapImage(new Uri(filename));
//                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

//                        if (!hashImage.Equals(_referenceData.LocalUser.ProfileImageHash))
//                        {
//                            _referenceData.LocalUser.ProfileImageHash = hashImage;
//                            _referenceData.LocalUser.ProfileImagePath = filename;
//                            _referenceData.SaveJson();
//                            _referenceData.hasChangedProfileImage = true;
//                            //TODO: invio a tutti gli host in rete
//                            _TCPSender.Send(filename); // Deve essere inviato a tutti gli utenti connessi 
//                        }

//                    }
//                }
//            }
//        }

//        public void UpdateProfileHost(string ip) {
//            MainWindow.main.textNToInsert.Text = _referenceData.Users[ip].Name;
//            MainWindow.main.textSToInsert.Text = _referenceData.Users[ip].Status;

//            HostImage.Width = 50; HostImage.Height = 50;
//            string filename = "";
//            if (_referenceData.Users[ip].ProfileImagePath.Equals(_referenceData.defaultImage) || !_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
//                filename = Utility.FileNameToPath("Resources",_referenceData.defaultImage);
//            else //if (_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
//            {
//                filename = Utility.FileNameToPath("Resources", _referenceData.Users[ip].ProfileImagePath);

//                /*string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
//                string[] files = Directory.GetFiles(currentDirectory, _referenceData.Users[ip].ProfileImagePath);
//                filename = files[0];*/
//            }
//            /*else
//            {
//                string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

//                string archiveFolder = Path.Combine(currentDirectory, "Resources");
//                string[] files = Directory.GetFiles(archiveFolder, _referenceData.defaultImage);
//                filename = files[0];
//            }*/
//            var file = File.OpenRead(filename);

//            HostImage.Source = new BitmapImage(new Uri(filename));
//            file.Close();
//        }

//        public void SendProfileImage() {
//            _referenceData.hasChangedProfileImage = true;
//            Task.Run(() => { _TCPSender.Send(_referenceData.LocalUser.ProfileImagePath); });
//        }

//    }
//}
