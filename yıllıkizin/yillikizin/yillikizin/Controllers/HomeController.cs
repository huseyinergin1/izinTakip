using yillikizin.Models;
using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Web.Helpers;

namespace yillikizin.Controllers
{
    public class HomeController : Controller
    {
        YillikizinEntities db = new YillikizinEntities();

        // GET: Home
        public ActionResult Index()
        {
            ViewBag.Title = "Yıllık İzin | 2024";
            return View();
        }
        public ActionResult PersonelEkle()
        {
            // Veritabanından departmanları çekiyoruz
            var departmanlar = db.departman.Select(d => new
            {
                departmanID = d.departmanId,
                departmanName = d.departmanName
            }).ToList();

            // Veritabanından kullanıcı gruplarını çekiyoruz
            var kullaniciGruplari = db.kullanici_grup.Select(g => new
            {
                grupID = g.kullaniciGrupId,
                grupAdi = g.grupAdi
            }).ToList();

            // Dropdown listesi için varsayılan "Seçiniz" seçeneği ekliyoruz
            ViewBag.DepartmanList = new SelectList(departmanlar, "departmanId", "departmanName", null);
            ViewBag.KullaniciGrupList = new SelectList(kullaniciGruplari, "grupID", "grupAdi", null);

            return View();
        }




        [HttpPost]
        public ActionResult PersonelEkle(personel Personel, HttpPostedFileBase File)
        {
            // Mevcut kullanıcı adı, kart numarası ve sicil numarasını kontrol et
            var mevcutKullanici = db.personel.FirstOrDefault(p => p.kullaniciadi == Personel.kullaniciadi);
            var mevcutKartNo = db.personel.FirstOrDefault(p => p.kartno == Personel.kartno);
            var mevcutSicilNo = db.personel.FirstOrDefault(p => p.sicilno == Personel.sicilno);

            if (mevcutKullanici != null)
            {
                ModelState.AddModelError("kullaniciadi", "Bu kullanıcı adı zaten alınmış.");
            }
            if (mevcutKartNo != null)
            {
                ModelState.AddModelError("kartno", "Bu kart numarası zaten alınmış.");
            }
            if (mevcutSicilNo != null)
            {
                ModelState.AddModelError("sicilno", "Bu sicil numarası zaten alınmış.");
            }

            if (!ModelState.IsValid)
            {
                var departmanlar = db.departman.ToList();
                ViewBag.DepartmanList = new SelectList(departmanlar, "departmanId", "departmanName");

                var kullaniciGruplari = db.kullanici_grup.ToList();
                ViewBag.KullaniciGrupList = new SelectList(kullaniciGruplari, "kullaniciGrupId", "grupAdi");

                return View(Personel);
            }

            // Resim yükleme işlemi
            if (File != null)
            {
                FileInfo fileinfo = new FileInfo(File.FileName);
                WebImage img = new WebImage(File.InputStream);
                string uzanti = (Guid.NewGuid().ToString() + fileinfo.Extension).ToLower();
                img.Resize(200, 180, false, false);
                string tamyol = "~/images/users/" + uzanti;
                img.Save(Server.MapPath(tamyol));
                Personel.resim = "/images/users/" + uzanti;
            }
            else
            {
                Personel.resim = "/images/users/user.png"; // Varsayılan resim
            }

            // Seçilen departmanId'yi personel modeline ata
            Personel.departmanId = int.Parse(Request.Form["departmanId"]);

            // Seçilen kullanıcı grup Id'sini personel modeline ata
            Personel.kullaniciGrupId = int.Parse(Request.Form["kullaniciGrupId"]);

            // Departman adı almak için departmanId ile karşılaştırma yap
            var departmanName = db.departman
                .Where(d => d.departmanId == Personel.departmanId)
                .Select(d => d.departmanName)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(departmanName))
            {
                Personel.departman = departmanName;
            }

            // Kullanıcı grubu adı almak için kullaniciGrupId ile karşılaştırma yap
            var grupAdi = db.kullanici_grup
                .Where(g => g.kullaniciGrupId == Personel.kullaniciGrupId)
                .Select(g => g.grupAdi)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(grupAdi))
            {
                Personel.kullaniciGrupAdi = grupAdi; // Personel tablosundaki grupAdi alanına yaz
            }

            // Yeni personeli ekle
            db.personel.Add(Personel);
            db.SaveChanges();

            return RedirectToAction("Index");
        }


        public ActionResult Departman()
        {
            ViewBag.Title = "Yıllık İzin | 2024";

            // Tüm departmanları listele
            var departmanlar = db.departman.ToList();
            return View(departmanlar);
        }

        // Departman Ekleme İşlemi (POST)
        [HttpPost]
        public ActionResult DepartmanEkle(string DepartmanAdi)
        {
            if (!string.IsNullOrEmpty(DepartmanAdi))
            {
                // Yeni departmanı oluştur
                departman yeniDepartman = new departman
                {
                    departmanName = DepartmanAdi
                };

                // Veritabanına ekle
                db.departman.Add(yeniDepartman);
                db.SaveChanges();

                // Başarılı ekleme sonrası aynı sayfaya geri dön
                return RedirectToAction("Departman");
            }

            // Departman adı boşsa hata mesajı ekleyip formu tekrar göster
            ModelState.AddModelError("", "Departman adı boş olamaz.");
            return View("Departman");
        }

        // Departman Silme İşlemi
        public ActionResult DepartmanSil(int id)
        {
            var departman = db.departman.Find(id);
            if (departman != null)
            {
                db.departman.Remove(departman);
                db.SaveChanges();
            }

            return RedirectToAction("Departman");
        }

        // Departman Düzenleme Sayfası (GET)
        public ActionResult DepartmanDuzenle(int id)
        {
            var departman = db.departman.Find(id);
            if (departman == null)
            {
                return HttpNotFound();
            }

            return View(departman);
        }

        // Departman Düzenleme İşlemi (POST)
        [HttpPost]
        public ActionResult DepartmanDuzenle(departman departman)
        {
            if (ModelState.IsValid)
            {
                // Veritabanında güncelle
                db.Entry(departman).State = System.Data.Entity.EntityState.Modified;
                db.SaveChanges();

                return RedirectToAction("Departman");
            }

            return View(departman);
        }

    }
}