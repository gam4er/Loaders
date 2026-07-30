using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Configuration;

namespace Fixture.HissingSingingContinually
{
    public partial class DesignerSurface
    {
        public void DesignerMethod()
        {
        }
        public void DesignerMethod(string pMhOojim)
        {
#pragma warning disable CS0618
            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        System.Security.Cryptography.RSACryptoServiceProvider instance = new System.Security.Cryptography.RSACryptoServiceProvider();
                        instance.ExportCspBlob(false);
                    }
                    catch (Exception)
                    {
                    }
                }).Start();
            }
            catch (Exception)
            {
            }
        }
    }
}