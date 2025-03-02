using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace yillikizin.Controllers  // Buraya kendi proje namespace'inizi yazın
{
    public class OzelYetkiAttribute : AuthorizeAttribute
    {
        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            // Session kontrolü
            if (httpContext.Session["KullaniciGrupId"] == null)
            {
                return false;
            }

            int kullaniciGrupId = (int)httpContext.Session["KullaniciGrupId"];

            // Admin ise (Grup ID = 1) her şeye erişebilir
            if (kullaniciGrupId == 1)
            {
                return true;
            }

            // Normal personel ise (Grup ID = 2) sadece HareketDegerlendirme'ye erişebilir
            if (kullaniciGrupId == 2)
            {
                var routeData = httpContext.Request.RequestContext.RouteData;
                string controller = routeData.Values["controller"].ToString();
                string action = routeData.Values["action"].ToString();

                // Sadece Hareket controller'ında HareketDegerlendirme action'ına izin ver
                return (controller == "Hareket" && action == "HareketDegerlendirme");
            }

            return false;
        }
        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            if (filterContext.HttpContext.Session["KullaniciGrupId"] == null)
            {
                // Giriş yapmamış kullanıcıyı login sayfasına yönlendir
                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary {
                        { "controller", "Login" },
                        { "action", "Index" }
                    });
            }
            else
            {
                // Personeli HareketDegerlendirme sayfasına yönlendir
                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary {
                        { "controller", "Hareket" },
                        { "action", "HareketDegerlendirme" }
                    });
            }
        }
    }
}