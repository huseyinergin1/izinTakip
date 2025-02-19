using System;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Web;
using System.Web.Mvc;

namespace yillikizin.Filters
{
    public class CustomAuthorizeAttribute : AuthorizeAttribute
    {
        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            // Session kontrolü
            if (httpContext.Session["KullaniciAdi"] != null)
            {
                return true; // Kullanıcı giriş yapmış
            }

            return false; // Kullanıcı giriş yapmamış
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Yetkisiz kullanıcıyı login sayfasına yönlendir
            filterContext.Result = new RedirectResult("~/Login/Index");
        }
    }
}
