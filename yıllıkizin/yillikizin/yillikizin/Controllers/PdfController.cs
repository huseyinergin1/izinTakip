using Rotativa;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace yillikizin.Controllers
{
    public class PdfController : Controller
    {
        public ActionResult GeneratePdf(DateTime? startDate, DateTime? endDate)
        {
            var pdfResult = new ActionAsPdf("HareketDegerlendirmeReport", new { StartDate = startDate, EndDate = endDate })
            {
                FileName = "Rapor.pdf"
            };
            return pdfResult;
        }
    }
}