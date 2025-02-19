using System;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Filters;
using yillikizin.Models;

namespace yillikizin.Controllers
{
    [CustomAuthorize]
    public class KullaniciGruplariController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities();

        // GET: KullaniciGruplari
        public ActionResult Index()
        {
            // Tüm kullanıcı gruplarını al
            var kullaniciGrup = db.kullanici_grup.ToList();
            return View(kullaniciGrup);
        }

        // POST: KullaniciGruplari/Ekle
        [HttpPost]
        public ActionResult Ekle(string grupAdi, string aciklama, string yetkiler)
        {
            // Formdan gelen verilerin doğruluğunu kontrol et
            if (string.IsNullOrEmpty(grupAdi))
            {
                ModelState.AddModelError("", "Grup adı boş olamaz.");
                return RedirectToAction("Index");
            }

            try
            {
                // Yeni grup oluştur
                var yeniGrup = new kullanici_grup
                {
                    grupAdi = grupAdi,
                    Aciklama = aciklama,
                    Yetki = yetkiler
                };

                // Yeni grubu veritabanına ekle
                db.kullanici_grup.Add(yeniGrup);
                db.SaveChanges(); // Değişiklikleri kaydet
            }
            catch (Exception ex)
            {
                // Hata durumunda model hatası ekleyin
                ModelState.AddModelError("", "Grup eklenirken bir hata oluştu: " + ex.Message);
            }

            return RedirectToAction("Index"); // Her durumda Index'e yönlendir
        }
        public ActionResult Sil(int id)
        {
            var grup = db.kullanici_grup.Find(id); // İlgili grubu veritabanından bul
            if (grup != null)
            {
                db.kullanici_grup.Remove(grup); // Grubu sil
                db.SaveChanges(); // Değişiklikleri kaydet
            }

            return RedirectToAction("Index"); // Silme işleminden sonra tekrar listeye dön
        }

    }
}