using System;
using System.Collections.Generic;
using System.Data.Entity; // Entity Framework için gerekli
using System.Linq;
using System.Web;
using System.Web.Mvc;
using yillikizin.Filters;
using yillikizin.Models; // Model namespace'inizi ekleyin
using System.IO;
using DocumentFormat.OpenXml.EMMA;

namespace yillikizin.Controllers
{
    [CustomAuthorize]
    public class PersonelController : Controller
    {
        private readonly YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlantısı

        // Personel listesi
        // Controller
        public ActionResult Index(bool? calismaDurumu = null) // Varsayılan değeri null yapıyoruz
        {
            var personelListesi = db.personel.AsQueryable();

            // calismaDurumu parametresi gönderilmişse filtreleme yap
            if (calismaDurumu.HasValue)
            {
                personelListesi = personelListesi.Where(p => p.calisma == calismaDurumu.Value);
            }

            // ViewBag'e null durumunu da doğru şekilde geçiriyoruz
            ViewBag.CalismaDurumu = calismaDurumu.HasValue ?
                calismaDurumu.Value.ToString().ToLower() :
                string.Empty; // null yerine empty string kullanıyoruz

            return View(personelListesi.ToList());
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

            var personel = db.personel.Include(p => p.Vardiya).FirstOrDefault(p => p.id == id);
            if (personel == null)
            {
                return HttpNotFound();
            }

            // Departman listesini yükleyin
            ViewBag.DepartmanList = new SelectList(db.departman.ToList(), "departmanId", "departmanName", personel.departmanId);

            // Vardiya listesini yükleyin
            ViewBag.VardiyaList = new SelectList(db.Vardiya.ToList(), "vardiyaId", "Ad", personel.VardiyaId);

            return PartialView("_EditPersonel", personel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(personel personel, HttpPostedFileBase resimDosya)
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
                    // Çalışma durumu güncelleme - bu kısmı ekleyin
                    existingPersonel.calisma = personel.calisma;

                    // Çalışma durumuna göre işten çıkış tarihini güncelle - bu kısmı ekleyin
                    if (!personel.calisma) // Eğer çalışmıyorsa
                    {
                        existingPersonel.iscikistarih = personel.iscikistarih;
                    }
                    else // Eğer çalışıyorsa
                    {
                        existingPersonel.iscikistarih = null; // Çıkış tarihini temizle
                    }
                    // Personel bilgilerini güncelle
                    existingPersonel.kullaniciadi = personel.kullaniciadi;
                    existingPersonel.sifre = personel.sifre;
                    existingPersonel.adi = personel.adi;
                    existingPersonel.soyadi = personel.soyadi;
                    existingPersonel.dogumtarih = personel.dogumtarih;
                    existingPersonel.kartno = personel.kartno;
                    existingPersonel.sicilno = personel.sicilno;
                    existingPersonel.kan = personel.kan;
                    existingPersonel.ayakkabino = personel.ayakkabino;
                    existingPersonel.adres = personel.adres;
                    existingPersonel.gsm = personel.gsm;
                    existingPersonel.beden = personel.beden;
                    existingPersonel.iscikistarih = personel.iscikistarih;
                    existingPersonel.isegiristarih = personel.isegiristarih;
                    existingPersonel.unvan = personel.unvan;
                    existingPersonel.departmanId = personel.departmanId;
                    existingPersonel.VardiyaId = personel.VardiyaId;

                    // Departman ID değiştiğinde departman adını güncelle
                    if (personel.departmanId.HasValue)
                    {
                        var departman = db.departman.Find(personel.departmanId.Value);
                        if (departman != null)
                        {
                            existingPersonel.departman = departman.departmanName;
                        }
                    }
                    // Yeni resim dosyası yüklenmişse
                    if (resimDosya != null && resimDosya.ContentLength > 0)
                    {
                        try
                        {
                            // Mevcut resim dosyasını sil
                            if (!string.IsNullOrEmpty(existingPersonel.resim))
                            {
                                var existingFilePath = Server.MapPath(existingPersonel.resim);
                                if (System.IO.File.Exists(existingFilePath))
                                {
                                    System.IO.File.Delete(existingFilePath);
                                }
                            }

                            // Klasör yolunu belirleyin
                            var folderPath = Server.MapPath("~/images/users/");

                            // Klasörün mevcut olup olmadığını kontrol edin, yoksa oluşturun
                            if (!Directory.Exists(folderPath))
                            {
                                Directory.CreateDirectory(folderPath);
                            }

                            // Yeni dosya yolunu belirleyin
                            var fileName = Path.GetFileName(resimDosya.FileName);
                            var path = Path.Combine(folderPath, fileName);

                            // Dosyayı kaydedin
                            resimDosya.SaveAs(path);

                            // Personel resim yolunu güncelleyin
                            existingPersonel.resim = "/images/users/" + fileName;
                        }
                        catch (Exception ex)
                        {
                            ModelState.AddModelError("", "Dosya yükleme sırasında bir hata oluştu: " + ex.Message);
                            return View(existingPersonel);
                        }

                    }
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

            // Departman ve Vardiya listelerini yeniden yükleyin çünkü partial view tekrar döndürülecek
            ViewBag.DepartmanList = new SelectList(db.departman.ToList(), "departmanId", "departmanName", personel.departmanId);
            ViewBag.VardiyaList = new SelectList(db.Vardiya.ToList(), "vardiyaId", "Ad", personel.VardiyaId);

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
