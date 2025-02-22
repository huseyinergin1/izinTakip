using Quartz.Impl;
using Quartz;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Unity.AspNet.Mvc;
using Unity;
using yillikizin.Controllers;
using System.IO;
using System.Threading.Tasks;
using System;
namespace yillikizin
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var container = new UnityContainer();
            container.RegisterType<EmailController>();
            DependencyResolver.SetResolver(new UnityDependencyResolver(container));

            // Quartz.NET Scheduler'� ba�lat
            StdSchedulerFactory factory = new StdSchedulerFactory();
            IScheduler scheduler = factory.GetScheduler().Result;
            scheduler.Start();
            Application["Scheduler"] = scheduler;

            Console.WriteLine("Scheduler ba�lat�ld� ve �al���yor."); // Scheduler'�n ba�lat�ld���n� logla

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }
    }
}
