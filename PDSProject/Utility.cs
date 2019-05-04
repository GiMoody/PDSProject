using System;

using System.IO;
using System.Reflection;

namespace PDSProject
{
    class Utility
    {
        /// <summary>
        /// Dal nome del file ottiene il path assoluto dell' immagine di profilo di default
        /// ma sarà usata poi per ogni immagine di profilo degli altri hosts salvata all'interno di una specifica cartella
        /// </summary>
        /// <param name="directory">Directory in cui si trova il file a partire dalla working directory</param>
        /// <param name="filename">Nome del file</param>
        /// <returns>Path assoluto del file</returns>
        static public string FileNameToPath(string directory, string filename) {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string archiveFolder = currentDirectory;

            if (directory != null && directory.Length != 0)
                archiveFolder = Path.Combine(currentDirectory, directory);
            
            string[] files = Directory.GetFiles(archiveFolder, filename);

            if (files.Length > 0)
                return files[0];
            else
                return "";
        }


        /// <summary>
        /// Dal nome del file ottiene il path assoluto del file contenuto nlla cartella System di Resources
        /// </summary>
        /// <param name="filename">Nome del file</param>
        /// <returns>Path assoluto del file</returns>
        public static string FileNameToSystem(string filename) {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "System");
            
            string[] files = Directory.GetFiles(archiveFolder, filename);

            if (files.Length > 0)
                return files[0];
            else
                return "";
        }

        /// <summary>
        /// Ritorna la stringa del path della cartella Resources
        /// </summary>
        /// <returns>Path assoluto della directory Resources</returns>
        public static string PathResources () {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Ritorna la stringa del path della cartella System in Resources
        /// </summary>
        /// <returns>Path assoluto della directory System in Resources</returns>
        public static string PathSystem() {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "System");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Ritorna la stringa del path della cartella Tmp in Resources
        /// </summary>
        /// <returns>Path assoluto della directory Tmp in Resources</returns>
        public static string PathTmp () {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "Tmp");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Ritorna la stringa del path della cartella Host in Resources
        /// </summary>
        /// <returns>Path assoluto della directory Host in Resources</returns>
        public static string PathHost() {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "Host");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Ritorna il path dell'immagine di profilo contenuta nella cartella Host in Resources
        /// </summary>
        /// <param name="filename">Nome del file</param>
        /// <returns>Path assoluto del file se esiste</returns>
        public static string FileNameToHost(string filename) {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "Host");
            
            string[] files = Directory.GetFiles(archiveFolder, filename);

            if (files.Length > 0)
                return files[0];
            else
                return "";
        }

        /// <summary>
        /// Dal path assoluto ottiene il nome del file
        /// </summary>
        /// <param name="path">Path assoluto</param>
        /// <returns>Nome del file</returns>
        static public string PathToFileName(string path) {
            string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);

            return infoImage[infoImage.Length - 1];
        }

        /// <summary>
        /// Ritorna l'indirizzo multicast dell'indirizzo IP ricevuto come paramentro
        /// </summary>
        /// <param name="IP">Indirizzo IP</param>
        /// <returns>Indirizzo multicast</returns>
        static public string GetMulticastAddress(string IP) {
            string[] parts = IP.Split('.');
            parts[3] = "255";
            return string.Join(".", parts);
        }
    }
}