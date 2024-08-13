using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Loaders
{
    internal class Stuff
    {
        public static string GetObfuscatedName(string originalName)
        {
            string obfuscatedName;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte [] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalName));
                obfuscatedName = "O_" + BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);                
            }
            return obfuscatedName;
        }
    }
}
