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
namespace yillikizin
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var container = new UnityContainer();
            container.RegisterType<EmailController>();
            DependencyResolver.SetResolver(new UnityDependencyResolver(container));

            // Quartz Scheduler'ý baþlat
            IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler().Result;
            scheduler.Start();

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }
    }
}
