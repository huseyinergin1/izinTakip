using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using yillikizin.Filters;

namespace yillikizin.Controllers
{
    [OzelYetki]
    public class AboutController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}