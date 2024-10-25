using System;
using System.Collections.Generic;
using System.Data.Entity; // Entity Framework için gerekli
using System.Linq;
using System.Web.Mvc;
using yillikizin.Models; // Model namespace'inizi ekleyin

namespace yillikizin.Controllers
{
    public class PersonelController : Controller
    {
        private readonly YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlantısı

        // Personel listesi
        public ActionResult Index()
        {
            var personelListesi = db.personel.ToList(); // Veritabanından personel listesini çek
            return View(personelListesi); // Listeyi view'a gönder
        }

        // Personel silme işlemi
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return HttpNotFound();
            }

            personel personel = db.personel.Find(id);
            if (personel == null)
            {
                return HttpNotFound();
            }

            db.personel.Remove(personel);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        // Personel detaylarını göster
        public ActionResult Details(int? id)
        {
            if (id == null || id == 0)
            {
                return HttpNotFound();
            }

            personel personel = db.personel.Find(id);
            if (personel == null)
            {
                return HttpNotFound();
            }

            return PartialView(personel);
        }

        // Yıllık izin bilgilerini getirme
        public ActionResult GetYillikIzin(int id)
        {
            var personel = db.personel.Find(id); // id'ye göre personeli buluyoruz
            if (personel == null)
            {
                return HttpNotFound();
            }

            var izinler = db.Izin.Where(x => x.Personelıd == id).ToList(); // ilgili personelin izin bilgileri
            return PartialView("_YillikIzin", izinler); // '_YillikIzin' adında bir partial view'e izin bilgilerini gönderiyoruz
        }

        // Düzenleme için Partial View dönen Edit Action'ı
        public ActionResult Edit(int? id)
        {
            if (id == null || id == 0)
            {
                return HttpNotFound();
            }

            var personel = db.personel.Find(id);
            if (personel == null)
            {
                return HttpNotFound();
            }

            // Departman listesini yükleyin
            ViewBag.DepartmanList = new SelectList(db.departman.ToList(), "departmanId", "departmanName", personel.departmanId);

            return PartialView("_EditPersonel", personel); // Kullanıcıyı burada PartialView'a yönlendirin
        }


        [HttpPost]
        public ActionResult Edit(personel personel)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Mevcut personel kaydını veritabanından alın
                    var existingPersonel = db.personel.Find(personel.id);
                    if (existingPersonel == null)
                    {
                        return HttpNotFound();
                    }

                    // Sadece adı ve soyadı güncelle
                    existingPersonel.adi = personel.adi;
                    existingPersonel.soyadi = personel.soyadi;

                    // Değişiklikleri kaydet
                    db.SaveChanges();

                    // Başarılı bir şekilde güncellendiğini belirt
                    return RedirectToAction("Index"); // veya güncellenen view'ye yönlendirin
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException e)
                {
                    // Hata mesajlarını loglayın
                    foreach (var validationErrors in e.EntityValidationErrors)
                    {
                        foreach (var validationError in validationErrors.ValidationErrors)
                        {
                            System.Diagnostics.Debug.WriteLine("Property: {0} Error: {1}", validationError.PropertyName, validationError.ErrorMessage);
                        }
                    }
                    ModelState.AddModelError("", "Validation failed. Check the logs for details.");
                }
            }

            return PartialView("_EditPersonel", personel);
        }



        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
