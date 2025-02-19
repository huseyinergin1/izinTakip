using Microsoft.Owin;
using Owin;
using Rotativa;
using System;
using System.IO;
using System.Web.Mvc;
using yillikizin.Models;

[assembly: OwinStartup(typeof(yillikizin.Startup))]
namespace yillikizin
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // OWIN yapılandırma kodları buraya eklenecek
            app.UseWelcomePage("/welcome"); // Örnek bir yapılandırma
        }
    }
}
