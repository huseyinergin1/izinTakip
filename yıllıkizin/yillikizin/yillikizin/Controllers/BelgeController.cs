using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using yillikizin.Filters;
using yillikizin.Models;

namespace yillikizin.Controllers
{
    [OzelYetki]
    public class BelgeController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities(); // MSSQL bağlamınız

        // GET: Belge
        public ActionResult Index()
        {
            var personeller = db.personel.ToList(); // Tüm personelleri çek
            var belgeler = db.belge.ToList(); // Tüm belgeleri çek
            var model = new BelgeViewModel
            {
                Personeller = personeller,
                Belgeler = belgeler
            };
            return View(model);
        }

        [HttpPost]
        public ActionResult UploadFile(int personelId, string belgeAdi, HttpPostedFileBase dosya)
        {
            if (dosya != null && dosya.ContentLength > 0)
            {
                string belgeKlasoru = Server.MapPath("~/Belgeler/");
                if (!Directory.Exists(belgeKlasoru))
                {
                    Directory.CreateDirectory(belgeKlasoru);
                }

                string dosyaAdi = Path.GetFileName(dosya.FileName);
                string dosyaYolu = Path.Combine(belgeKlasoru, dosyaAdi);
                dosya.SaveAs(dosyaYolu);

                // Veritabanına belge bilgilerini kaydet
                var belge = new belge // Model adını kontrol edin
                {
                    personelId = personelId, // Doğru alan adını kullandığınızdan emin olun
                    belgeAdi = belgeAdi,
                    dosyaYolu = dosyaYolu,
                    yuklemeTarihi = DateTime.Now
                };
                db.belge.Add(belge);
                db.SaveChanges();

                return Json(new { success = true, message = "Dosya başarıyla yüklendi." });
            }

            return Json(new { success = false, message = "Dosya yüklenemedi." });
        }
        public FileResult DownloadFile(int id)
        {
            var belge = db.belge.Find(id);
            if (belge != null && System.IO.File.Exists(belge.dosyaYolu))
            {
                byte[] fileBytes = System.IO.File.ReadAllBytes(belge.dosyaYolu);
                string fileName = Path.GetFileName(belge.dosyaYolu);
                return File(fileBytes, System.Net.Mime.MediaTypeNames.Application.Octet, fileName);
            }
            return null; // Belge bulunamazsa null döndür
        }


        public ActionResult PreviewFile(int id)
        {
            var belge = db.belge.FirstOrDefault(b => b.belgeId == id);
            if (belge == null)
            {
                return Json(new { success = false, message = "Belge bulunamadı." });
            }

            // Veritabanında saklanan dosya yolunu kullan
            var filePath = Server.MapPath("~/" + belge.dosyaYolu);
            var fileExtension = Path.GetExtension(filePath).ToLower();

            // Desteklenen dosya formatlarını kontrol et
            if (fileExtension == ".pdf" || fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png" || fileExtension == ".xls" || fileExtension == ".xlsx")
            {
                return Json(new
                {
                    success = true,
                    fileUrl = Url.Content("~/" + belge.dosyaYolu), // URL'nin doğru olduğundan emin ol
                    fileType = fileExtension
                });
            }
            else
            {
                return Json(new { success = false, message = "Desteklenmeyen dosya formatı." });
            }
        }

        // Yeni metot: Personel belgelerini almak için
        [HttpGet]
        public JsonResult GetUploadedFiles(int personelId)
        {
            var belgeler = db.belge.Where(b => b.personelId == personelId)
                .Select(b => new
                {
                    b.belgeId,
                    b.belgeAdi
                })
                .ToList();

            return Json(new { success = true, files = belgeler }, JsonRequestBehavior.AllowGet);
        }
    }
}