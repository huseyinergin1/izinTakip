using System.Web.Mvc;
using System.Web.Routing;

namespace yillikizin
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {

            // Default route
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Login", action = "Index", id = UrlParameter.Optional }
            );
            routes.MapRoute(
               name: "DevamsizRapor",
               url: "Hareket/DevamsizRapor",
           defaults: new { controller = "Hareket", action = "DevamsizRapor" }
           );
        }
    }
}
