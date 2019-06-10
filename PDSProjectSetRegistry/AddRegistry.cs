using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace PDSProjectSetRegistry {
    /// <summary>
    /// Add option "PDS_APP Invio File" to Win ContextMenù
    /// </summary>
    class AddRegistry {
        static void Main ( string[] args ) {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (principal.IsInRole(WindowsBuiltInRole.Administrator)) {
                string nameExe = "PDSProject.exe";
                string pathExe = FileNameToPath("", nameExe);

                // Set RegistryKey per file
                RegistryKey _key = Registry.ClassesRoot.OpenSubKey("*\\Shell", true);
                if (_key.GetSubKeyNames().Contains("PDSApp Invia File")) {
                    _key.Close();
                    return;
                }
                
                RegistryKey newkey = _key.CreateSubKey("PDSApp Invia File");
                RegistryKey subNewkey = newkey.CreateSubKey("Command");

                subNewkey.SetValue("", pathExe + " %1");

                // Set RegistryKey per directory
                _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
                if (_key.GetSubKeyNames().Contains("PDSApp Invia File")) {
                    subNewkey.Close();
                    newkey.Close();
                    _key.Close();
                    return;
                }

                newkey = _key.CreateSubKey("PDSApp Invia Cartella");
                subNewkey = newkey.CreateSubKey("Command");

                subNewkey.SetValue("", pathExe + " %1");

                subNewkey.Close();
                newkey.Close();
                _key.Close();
            }
        }

        /// <summary>
        /// Giving the file name it return the absolute path of the user's profile image
        /// </summary>
        /// <param name="directory">Directory in wich the file is located starting from the working directory</param>
        /// <param name="filename">File name</param>
        /// <returns>Absolute path of the file, if it exists</returns>
        static public string FileNameToPath ( string directory, string filename ) {
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
    }
}
