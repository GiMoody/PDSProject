using System;

using System.IO;
using System.Reflection;

namespace PDSProject
{
    class Utility
    {
        /// <summary>
        /// Giving the file name it return the absolute path of the user's profile image
        /// </summary>
        /// <param name="directory">Directory in wich the file is located starting from the working directory</param>
        /// <param name="filename">File name</param>
        /// <returns>Absolute path of the file, if it exists</returns>
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
        /// Get the absolute path of a file contained in the "System" directory on the Application Resources
        /// </summary>
        /// <param name="filename">File Name</param>
        /// <returns>Absolute path, if it exists</returns>
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
        /// Return the absolute path of the "Resources" directory of the application
        /// </summary>
        /// <returns>Absolute path of the "Resources" directory</returns>
        public static string PathResources () {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Return the absolute path of the "System" directory of the application contained in Resources
        /// </summary>
        /// <returns>Absolute path of the "System" directory</returns>
        public static string PathSystem() {
            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "System");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Return the absolute path of the "Tmp" directory of the application contained in Resources
        /// </summary>
        /// <returns>Absolute path of the "Tmp" directory</returns>
        public static string PathTmp () {
        string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "Tmp");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Return the absolute path of the "Host" directory of the application contained in Resources
        /// </summary>
        /// <returns>Absolute path of the "Host" directory</returns>
        public static string PathHost() {
        string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            
            string archiveFolder = Path.Combine(currentDirectory, "Resources");
            archiveFolder = Path.Combine(archiveFolder, "Host");
            return archiveFolder.ToString();
        }

        /// <summary>
        /// Return the path of an profile image of a remote host contained in the "Host" directory in Resources
        /// </summary>
        /// <param name="filename">File name</param>
        /// <returns>Absolute path of the image</returns>
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
        /// Give the file name starting from the absolute path of the file
        /// </summary>
        /// <param name="path">Absolute path</param>
        /// <returns>File's name</returns>
        static public string PathToFileName(string path) {
            string[] infoImage = path.Split(new string[] { "\\" }, StringSplitOptions.None);

            return infoImage[infoImage.Length - 1];
        }

        /// <summary>
        /// Return the multicast address of an ip address
        /// </summary>
        /// <param name="IP">Ip address</param>
        /// <returns>Multicast address</returns>
        static public string GetMulticastAddress(string IP) {
            string[] parts = IP.Split('.');
            parts[3] = "255";
            return string.Join(".", parts);
        }
    }
}