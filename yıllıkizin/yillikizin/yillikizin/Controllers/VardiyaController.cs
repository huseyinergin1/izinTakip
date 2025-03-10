using System;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Models;

namespace yillikizin.Controllers
{
    [OzelYetki]
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
            ModelState.Remove("fmOpsiyon");
            ModelState.Remove("ErkenMesai");
            ModelState.Remove("SerbestCalisma"); // SerbestCalisma için ModelState'i kaldır
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
                    // Form değerlerini kontrol et
                    string erkenMesaiStr = Request.Form["ErkenMesai"];
                    string serbestCalismaStr = Request.Form["SerbestCalisma"];

                    // ErkenMesai checkbox değerini ayarla
                    yeniVardiya.ErkenMesai = !string.IsNullOrEmpty(erkenMesaiStr) &&
                                            (erkenMesaiStr.ToLower() == "true" || erkenMesaiStr == "on");

                    // SerbestCalisma checkbox değerini ayarla
                    yeniVardiya.serbestcalisma = !string.IsNullOrEmpty(serbestCalismaStr) &&
                                                (serbestCalismaStr.ToLower() == "true" || serbestCalismaStr == "on");

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
            var vardiya = db.Vardiya.Find(id);
            if (vardiya == null)
            {
                return Json(new { success = false, message = "Vardiya bulunamadı." }, JsonRequestBehavior.AllowGet);
            }

            return Json(new
            {
                success = true,
                VardiyaId = vardiya.VardiyaId,
                Ad = vardiya.Ad,
                CalismaBaslangic = string.Format("{0:hh\\:mm}", vardiya.CalismaBaslangic),
                CalismaBitis = string.Format("{0:hh\\:mm}", vardiya.CalismaBitis),
                MesaiBaslangic = string.Format("{0:hh\\:mm}", vardiya.MesaiBaslangic),
                MesaiBitis = string.Format("{0:hh\\:mm}", vardiya.MesaiBitis),
                ErkenGelme = string.Format("{0:hh\\:mm}", vardiya.ErkenGelme),
                GecGelme = string.Format("{0:hh\\:mm}", vardiya.GecGelme),
                ErkenCikma = string.Format("{0:hh\\:mm}", vardiya.ErkenCikma),
                GecCikma = string.Format("{0:hh\\:mm}", vardiya.GecCikma),
                fmOpsiyon = vardiya.fmOpsiyon,
                serbestcalisma = vardiya.serbestcalisma, // Değişken adı küçük harfle
                ErkenMesai = vardiya.ErkenMesai,
                Aciklama = vardiya.Aciklama
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult Duzenle(Vardiya vardiya)
        {
            try
            {
                using (var db = new YillikizinEntities())
                {
                    var mevcutVardiya = db.Vardiya.Find(vardiya.VardiyaId);
                    if (mevcutVardiya == null)
                    {
                        return new HttpStatusCodeResult(404, "Vardiya bulunamadı.");
                    }

                    // Form değerlerini kontrol et
                    string erkenMesaiStr = Request.Form["ErkenMesai"];
                    string serbestCalismaStr = Request.Form["SerbestCalisma"];

                    // İŞLEM SIRASI: Önce değerleri hazırla, sonra atama yap
                    bool erkenMesaiValue = !string.IsNullOrEmpty(erkenMesaiStr) &&
                                          (erkenMesaiStr.ToLower() == "true" || erkenMesaiStr == "on");

                    bool serbestCalismaValue = !string.IsNullOrEmpty(serbestCalismaStr) &&
                                              (serbestCalismaStr.ToLower() == "true" || serbestCalismaStr == "on");

                    // Değerleri güncelle
                    mevcutVardiya.Ad = vardiya.Ad;
                    mevcutVardiya.CalismaBaslangic = vardiya.CalismaBaslangic;
                    mevcutVardiya.CalismaBitis = vardiya.CalismaBitis;
                    mevcutVardiya.MesaiBaslangic = vardiya.MesaiBaslangic;
                    mevcutVardiya.MesaiBitis = vardiya.MesaiBitis;
                    mevcutVardiya.ErkenGelme = vardiya.ErkenGelme;
                    mevcutVardiya.GecGelme = vardiya.GecGelme;
                    mevcutVardiya.ErkenCikma = vardiya.ErkenCikma;
                    mevcutVardiya.GecCikma = vardiya.GecCikma;
                    mevcutVardiya.Aciklama = vardiya.Aciklama;
                    mevcutVardiya.fmOpsiyon = vardiya.fmOpsiyon;

                    // Checkbox değerlerini ayarla
                    mevcutVardiya.ErkenMesai = erkenMesaiValue;
                    mevcutVardiya.serbestcalisma = serbestCalismaValue;

                    db.SaveChanges();
                }
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Hata loglama
                System.Diagnostics.Debug.WriteLine($"Vardiya Düzenle Hatası: {ex.Message}");
                return new HttpStatusCodeResult(500, "Veritabanı hatası oluştu: " + ex.Message);
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