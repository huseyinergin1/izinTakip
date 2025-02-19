using System;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Models;

namespace yillikizin.Controllers
{
    public class VardiyaController : Controller
    {
        private readonly YillikizinEntities _context;
        private YillikizinEntities db = new YillikizinEntities();
        public VardiyaController()
        {
            _context = new YillikizinEntities();
        }

        // Index: Vardiya'ları listele
        public ActionResult Index()
        {
            using (var db = new YillikizinEntities())
            {
                var vardiyalar = new Vardiyalar
                {
                    VardiyaListesi = db.Vardiya.ToList()
                };
                return View(vardiyalar);
            }
        }

        // Vardiya ekleme işlemi
        [HttpPost]
        public ActionResult Ekle(Vardiya yeniVardiya)
        {
            ModelState.Remove("MesaiBaslangic");
            ModelState.Remove("MesaiBitis");
            ModelState.Remove("ErkenGelme");
            ModelState.Remove("GecGelme");
            ModelState.Remove("ErkenCikma");
            ModelState.Remove("GecCikma");
            ModelState.Remove("Aciklama");

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Zorunlu alanları doldurunuz.";
                return RedirectToAction("Index");
            }

            try
            {
                using (var db = new YillikizinEntities())
                {
                    db.Vardiya.Add(yeniVardiya);
                    db.SaveChanges();
                }
                return RedirectToAction("Index");
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException ex)
            {
                foreach (var validationErrors in ex.EntityValidationErrors)
                {
                    foreach (var validationError in validationErrors.ValidationErrors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Property: {validationError.PropertyName} Error: {validationError.ErrorMessage}");
                    }
                }
                TempData["ErrorMessage"] = "Veritabanı hatası oluştu. Lütfen tekrar deneyin.";
                return RedirectToAction("Index");
            }
        }

        public JsonResult GetVardiya(int id)
        {
            var vardiya = db.Vardiya.Find(id); // id ile ilgili vardiyayı bul
            if (vardiya == null)
            {
                return Json(new { success = false, message = "Vardiya bulunamadı." }, JsonRequestBehavior.AllowGet);
            }
            return Json(new
            {
                success = true,
                VardiyaId = vardiya.VardiyaId,
                Ad = vardiya.Ad,
                CalismaBaslangic = vardiya.CalismaBaslangic.ToString(@"hh\:mm"),
                CalismaBitis = vardiya.CalismaBitis.ToString(@"hh\:mm"),
                MesaiBaslangic = vardiya.MesaiBaslangic.ToString(@"hh\:mm"),
                MesaiBitis = vardiya.MesaiBitis.ToString(@"hh\:mm"),
                ErkenGelme = vardiya.ErkenGelme.ToString(@"hh\:mm"),
                GecGelme = vardiya.GecGelme.ToString(@"hh\:mm"),
                ErkenCikma = vardiya.ErkenCikma.ToString(@"hh\:mm"),
                GecCikma = vardiya.GecCikma.ToString(@"hh\:mm"),
                Aciklama = vardiya.Aciklama
            }, JsonRequestBehavior.AllowGet);
        }


        // Vardiya düzenleme işlemi
        [HttpPost]
        public ActionResult Duzenle(Vardiya vardiya)
        {
            if (!ModelState.IsValid)
            {
                return new HttpStatusCodeResult(400, "Geçersiz veri.");
            }

            try
            {
                using (var db = new YillikizinEntities())
                {
                    db.Entry(vardiya).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();
                }
                return new HttpStatusCodeResult(200);
            }
            catch (Exception)
            {
                return new HttpStatusCodeResult(500, "Veritabanı hatası oluştu.");
            }
        }

        // Vardiya silme işlemi
        [HttpPost]
        public ActionResult Sil(int id)
        {
            try
            {
                using (var db = new YillikizinEntities())
                {
                    var vardiya = db.Vardiya.Find(id);
                    if (vardiya == null)
                    {
                        return HttpNotFound();
                    }
                    db.Vardiya.Remove(vardiya);
                    db.SaveChanges();
                }
                return new HttpStatusCodeResult(200);
            }
            catch (Exception)
            {
                return new HttpStatusCodeResult(500, "Silme işlemi sırasında bir hata oluştu.");
            }
        }
    }
}